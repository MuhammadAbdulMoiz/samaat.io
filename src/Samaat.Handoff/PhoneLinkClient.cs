using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Samaat.Core.Commands;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Link;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Pairing;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Storage;
using Samaat.Core.Diagnostics;

namespace Samaat.Handoff;

public enum ComputerLinkState
{
    /// <summary>No computer paired yet.</summary>
    NotPaired,

    /// <summary>Paired, waiting to hear the computer on the network.</summary>
    Searching,

    Connecting,

    Online,
}

public sealed record PhoneLinkClientOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>A computer counts as "nearby" for pairing while its beacons keep coming.</summary>
    public TimeSpan BeaconFreshness { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan RetryMin { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetryMax { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan KeepAlive { get; init; } = PeerChannel.DefaultKeepAlive;

    /// <summary>A phone-started move waits for the whole move on the computer.</summary>
    public TimeSpan MoveTimeout { get; init; } = TimeSpan.FromMinutes(3);
}

/// <summary>A computer heard on the network.</summary>
public sealed record NearbyComputer(string Id, string Name, IPEndPoint Endpoint, bool PairingOpen);

/// <summary>
/// The phone's side of the link: pairs with one computer, keeps a session to it whenever it's reachable, answers its
/// move requests through <see cref="PhoneAgent"/>, and asks it to move earbuds.
/// </summary>
public sealed class PhoneLinkClient : IAsyncDisposable
{
    private readonly PeerStore _store;
    private readonly string _phoneName;
    private readonly PhoneAgent _agent;
    private readonly IBeaconListener _beacons;
    private readonly TimeProvider _time;
    private readonly PhoneLinkClientOptions _options;
    private readonly ConcurrentDictionary<string, HeardBeacon> _heard = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _cts = new();
    private Task _loop = Task.CompletedTask;
    private PeerChannel? _channel;
    private volatile IReadOnlyList<PeerDevice> _computerDevices = [];
    private ComputerLinkState _state;

    public PhoneLinkClient(PeerStore store, string phoneName, PhoneAgent agent, IBeaconListener beacons, TimeProvider? time = null, PhoneLinkClientOptions? options = null)
    {
        _store = store;
        _phoneName = phoneName;
        _agent = agent;
        _beacons = beacons;
        _time = time ?? TimeProvider.System;
        _options = options ?? new PhoneLinkClientOptions();
        _state = Computer is null ? ComputerLinkState.NotPaired : ComputerLinkState.Searching;
        _beacons.Heard += OnBeacon;
        _agent.Audio.Changed += OnAudioChanged;
        _store.Changed += (_, _) => Wake();
    }

    /// <summary>The paired computer. The phone pairs with one computer at a time.</summary>
    public PairedPeer? Computer => _store.Peers.FirstOrDefault();

    public ComputerLinkState State => _state;

    /// <summary>The computer's earbuds as it last reported them, while online.</summary>
    public IReadOnlyList<PeerDevice> ComputerDevices => _computerDevices;

    /// <summary>Why the last connection attempt failed, in words for the screen; null once online.</summary>
    public string? LastError { get; private set; }

    /// <summary>Computers heard recently that have pairing open.</summary>
    public IReadOnlyList<NearbyComputer> PairableComputers
    {
        get
        {
            var fresh = _time.GetUtcNow() - _options.BeaconFreshness;
            return [.. _heard.Values
                .Where(h => h.Beacon.Pairing && h.HeardAt >= fresh)
                .OrderBy(h => h.Beacon.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(h => new NearbyComputer(h.Beacon.Id, h.Beacon.Name, h.Endpoint, true))];
        }
    }

    /// <summary>State, the computer's earbuds or nearby computers changed. Any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The computer reported progress on a move. Any thread.</summary>
    public event EventHandler<HandoffProgressMessage>? Progress;

    public void Start()
    {
        _beacons.Start();
        if (_loop.IsCompleted)
        {
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    /// <summary>Dials a computer with pairing open and exchanges keys. Show the session's code; then call <see cref="FinishPairingAsync"/>.</summary>
    /// <exception cref="HandshakeException">The computer refused or the connection failed; the message is for the screen.</exception>
    public async Task<PairingSession> BeginPairingAsync(IPEndPoint computer, CancellationToken ct = default)
    {
        var frames = await DialAsync(computer, ct);
        try
        {
            return await Handshake.PairAsPhoneAsync(frames, _store.SelfId, _phoneName, ct);
        }
        catch
        {
            await frames.DisposeAsync();
            throw;
        }
    }

    /// <summary>Sends this side's answer. When both accepted, the computer replaces any earlier one and the phone connects to it.</summary>
    public async Task<bool> FinishPairingAsync(PairingSession session, bool accept, TimeSpan? timeout = null, IPEndPoint? endpoint = null, CancellationToken ct = default)
    {
        await using (session)
        {
            var paired = await session.CompleteAsync(accept, timeout ?? TimeSpan.FromMinutes(2), ct);
            if (!paired)
            {
                return false;
            }

            foreach (var old in _store.Peers.Where(p => p.Id != session.PeerId))
            {
                _store.Remove(old.Id);
            }

            _store.Add(new PairedPeer(session.PeerId, session.PeerName, session.Secret, _time.GetUtcNow(), endpoint?.ToString()));
            LastError = null;
            Wake();
            return true;
        }
    }

    public async Task ForgetAsync()
    {
        foreach (var peer in _store.Peers)
        {
            _store.Remove(peer.Id);
        }

        if (_channel is { } channel)
        {
            await channel.DisposeAsync();
        }
    }

    /// <summary>Asks the computer to move earbuds. The computer runs the move and reports progress on <see cref="Progress"/>.</summary>
    public async Task<HandoffResult> RequestMoveAsync(string address, HandoffDirection direction, CancellationToken ct = default)
    {
        if (_channel is not { IsClosed: false } channel)
        {
            var name = Computer?.Name ?? "the computer";
            return new HandoffResult(HandoffOutcome.NotStarted, $"Can't reach {name}. Make sure Samaat is running there and both are on the same Wi-Fi.");
        }

        var reply = await channel.RequestAsync(new StartHandoffMessage(MessageCodec.NormalizeAddress(address), direction), _options.MoveTimeout, _time, ct);
        var outcome = reply.Outcome ?? (reply.Status == CommandStatus.Confirmed ? HandoffOutcome.Moved : HandoffOutcome.NotStarted);
        return new HandoffResult(outcome, reply.Message ?? reply.ToResult().UserMessage ?? "Moved.");
    }

    /// <summary>Wakes the connect loop now (the app came to the foreground, Wi-Fi changed).</summary>
    public void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var retry = _options.RetryMin;
        while (!ct.IsCancellationRequested)
        {
            var computer = Computer;
            if (computer is null)
            {
                SetState(ComputerLinkState.NotPaired);
                await WaitAsync(Timeout.InfiniteTimeSpan, ct);
                continue;
            }

            var endpoint = FindEndpoint(computer);
            if (endpoint is null)
            {
                SetState(ComputerLinkState.Searching);
                await WaitAsync(_options.RetryMax, ct);
                continue;
            }

            SetState(ComputerLinkState.Connecting);
            PeerChannel? channel = null;
            try
            {
                var frames = await DialAsync(endpoint, ct);
                try
                {
                    channel = await Handshake.ConnectAsPhoneAsync(frames, _store.SelfId, computer.Id, computer.Secret,
                        new HelloMessage(_phoneName, "android", MessageCodec.ProtocolVersion), ct);
                }
                catch
                {
                    await frames.DisposeAsync();
                    throw;
                }
            }
            catch (HandshakeException ex)
            {
                LastError = ex.Message;
                SamaatLog.Info("Phone link", "Connecting to the computer failed", ex);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            if (channel is null)
            {
                SetState(ComputerLinkState.Searching);
                await WaitAsync(retry, ct);
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, _options.RetryMax.Ticks));
                continue;
            }

            retry = _options.RetryMin;
            LastError = null;
            _store.UpdateEndpoint(computer.Id, endpoint.ToString());
            await RunSessionAsync(channel, ct);
        }
    }

    private async Task RunSessionAsync(PeerChannel channel, CancellationToken ct)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Closed += (_, _) => closed.TrySetResult();
        _channel = channel;
        channel.Start(HandleAsync, _options.KeepAlive, _time);
        SetState(ComputerLinkState.Online);
        await SendDevicesAsync();
        try
        {
            await closed.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Disposing.
        }

        _channel = null;
        _computerDevices = [];
        await channel.DisposeAsync();
        if (!ct.IsCancellationRequested)
        {
            SetState(ComputerLinkState.Searching);
        }
    }

    private async Task<ReplyMessage?> HandleAsync(PeerMessage message, CancellationToken ct)
    {
        switch (message)
        {
            case DeviceListMessage list:
                _computerDevices = [.. list.Devices.Select(d => d with { Address = MessageCodec.NormalizeAddress(d.Address) })];
                Changed?.Invoke(this, EventArgs.Empty);
                return null;
            case HandoffProgressMessage progress:
                Progress?.Invoke(this, progress);
                return null;
            default:
                return await _agent.HandleAsync(message, ct);
        }
    }

    private async Task SendDevicesAsync()
    {
        if (_channel is not { IsClosed: false } channel)
        {
            return;
        }

        try
        {
            await channel.SendAsync(new DeviceListMessage(_agent.ListDevices()));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Dropped; the loop reconnects and sends the list again.
        }
    }

    private void OnAudioChanged(object? sender, EventArgs e) => _ = SendDevicesAsync();

    private IPEndPoint? FindEndpoint(PairedPeer computer)
    {
        if (_heard.TryGetValue(computer.Id, out var heard) && heard.HeardAt >= _time.GetUtcNow() - _options.BeaconFreshness)
        {
            return heard.Endpoint;
        }

        return computer.Endpoint is { } stored && IPEndPoint.TryParse(stored, out var endpoint) ? endpoint : heard?.Endpoint;
    }

    private void OnBeacon(object? sender, HeardBeacon heard)
    {
        var previous = _heard.TryGetValue(heard.Beacon.Id, out var old) ? old : null;
        _heard[heard.Beacon.Id] = heard;
        if (previous is null || (heard.Beacon.Id == Computer?.Id && _state != ComputerLinkState.Online && !Equals(previous.Endpoint, heard.Endpoint)))
        {
            Wake();
        }

        if (previous is null || previous.Beacon.Pairing != heard.Beacon.Pairing || previous.Beacon.Name != heard.Beacon.Name
            || !Equals(previous.Endpoint, heard.Endpoint))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>What the phone shows when it can't open a connection to the computer.</summary>
    internal static string DialError(SocketError error) => error == SocketError.ConnectionRefused
        ? "The computer refused the connection. Check that Samaat is running on it."
        : "Couldn't reach the computer. Check that both are on the same Wi-Fi.";

    private async Task<FrameStream> DialAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var client = new TcpClient(endpoint.AddressFamily) { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.ConnectTimeout);
        try
        {
            await client.ConnectAsync(endpoint, timeout.Token);
            return new FrameStream(client.GetStream());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new HandshakeException("The computer didn't answer. Check that both are on the same Wi-Fi.");
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new HandshakeException(DialError(ex.SocketErrorCode));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await _wake.WaitAsync(timeout, ct);
        }
        catch (OperationCanceledException)
        {
            // Disposing; the loop checks the token.
        }
    }

    private void SetState(ComputerLinkState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        _beacons.Heard -= OnBeacon;
        _agent.Audio.Changed -= OnAudioChanged;
        await _cts.CancelAsync();
        await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (_channel is { } channel)
        {
            await channel.DisposeAsync();
        }

        await _beacons.DisposeAsync();
        _cts.Dispose();
        _wake.Dispose();
    }
}
