using System.Net;
using System.Net.Sockets;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Pairing;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Security;
using Samaat.Handoff.Storage;

namespace Samaat.Handoff.Tests;

/// <summary>Counts beacons on their way to the in-memory bus.</summary>
internal sealed class CountingBeaconSender(IBeaconSender inner) : IBeaconSender
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public Task SendAsync(Beacon beacon, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _count);
        return inner.SendAsync(beacon, ct);
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

/// <summary>
/// Phone-link hardening from the repository audit (2026-11-05, issue 6): pairing re-checked after the handshake, a cap on
/// unauthenticated handshakes, beacons only while pairing is open, and the plaintext store readable by its owner only.
/// </summary>
public sealed class HardeningTests : IAsyncDisposable
{
    private readonly InMemoryBeaconBus _bus = new();
    private readonly List<IAsyncDisposable> _owned = [];

    private PhoneLinkHost Host(PeerStore? store = null, IBeaconSender? beacons = null, int port = 0, int maxHandshakes = PhoneLinkHostOptions.DefaultMaxPendingHandshakes)
    {
        var host = new PhoneLinkHost(store ?? new PeerStore(null),
            new PhoneLinkHostOptions("Desk PC")
            {
                BindAddress = IPAddress.Loopback, Port = port, BeaconInterval = TimeSpan.FromMilliseconds(50), MaxPendingHandshakes = maxHandshakes,
            },
            () => [], beacons ?? _bus.CreateSender());
        _owned.Add(host);
        return host;
    }

    private PhoneLinkClient Phone(string name, PeerStore? store = null, PhoneLinkClientOptions? options = null)
    {
        var phone = new PhoneLinkClient(store ?? new PeerStore(null), name, new PhoneAgent(new FakePhoneAudio()), _bus.CreateListener(), options: options);
        _owned.Add(phone);
        return phone;
    }

    private static IPEndPoint Endpoint(PhoneLinkHost host) => new(IPAddress.Loopback, host.Port!.Value);

    [Fact]
    public async Task PairingClosedDuringTheHandshake_NoPrompt_AndTheConnectionIsDropped()
    {
        // A phone is already paired, so closing pairing doesn't stop the listener (and with it every connection).
        var store = new PeerStore(null);
        store.Add(new PairedPeer("phone", "Galaxy", [1, 2, 3], DateTimeOffset.UnixEpoch));
        var host = Host(store);
        var prompts = 0;
        host.PairingRequested += (_, _) => Interlocked.Increment(ref prompts);
        host.Start();
        host.OpenPairing();

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(Endpoint(host));
        await using var frames = new FrameStream(tcp.GetStream());
        using var key = new PairingKey();
        var nonce = KeyDerivation.NewNonce();
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.PairStart)
        {
            PeerId = "late", Name = "Late phone", Commitment = KeyDerivation.Commitment(key.PublicKey, nonce),
        }, CancellationToken.None);
        (await Handshake.ReadStepAsync(frames, CancellationToken.None)).Expect(HandshakeFrame.PairKey);

        // The user closes pairing (or its window runs out) while this phone is mid-handshake.
        host.ClosePairing();
        host.OpenPairing(); // and even reopens it: the late phone's handshake began in the old window
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.PairReveal) { PublicKey = key.PublicKey, Nonce = nonce }, CancellationToken.None);

        await Assert.ThrowsAsync<HandshakeException>(() => Handshake.ReadStepAsync(frames, CancellationToken.None));
        Assert.Equal(0, Volatile.Read(ref prompts));

        // The dropped pairing gave its slot back.
        var session = await Phone("Pixel").BeginPairingAsync(Endpoint(host));
        await Eventually.TrueAsync(() => Volatile.Read(ref prompts) == 1, "the next phone's prompt");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task UnauthenticatedHandshakes_AreCapped_AndTheRestRefused()
    {
        var host = Host(maxHandshakes: 4);
        host.Start();
        host.OpenPairing();

        // Four connections that never say anything hold the four handshake places...
        var idle = new List<TcpClient>();
        for (var i = 0; i < 4; i++)
        {
            var c = new TcpClient();
            await c.ConnectAsync(Endpoint(host));
            idle.Add(c);
        }

        await Eventually.TrueAsync(() => host.PendingHandshakes == 4, "four handshakes in progress");

        // ...so a fifth is closed straight away instead of being served.
        using var fifth = new TcpClient();
        await fifth.ConnectAsync(Endpoint(host));
        var buffer = new byte[16];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await ReadOrResetAsync(fifth, buffer, timeout.Token);
        Assert.Equal(0, read);
        Assert.Equal(4, host.PendingHandshakes);

        // Once they go, phones get through again.
        idle.ForEach(c => c.Dispose());
        await Eventually.TrueAsync(() => host.PendingHandshakes == 0, "the idle handshakes to end");
        var session = await Phone("Pixel").BeginPairingAsync(Endpoint(host));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Beacons_GoOutOnlyWhilePairingIsOpen_NotBecauseAPhoneIsPaired()
    {
        var store = new PeerStore(null);
        store.Add(new PairedPeer("phone", "Pixel", [1, 2, 3], DateTimeOffset.UnixEpoch));
        var beacons = new CountingBeaconSender(_bus.CreateSender());
        var host = Host(store, beacons);

        host.Start();
        Assert.True(host.IsListening);
        await Task.Delay(300);
        Assert.Equal(0, beacons.Count);

        host.OpenPairing();
        await Eventually.TrueAsync(() => beacons.Count >= 2, "beacons while pairing is open");

        host.ClosePairing();
        await Task.Delay(100); // a beacon already on its way may land
        var afterClosing = beacons.Count;
        await Task.Delay(300);
        Assert.Equal(afterClosing, beacons.Count);
        Assert.True(host.IsListening, "a paired phone can still dial in");
    }

    [Fact]
    public async Task WithoutBeacons_APairedPhoneReconnects_ToItsRememberedAddress()
    {
        var computerStore = new PeerStore(null);
        var phoneStore = new PeerStore(null);
        var host = Host(computerStore);
        // Beacons go stale at once, so the phone can only use the address and port it stored when it paired.
        var phone = Phone("Pixel", phoneStore, new PhoneLinkClientOptions
        {
            BeaconFreshness = TimeSpan.FromTicks(1), RetryMin = TimeSpan.FromMilliseconds(50), RetryMax = TimeSpan.FromMilliseconds(200),
        });
        PairingRequest? request = null;
        host.PairingRequested += (_, r) => request = r;
        host.Start();
        phone.Start();
        host.OpenPairing();
        var endpoint = Endpoint(host);
        var session = await phone.BeginPairingAsync(endpoint);
        await Eventually.TrueAsync(() => request is not null, "the prompt");
        var answers = await Task.WhenAll(phone.FinishPairingAsync(session, true, endpoint: endpoint), request!.AcceptAsync());
        Assert.Equal([true, true], answers);
        await Eventually.TrueAsync(() => phone.State == ComputerLinkState.Online, "the phone to connect");

        // The computer restarts on the same port, pairing closed: it sends no beacons at all.
        await host.DisposeAsync();
        await Eventually.TrueAsync(() => phone.State != ComputerLinkState.Online, "the phone to notice");
        var beacons = new CountingBeaconSender(_bus.CreateSender());
        var again = Host(computerStore, beacons, port: endpoint.Port);
        again.Start();

        await Eventually.TrueAsync(() => phone.State == ComputerLinkState.Online, "the phone to reconnect", 15_000);
        Assert.Equal(0, beacons.Count);
    }

    [Fact]
    public void PlaintextStore_IsReadableByItsOwnerOnly_OutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows keeps secrets with DPAPI, and %APPDATA% is already per user.
        }

        var dir = Path.Combine(Path.GetTempPath(), "samaat-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "phones.json");
            File.WriteAllText(path, "{}");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            new PeerStore(path).Add(new PairedPeer("phone", "Pixel", [1, 2, 3], DateTimeOffset.UnixEpoch));

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Single(new PeerStore(path).Peers);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<int> ReadOrResetAsync(TcpClient client, byte[] buffer, CancellationToken ct)
    {
        try
        {
            return await client.GetStream().ReadAsync(buffer, ct);
        }
        catch (IOException)
        {
            return 0; // a reset is a refusal too
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var owned in Enumerable.Reverse(_owned))
        {
            await owned.DisposeAsync();
        }
    }
}
