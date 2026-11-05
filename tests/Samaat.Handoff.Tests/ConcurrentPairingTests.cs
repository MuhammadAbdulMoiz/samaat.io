using System.Net;
using System.Net.Sockets;
using Samaat.Handoff;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Pairing;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Security;
using Samaat.Handoff.Storage;

namespace Samaat.Handoff.Tests;

/// <summary>
/// Pairing is one phone at a time. Found by the repository audit (2026-11-05): phones that arrived together all passed
/// the "already pairing" check before any of them finished the handshake, so each got its own prompt on the computer,
/// the earlier sessions were orphaned, and any of the prompts could still be accepted.
/// </summary>
public sealed class ConcurrentPairingTests : IAsyncDisposable
{
    private readonly InMemoryBeaconBus _bus = new();
    private readonly PhoneLinkHost _host;
    private readonly List<PhoneLinkClient> _phones = [];
    private int _prompts;
    private PairingRequest? _lastPrompt;

    public ConcurrentPairingTests()
    {
        _host = new PhoneLinkHost(new PeerStore(null),
            new PhoneLinkHostOptions("Desk PC") { BindAddress = IPAddress.Loopback, Port = 0, BeaconInterval = TimeSpan.FromMilliseconds(100) },
            () => [], _bus.CreateSender());
        _host.PairingRequested += (_, r) =>
        {
            Interlocked.Increment(ref _prompts);
            _lastPrompt = r;
        };
        _host.Start();
        _host.OpenPairing();
    }

    private IPEndPoint Endpoint => new(IPAddress.Loopback, _host.Port!.Value);

    private PhoneLinkClient Phone(string name)
    {
        var phone = new PhoneLinkClient(new PeerStore(null), name, new PhoneAgent(new FakePhoneAudio()), _bus.CreateListener());
        _phones.Add(phone);
        return phone;
    }

    private static async Task<(PairingSession? Session, HandshakeException? Error)> TryBeginAsync(PhoneLinkClient phone, IPEndPoint endpoint)
    {
        try
        {
            return (await phone.BeginPairingAsync(endpoint), null);
        }
        catch (HandshakeException ex)
        {
            return (null, ex);
        }
    }

    [Fact]
    public async Task ThreePhonesAtOnce_GetOnePrompt_AndTheOthersAreToldWhy()
    {
        var phones = new[] { Phone("Pixel"), Phone("Galaxy"), Phone("iPhone") };
        var endpoint = Endpoint;

        var results = await Task.WhenAll(phones.Select(p => Task.Run(() => TryBeginAsync(p, endpoint))));

        Assert.Single(results, r => r.Session is not null);
        Assert.Equal(2, results.Count(r => r.Error is { Refused: true } e && e.Message.Contains("already pairing", StringComparison.Ordinal)));
        await Eventually.TrueAsync(() => Volatile.Read(ref _prompts) >= 1, "the prompt");
        await Task.Delay(200); // a late second prompt would show up here
        Assert.Equal(1, Volatile.Read(ref _prompts));

        foreach (var (session, _) in results.Where(r => r.Session is not null))
        {
            await session!.DisposeAsync();
        }
    }

    [Fact]
    public async Task AfterTheComputerDeclines_AnotherPhoneCanPair()
    {
        var first = await Phone("Pixel").BeginPairingAsync(Endpoint);
        await Eventually.TrueAsync(() => _lastPrompt is not null, "the first prompt");
        await _lastPrompt!.DeclineAsync();
        await first.DisposeAsync();

        var second = await Phone("Galaxy").BeginPairingAsync(Endpoint);

        await Eventually.TrueAsync(() => Volatile.Read(ref _prompts) == 2, "the second phone's prompt");
        Assert.Equal("Galaxy", _lastPrompt!.PhoneName);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task APhoneThatHangsUpMidHandshake_DoesNotHoldThePairingSlot()
    {
        // Start a pairing by hand and walk away after the computer sends its key.
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(Endpoint);
            await using var frames = new FrameStream(tcp.GetStream());
            using var key = new PairingKey();
            await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.PairStart)
            {
                PeerId = "abandoned",
                Name = "Gone",
                Commitment = KeyDerivation.Commitment(key.PublicKey, KeyDerivation.NewNonce()),
            }, CancellationToken.None);
            (await Handshake.ReadStepAsync(frames, CancellationToken.None)).Expect(HandshakeFrame.PairKey);
        }

        // The computer notices the hang-up and frees the slot, so the next phone gets through.
        PairingSession? session = null;
        var deadline = Environment.TickCount64 + 10_000;
        while (session is null)
        {
            Assert.True(Environment.TickCount64 < deadline, "a new phone should get past the abandoned handshake");
            session = (await TryBeginAsync(Phone("Pixel"), Endpoint)).Session;
            if (session is null)
            {
                await Task.Delay(50);
            }
        }

        await Eventually.TrueAsync(() => Volatile.Read(ref _prompts) == 1, "one prompt, for the phone that stayed");
        Assert.Equal("Pixel", _lastPrompt!.PhoneName);
        await session!.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var phone in _phones)
        {
            await phone.DisposeAsync();
        }

        await _host.DisposeAsync();
    }
}
