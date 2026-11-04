using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Samaat.Handoff.Protocol;

namespace Samaat.Handoff.Discovery;

/// <summary>What the computer announces on the local network. No secrets: anyone on the network can see it.</summary>
public sealed record Beacon(int V, string Id, string Name, int Port, bool Pairing)
{
    public const int DefaultPort = 47820;

    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this, HandoffJson.Default.Beacon);

    public static Beacon? Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            var beacon = JsonSerializer.Deserialize(data, HandoffJson.Default.Beacon);
            return beacon is { V: MessageCodec.ProtocolVersion, Id.Length: > 0, Port: > 0 and < 65536 } ? beacon : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A beacon and where it came from.</summary>
public sealed record HeardBeacon(Beacon Beacon, IPEndPoint Endpoint, DateTimeOffset HeardAt);

/// <summary>Announces this computer. The UDP implementation broadcasts; tests use an in-memory bus.</summary>
public interface IBeaconSender : IAsyncDisposable
{
    Task SendAsync(Beacon beacon, CancellationToken ct = default);
}

/// <summary>Hears computers' beacons on the local network.</summary>
public interface IBeaconListener : IAsyncDisposable
{
    /// <summary>Raised for each beacon. <see cref="HeardBeacon.Endpoint"/> is the computer's TCP endpoint. Any thread.</summary>
    event EventHandler<HeardBeacon>? Heard;

    void Start();
}

/// <summary>Broadcasts beacons to every IPv4 network this computer is on.</summary>
public sealed class UdpBeaconSender(int port = Beacon.DefaultPort) : IBeaconSender
{
    private readonly UdpClient _udp = new(AddressFamily.InterNetwork) { EnableBroadcast = true };

    public async Task SendAsync(Beacon beacon, CancellationToken ct = default)
    {
        var data = beacon.Encode();
        foreach (var address in BroadcastAddresses())
        {
            try
            {
                await _udp.SendAsync(data, new IPEndPoint(address, port), ct);
            }
            catch (SocketException)
            {
                // One network refusing (no route, VPN) mustn't stop the others.
            }
        }
    }

    /// <summary>255.255.255.255 often leaves by one interface only, so each network's own broadcast address is added.</summary>
    internal static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                    {
                        continue;
                    }

                    var ip = unicast.Address.GetAddressBytes();
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (var i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    }

                    addresses.Add(new IPAddress(broadcast));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Fall back to the limited broadcast address alone.
        }

        return addresses;
    }

    public ValueTask DisposeAsync()
    {
        _udp.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Listens for beacons on the beacon port. On Android the platform must hold a Wi-Fi multicast lock meanwhile.</summary>
public sealed class UdpBeaconListener(int port = Beacon.DefaultPort, TimeProvider? time = null) : IBeaconListener
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private UdpClient? _udp;
    private Task _loop = Task.CompletedTask;

    public event EventHandler<HeardBeacon>? Heard;

    public void Start()
    {
        if (_udp is not null)
        {
            return;
        }

        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _udp = udp;
        _loop = Task.Run(() => LoopAsync(udp, _cts.Token));
    }

    private async Task LoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (Beacon.Decode(received.Buffer) is { } beacon)
            {
                Heard?.Invoke(this, new HeardBeacon(beacon, new IPEndPoint(received.RemoteEndPoint.Address, beacon.Port), _time.GetUtcNow()));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _udp?.Dispose();
        await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _cts.Dispose();
    }
}

/// <summary>In-process beacons, for tests and the simulator: what one sends, every listener hears from 127.0.0.1.</summary>
public sealed class InMemoryBeaconBus(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<Listener> _listeners = [];
    private readonly Lock _gate = new();

    public IBeaconSender CreateSender() => new Sender(this);

    public IBeaconListener CreateListener()
    {
        var listener = new Listener(this);
        lock (_gate)
        {
            _listeners.Add(listener);
        }

        return listener;
    }

    public void Publish(Beacon beacon)
    {
        Listener[] listeners;
        lock (_gate)
        {
            listeners = [.. _listeners];
        }

        var heard = new HeardBeacon(beacon, new IPEndPoint(IPAddress.Loopback, beacon.Port), _time.GetUtcNow());
        foreach (var listener in listeners)
        {
            listener.Deliver(heard);
        }
    }

    private void Remove(Listener listener)
    {
        lock (_gate)
        {
            _listeners.Remove(listener);
        }
    }

    private sealed class Sender(InMemoryBeaconBus bus) : IBeaconSender
    {
        public Task SendAsync(Beacon beacon, CancellationToken ct = default)
        {
            bus.Publish(beacon);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Listener(InMemoryBeaconBus bus) : IBeaconListener
    {
        private bool _started;

        public event EventHandler<HeardBeacon>? Heard;

        public void Start() => _started = true;

        public void Deliver(HeardBeacon heard)
        {
            if (_started)
            {
                Heard?.Invoke(this, heard);
            }
        }

        public ValueTask DisposeAsync()
        {
            bus.Remove(this);
            return ValueTask.CompletedTask;
        }
    }
}
