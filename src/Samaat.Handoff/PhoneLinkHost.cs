using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Link;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Pairing;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Storage;

namespace Samaat.Handoff;

public sealed record PhoneLinkHostOptions(string ComputerName)
{
    public const int DefaultPort = 47821;

    /// <summary>The TCP port to listen on; if it's taken, any free port is used and the beacon says which.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Any for real use; tests bind to loopback.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    public TimeSpan BeaconInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan PairingWindow { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PairingAnswerTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan KeepAlive { get; init; } = PeerChannel.DefaultKeepAlive;
}

/// <summary>A paired phone and what it last reported.</summary>
public sealed record PhoneStatus(string Id, string Name, bool Online, IReadOnlyList<PeerDevice> Devices)
{
    public PeerDevice? FindDevice(string address)
    {
        var wanted = MessageCodec.NormalizeAddress(address);
        return Devices.FirstOrDefault(d => d.Address == wanted);
    }
}

/// <summary>A phone wants to pair. Show <see cref="Code"/> and let the user accept or decline.</summary>
public sealed class PairingRequest
{
    private readonly PairingSession _session;
    private readonly Func<PairingSession, bool, Task<bool>> _answer;

    internal PairingRequest(PairingSession session, Func<PairingSession, bool, Task<bool>> answer)
    {
        _session = session;
        _answer = answer;
    }

    public string PhoneName => _session.PeerName;

    public string Code => _session.Code;

    /// <summary>Completes false when the phone declines or goes away, so the prompt can close.</summary>
    public Task<bool> PhoneDecision => _session.PeerDecision;

    /// <summary>True once both sides accepted and the phone is stored.</summary>
    public Task<bool> AcceptAsync() => _answer(_session, true);

    public Task DeclineAsync() => _answer(_session, false);
}

/// <summary>A move under way, wherever it was started. For the computer's UI.</summary>
public sealed record HandoffActivity(string Address, string PhoneId, HandoffDirection Direction, HandoffProgress Progress);

/// <summary>
/// The computer's side of the phone link: listens for paired phones, announces itself, runs pairing,
/// and moves earbuds with <see cref="HandoffCoordinator"/>. It listens only while a phone is paired or pairing is open.
/// </summary>
public sealed class PhoneLinkHost : IAsyncDisposable
{
    private readonly PeerStore _store;
    private readonly PhoneLinkHostOptions _options;
    private readonly Func<IReadOnlyList<IEarbudDevice>> _devices;
    private readonly IBeaconSender _beacons;
    private readonly TimeProvider _time;
    private readonly HandoffCoordinator _coordinator;
    private readonly HandoffTimeouts _timeouts;
    private readonly ConcurrentDictionary<string, PhoneSession> _sessions = new();
    private readonly ConcurrentDictionary<string, byte> _moving = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposed = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _listening;
    private Task _acceptLoop = Task.CompletedTask;
    private Task _beaconLoop = Task.CompletedTask;
    private ITimer? _pairingTimer;
    private int? _lastPort;
    // The one pairing at a time: null, a reservation while a phone's handshake runs, or the PairingSession waiting for answers.
    // Taken under _gate before the handshake starts, so phones that arrive together can't all get a prompt.
    private object? _pairingSlot;
    private bool _pairingOpen;
    private bool _started;

    public PhoneLinkHost(
        PeerStore store,
        PhoneLinkHostOptions options,
        Func<IReadOnlyList<IEarbudDevice>> devices,
        IBeaconSender? beacons = null,
        TimeProvider? time = null,
        HandoffTimeouts? timeouts = null)
    {
        _store = store;
        _options = options;
        _devices = devices;
        _beacons = beacons ?? new UdpBeaconSender();
        _time = time ?? TimeProvider.System;
        _timeouts = timeouts ?? HandoffTimeouts.Default;
        _coordinator = new HandoffCoordinator(_time, _timeouts);
        _store.Changed += (_, _) => { UpdateListening(); RaiseChanged(); };
    }

    public string ComputerId => _store.SelfId;

    public string ComputerName => _options.ComputerName;

    /// <summary>The port phones dial, while listening.</summary>
    public int? Port { get; private set; }

    public bool IsListening => Port is not null;

    /// <summary>Why listening failed (port blocked, no network), or null.</summary>
    public string? ListenError { get; private set; }

    public bool IsPairingOpen
    {
        get
        {
            lock (_gate)
            {
                return _pairingOpen;
            }
        }
    }

    public IReadOnlyList<PhoneStatus> Phones =>
        [.. _store.Peers.Select(p => _sessions.TryGetValue(p.Id, out var s) && !s.Channel.IsClosed
            ? new PhoneStatus(p.Id, p.Name, true, s.Devices)
            : new PhoneStatus(p.Id, p.Name, false, []))];

    /// <summary>Phones, their connection or their earbud lists changed. Any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>A phone wants to pair while pairing is open. Any thread.</summary>
    public event EventHandler<PairingRequest>? PairingRequested;

    /// <summary>A move made progress, including moves started on the phone. Any thread.</summary>
    public event EventHandler<HandoffActivity>? Activity;

    public void Start()
    {
        _started = true;
        UpdateListening();
    }

    public void OpenPairing()
    {
        lock (_gate)
        {
            _pairingOpen = true;
            _pairingTimer?.Dispose();
            _pairingTimer = _time.CreateTimer(_ => ClosePairing(), null, _options.PairingWindow, Timeout.InfiniteTimeSpan);
        }

        UpdateListening();
        RaiseChanged();
        _ = SendBeaconAsync();
    }

    public void ClosePairing()
    {
        lock (_gate)
        {
            _pairingOpen = false;
            _pairingTimer?.Dispose();
            _pairingTimer = null;
        }

        UpdateListening();
        RaiseChanged();
    }

    public void Forget(string phoneId)
    {
        if (_sessions.TryRemove(phoneId, out var session))
        {
            _ = session.Channel.DisposeAsync();
        }

        _store.Remove(phoneId);
    }

    public bool IsMoving(string address) => _moving.ContainsKey(MessageCodec.NormalizeAddress(address));

    /// <summary>Moves <paramref name="device"/> to or from the phone. One move per pair of earbuds at a time.</summary>
    public async Task<HandoffResult> MoveAsync(
        IEarbudDevice device, string phoneId, HandoffDirection direction, IProgress<HandoffProgress>? progress = null, CancellationToken ct = default)
    {
        var name = _store.Find(phoneId)?.Name ?? "The phone";
        if (!_sessions.TryGetValue(phoneId, out var session) || session.Channel.IsClosed)
        {
            return new HandoffResult(HandoffOutcome.NotStarted, $"{name} isn't connected. Open Samaat on the phone, on the same Wi-Fi as this computer.");
        }

        var address = device.Identity.Details?.Address is { } a ? MessageCodec.NormalizeAddress(a) : device.Identity.Id;
        if (!_moving.TryAdd(address, 0))
        {
            return new HandoffResult(HandoffOutcome.NotStarted, $"{device.Identity.Name} are already being moved.");
        }

        try
        {
            var relay = new RelayProgress(this, session, address, direction, progress);
            return await _coordinator.RunAsync(device, session, direction, relay, ct);
        }
        finally
        {
            _moving.TryRemove(address, out _);
            RaiseChanged();
            // Every phone should see where the earbuds ended up, whatever the UI does.
            await PublishDevicesAsync();
        }
    }

    /// <summary>Tells connected phones that this computer's earbuds changed (connected, added, removed).</summary>
    public async Task PublishDevicesAsync()
    {
        var list = new DeviceListMessage(ListOwnDevices());
        foreach (var session in _sessions.Values)
        {
            try
            {
                await session.Channel.SendAsync(list);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // That phone just dropped; its session cleans itself up.
            }
        }
    }

    private IReadOnlyList<PeerDevice> ListOwnDevices() =>
        [.. _devices()
            .Where(d => d.Identity.Details?.Address is { Length: > 0 })
            .Select(d =>
            {
                var canLink = d.GetFeature<IPcLinkFeature>() is not null;
                return new PeerDevice(MessageCodec.NormalizeAddress(d.Identity.Details!.Address!), d.Identity.Name,
                    d.Connection == ConnectionState.Connected, canLink, canLink);
            })];

    private void UpdateListening()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        var wanted = _started && (IsPairingOpen || _store.Peers.Count > 0);
        lock (_gate)
        {
            if (wanted && _listener is null)
            {
                StartListening();
            }
            else if (!wanted && _listener is not null)
            {
                StopListening();
            }
        }
    }

    private void StartListening()
    {
        TcpListener listener;
        try
        {
            // Come back on the same port after a pause, so phones that remember it still find us.
            listener = new TcpListener(_options.BindAddress, _lastPort ?? _options.Port);
            listener.Start();
        }
        catch (SocketException)
        {
            try
            {
                listener = new TcpListener(_options.BindAddress, 0);
                listener.Start();
            }
            catch (SocketException ex)
            {
                ListenError = $"Samaat couldn't listen for your phone: {ex.Message}";
                return;
            }
        }

        ListenError = null;
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _lastPort = Port;
        _listening = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
        var token = _listening.Token;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, token));
        _beaconLoop = Task.Run(() => BeaconLoopAsync(token));
    }

    private void StopListening()
    {
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;
        _listener?.Stop();
        _listener = null;
        Port = null;
        foreach (var session in _sessions.Values)
        {
            _ = session.Channel.DisposeAsync();
        }

        _sessions.Clear();
    }

    private async Task BeaconLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SendBeaconAsync();
            try
            {
                await Task.Delay(_options.BeaconInterval, _time, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SendBeaconAsync()
    {
        if (Port is not { } port)
        {
            return;
        }

        try
        {
            await _beacons.SendAsync(new Beacon(MessageCodec.ProtocolVersion, ComputerId, ComputerName, port, IsPairingOpen));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // No network right now; the next beacon tries again.
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        var frames = new FrameStream(client.GetStream());
        var handedOff = false;
        try
        {
            var first = await Handshake.ReadStepAsync(frames, ct);
            switch (first.Kind)
            {
                case HandshakeFrame.PairStart:
                    handedOff = await HandlePairingAsync(frames, first, ct);
                    break;
                case HandshakeFrame.Hello:
                    handedOff = await HandleSessionAsync(frames, first, ct);
                    break;
                default:
                    await Handshake.RefuseAsync(frames, "Unexpected first message.", ct);
                    break;
            }
        }
        catch (Exception ex) when (ex is HandshakeException or IOException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // A failed or abandoned handshake just closes the connection.
        }
        finally
        {
            if (!handedOff)
            {
                await frames.DisposeAsync();
                client.Dispose();
            }
        }
    }

    private async Task<bool> HandlePairingAsync(FrameStream frames, HandshakeFrame start, CancellationToken ct)
    {
        // Check and take the slot in one step: checking, then taking it after the handshake let every phone that
        // arrived during the handshake through, each with its own prompt.
        var reservation = new object();
        string? refusal;
        lock (_gate)
        {
            refusal = !_pairingOpen
                ? $"Pairing isn't open on {ComputerName}. In Samaat on the computer, open Settings and choose Pair a phone."
                : _pairingSlot is not null
                    ? $"{ComputerName} is already pairing with another phone. Try again in a moment."
                    : null;
            if (refusal is null)
            {
                _pairingSlot = reservation;
            }
        }

        if (refusal is not null)
        {
            // Awaited, so the phone gets the reason before the connection closes.
            await Handshake.RefuseAsync(frames, refusal, ct);
            return false;
        }

        PairingSession session;
        try
        {
            session = await Handshake.PairAsComputerAsync(frames, start, ComputerId, ComputerName, ct);
        }
        catch
        {
            // The phone hung up or sent something wrong: free the slot for the next phone.
            ReleaseReservation(reservation);
            throw;
        }

        lock (_gate)
        {
            _pairingSlot = session;
        }

        _ = session.PeerDecision.ContinueWith(async t =>
        {
            // The phone declined or left before we answered: drop the prompt.
            if (!t.Result)
            {
                await EndPairingAsync(session);
            }
        }, TaskScheduler.Default);

        PairingRequested?.Invoke(this, new PairingRequest(session, AnswerPairingAsync));
        return true;
    }

    private async Task<bool> AnswerPairingAsync(PairingSession session, bool accept)
    {
        var paired = false;
        try
        {
            paired = await session.CompleteAsync(accept, _options.PairingAnswerTimeout);
            if (paired)
            {
                _store.Add(new PairedPeer(session.PeerId, session.PeerName, session.Secret, _time.GetUtcNow()));
                ClosePairing();
            }
        }
        catch (InvalidOperationException)
        {
            // Answered twice (two clicks); the first answer stands.
        }
        finally
        {
            await EndPairingAsync(session);
        }

        return paired;
    }

    private void ReleaseReservation(object reservation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pairingSlot, reservation))
            {
                _pairingSlot = null;
            }
        }
    }

    private async Task EndPairingAsync(PairingSession session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_pairingSlot, session))
            {
                return;
            }

            _pairingSlot = null;
        }

        await session.DisposeAsync();
        RaiseChanged();
    }

    private async Task<bool> HandleSessionAsync(FrameStream frames, HandshakeFrame hello, CancellationToken ct)
    {
        if (hello.PeerId is not { } phoneId || _store.Find(phoneId) is not { } peer)
        {
            await Handshake.RefuseAsync(frames, $"This phone isn't paired with {ComputerName} any more. Pair it again.", ct);
            return false;
        }

        var channel = await Handshake.AcceptAsComputerAsync(frames, hello, ComputerId, peer.Secret,
            new HelloMessage(ComputerName, "windows", MessageCodec.ProtocolVersion), ct);
        var session = new PhoneSession(this, peer.Id, peer.Name, channel);
        if (_sessions.TryGetValue(phoneId, out var old))
        {
            // The phone reconnected (new Wi-Fi, app restarted): the new session wins.
            await old.Channel.DisposeAsync();
        }

        _sessions[phoneId] = session;
        channel.Closed += (_, _) =>
        {
            _sessions.TryRemove(new KeyValuePair<string, PhoneSession>(phoneId, session));
            RaiseChanged();
        };
        channel.Start(session.HandleAsync, _options.KeepAlive, _time);
        await channel.SendAsync(new DeviceListMessage(ListOwnDevices()), ct);
        RaiseChanged();
        return true;
    }

    private async Task<ReplyMessage?> HandleStartHandoffAsync(PhoneSession session, StartHandoffMessage request, CancellationToken ct)
    {
        var address = MessageCodec.NormalizeAddress(request.Address);
        var device = _devices().FirstOrDefault(d => d.Identity.Details?.Address is { } a && MessageCodec.NormalizeAddress(a) == address);
        if (device is null)
        {
            return new ReplyMessage(request.Id, CommandStatus.Rejected,
                $"{ComputerName} doesn't have these earbuds paired.", HandoffOutcome.NotStarted);
        }

        var result = await MoveAsync(device, session.Id, request.Direction, null, ct);
        return new ReplyMessage(request.Id, result.Succeeded ? CommandStatus.Confirmed : CommandStatus.Rejected, result.Message, result.Outcome);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        await _disposed.CancelAsync();
        lock (_gate)
        {
            _pairingTimer?.Dispose();
            if (_listener is not null)
            {
                StopListening();
            }
        }

        await _acceptLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await _beaconLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (_pairingSlot is PairingSession pairing)
        {
            await pairing.DisposeAsync();
        }

        await _beacons.DisposeAsync();
    }

    /// <summary>Forwards progress to the caller, the computer's UI and the phone's screen.</summary>
    private sealed class RelayProgress(PhoneLinkHost host, PhoneSession session, string address, HandoffDirection direction, IProgress<HandoffProgress>? inner)
        : IProgress<HandoffProgress>
    {
        public void Report(HandoffProgress value)
        {
            inner?.Report(value);
            host.Activity?.Invoke(host, new HandoffActivity(address, session.Id, direction, value));
            _ = SendAsync(value);
        }

        private async Task SendAsync(HandoffProgress value)
        {
            try
            {
                await session.Channel.SendAsync(new HandoffProgressMessage(address, direction, value.Stage, value.Text));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The phone dropped mid-move; the coordinator sees that on its next request.
            }
        }
    }

    /// <summary>One connected phone.</summary>
    private sealed class PhoneSession(PhoneLinkHost host, string id, string name, PeerChannel channel) : IPhoneEndpoint
    {
        private volatile IReadOnlyList<PeerDevice> _devices = [];

        public string Id { get; } = id;

        public string Name { get; } = name;

        public PeerChannel Channel { get; } = channel;

        public IReadOnlyList<PeerDevice> Devices => _devices;

        public PeerDevice? FindDevice(string address)
        {
            var wanted = MessageCodec.NormalizeAddress(address);
            return _devices.FirstOrDefault(d => d.Address == wanted);
        }

        public async Task<ReplyMessage?> HandleAsync(PeerMessage message, CancellationToken ct)
        {
            switch (message)
            {
                case DeviceListMessage list:
                    _devices = [.. list.Devices.Select(d => d with { Address = MessageCodec.NormalizeAddress(d.Address) })];
                    host.RaiseChanged();
                    return null;
                case StartHandoffMessage start:
                    return await host.HandleStartHandoffAsync(this, start, ct);
                default:
                    return null;
            }
        }

        public async Task<CommandResult> PrepareAsync(string address, HandoffDirection direction, TimeSpan timeout, CancellationToken ct) =>
            (await Channel.RequestAsync(new PrepareMessage(address, direction), timeout, host._time, ct)).ToResult();

        public async Task<CommandResult> ConnectAudioAsync(string address, TimeSpan timeout, CancellationToken ct) =>
            (await Channel.RequestAsync(new ConnectAudioMessage(address), timeout, host._time, ct)).ToResult();

        public async Task<CommandResult> ReleaseAudioAsync(string address, TimeSpan timeout, CancellationToken ct) =>
            (await Channel.RequestAsync(new ReleaseAudioMessage(address), timeout, host._time, ct)).ToResult();
    }
}
