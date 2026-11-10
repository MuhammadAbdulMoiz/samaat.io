using System.Net;
using System.Net.Sockets;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Protocol;

namespace Samaat.Handoff.Tests;

/// <summary>Earbuds on this computer whose connect and disconnect return whatever the test says.</summary>
internal sealed class FakeLinkDevice : IEarbudDevice, IPcLinkFeature
{
    public const string DefaultAddress = "AA:BB:CC:DD:EE:01";

    public FakeLinkDevice(string address = DefaultAddress, ConnectionState connection = ConnectionState.Connected, bool hasLink = true)
    {
        Identity = new DeviceIdentity("fake-" + address, "Test Buds", Details: new DeviceDetails { Address = address });
        Connection = connection;
        Features = hasLink ? [this] : [];
    }

    public DeviceIdentity Identity { get; set; }

    public ConnectionState Connection { get; set; }

    public IReadOnlyCollection<IFeature> Features { get; }

    public event EventHandler? ConnectionChanged;

    public Queue<CommandResult> ConnectResults { get; } = new();

    public Queue<CommandResult> DisconnectResults { get; } = new();

    public List<string> Calls { get; } = [];

    /// <summary>Set to make a call never finish (for timeouts).</summary>
    public bool Hang { get; set; }

    /// <summary>When set, disconnect waits for it (ignoring cancellation, like a real Bluetooth call) before finishing.</summary>
    public TaskCompletionSource? DisconnectGate { get; set; }

    public Task<CommandResult> ConnectAsync(CancellationToken ct = default) => Run("pc.connect", ConnectResults, ConnectionState.Connected, ct);

    public Task<CommandResult> DisconnectAsync(CancellationToken ct = default) => Run("pc.disconnect", DisconnectResults, ConnectionState.Disconnected, ct);

    private async Task<CommandResult> Run(string name, Queue<CommandResult> results, ConnectionState after, CancellationToken ct)
    {
        Calls.Add(name);
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (name == "pc.disconnect" && DisconnectGate is { } gate)
        {
            await gate.Task;
        }

        var result = results.Count > 0 ? results.Dequeue() : CommandResult.Confirmed;
        if (result.Succeeded)
        {
            Connection = after;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }
}

/// <summary>The phone, as the coordinator sees it, with scripted answers.</summary>
internal sealed class FakePhoneEndpoint(List<string> calls) : IPhoneEndpoint
{
    public string Name => "Test Phone";

    public Dictionary<string, PeerDevice> Devices { get; } = new();

    public Queue<CommandResult> PrepareResults { get; } = new();

    public Queue<CommandResult> ConnectResults { get; } = new();

    public Queue<CommandResult> ReleaseResults { get; } = new();

    public bool HangConnect { get; set; }

    public PeerDevice? FindDevice(string address) => Devices.GetValueOrDefault(address);

    public Task<CommandResult> PrepareAsync(string address, HandoffDirection direction, TimeSpan timeout, CancellationToken ct)
    {
        calls.Add("phone.prepare");
        return Task.FromResult(PrepareResults.Count > 0 ? PrepareResults.Dequeue() : CommandResult.Confirmed);
    }

    public async Task<CommandResult> ConnectAudioAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        calls.Add("phone.connect");
        if (HangConnect)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        return ConnectResults.Count > 0 ? ConnectResults.Dequeue() : CommandResult.Confirmed;
    }

    public Task<CommandResult> ReleaseAudioAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        calls.Add("phone.release");
        return Task.FromResult(ReleaseResults.Count > 0 ? ReleaseResults.Dequeue() : CommandResult.Confirmed);
    }
}

/// <summary>The phone's Bluetooth audio, in memory.</summary>
internal sealed class FakePhoneAudio : IPhoneAudioLink
{
    private readonly Lock _gate = new();
    private List<PhoneAudioDevice> _devices = [];

    public bool IsBluetoothOn { get; set; } = true;

    public string? Blocker { get; set; }

    public IReadOnlyList<PhoneAudioDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return [.. _devices];
            }
        }
    }

    public Queue<CommandResult> ConnectResults { get; } = new();

    public List<string> Calls { get; } = [];

    /// <summary>Set to make connect never finish, ignoring cancellation too (the earbuds never show up, the platform call never returns).</summary>
    public bool HangConnect { get; set; }

    /// <summary>When set, connect waits for it (ignoring cancellation), then finishes as usual.</summary>
    public TaskCompletionSource? ConnectGate { get; set; }

    public event EventHandler? Changed;

    public void TurnBluetoothOff()
    {
        IsBluetoothOn = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public FakePhoneAudio With(string address, bool connected, string name = "Test Buds")
    {
        lock (_gate)
        {
            _devices = [.. _devices.Where(d => d.Address != address), new PhoneAudioDevice(address, name, connected)];
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return this;
    }

    public async Task<CommandResult> ConnectAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add("connect " + address);
        if (HangConnect)
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
        }

        if (ConnectGate is { } gate)
        {
            await gate.Task; // ignores cancellation, like a platform call that lands late
        }

        var result = ConnectResults.Count > 0 ? ConnectResults.Dequeue() : CommandResult.Confirmed;
        if (result.Succeeded)
        {
            Set(address, true);
        }

        return result;
    }

    public Task<CommandResult> DisconnectAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add("disconnect " + address);
        Set(address, false);
        return Task.FromResult(CommandResult.Confirmed);
    }

    private void Set(string address, bool connected)
    {
        var name = Devices.FirstOrDefault(d => d.Address == address)?.Name ?? "Test Buds";
        With(address, connected, name);
    }
}

/// <summary>Two connected frame streams over loopback TCP, like the real link.</summary>
internal sealed class LoopbackPair : IAsyncDisposable
{
    private LoopbackPair(FrameStream client, FrameStream server)
    {
        Client = client;
        Server = server;
    }

    public FrameStream Client { get; }

    public FrameStream Server { get; }

    public static async Task<LoopbackPair> CreateAsync(Func<Stream, Stream>? wrapClient = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var client = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            var server = await accept;
            return new LoopbackPair(new FrameStream(wrapClient?.Invoke(client.GetStream()) ?? client.GetStream()), new FrameStream(server.GetStream()));
        }
        finally
        {
            listener.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}

internal static class Eventually
{
    /// <summary>Polls until <paramref name="condition"/> holds, for state that changes on other threads.</summary>
    public static async Task TrueAsync(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail($"Timed out waiting for: {what}");
            }

            await Task.Delay(20);
        }
    }
}
