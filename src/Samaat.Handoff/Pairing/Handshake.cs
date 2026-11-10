using System.Security.Cryptography;
using System.Text.Json;
using Samaat.Handoff.Link;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Security;

namespace Samaat.Handoff.Pairing;

/// <summary>The plain frames sent before any key exists. Which fields are set depends on <see cref="Kind"/>.</summary>
public sealed record HandshakeFrame(string Kind)
{
    public const string PairStart = "pair-start";
    public const string PairKey = "pair-key";
    public const string PairReveal = "pair-reveal";
    public const string Hello = "hello";
    public const string HelloAck = "hello-ack";
    public const string Refused = "refused";

    public int Version { get; init; } = MessageCodec.ProtocolVersion;

    public string? PeerId { get; init; }

    public string? Name { get; init; }

    public byte[]? Commitment { get; init; }

    public byte[]? PublicKey { get; init; }

    public byte[]? Nonce { get; init; }

    /// <summary>Why the other side refused, in words for the screen.</summary>
    public string? Error { get; init; }

    internal static async Task SendAsync(FrameStream frames, HandshakeFrame frame, CancellationToken ct) =>
        await frames.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(frame, HandoffJson.Default.HandshakeFrame), ct);

    internal static async Task<HandshakeFrame> ReadAsync(FrameStream frames, CancellationToken ct)
    {
        var payload = await frames.ReadAsync(ct) ?? throw new HandshakeException("The other device closed the connection.");
        try
        {
            return JsonSerializer.Deserialize(payload, HandoffJson.Default.HandshakeFrame)
                ?? throw new HandshakeException("The other device sent an empty message.");
        }
        catch (JsonException)
        {
            throw new HandshakeException("The other device isn't speaking Samaat's protocol.");
        }
    }

    internal HandshakeFrame Expect(string kind)
    {
        if (Kind == Refused)
        {
            throw new HandshakeException(Error ?? "The other device refused.") { Refused = true };
        }

        return Kind == kind ? this : throw new HandshakeException($"Expected '{kind}' but got '{Kind}'.");
    }
}

public sealed record PairConfirmFrame(bool Accepted);

/// <summary>A handshake or pairing step failed. <see cref="Exception.Message"/> is fit for the screen.</summary>
public sealed class HandshakeException(string message) : Exception(message)
{
    /// <summary>The other side said no on purpose (as opposed to a broken connection).</summary>
    public bool Refused { get; init; }
}

/// <summary>
/// One pairing attempt after keys are exchanged: both screens show <see cref="Code"/>; each user accepts or declines.
/// Paired only when both accept. Dispose closes the connection.
/// </summary>
public sealed class PairingSession : IAsyncDisposable
{
    private readonly FrameStream _frames;
    private readonly SessionCipher _send;
    private readonly SessionCipher _receive;
    private readonly CancellationTokenSource _cts = new();
    private int _decided;

    internal PairingSession(FrameStream frames, string peerId, string peerName, int code, byte[] secret, byte[] sendKey, byte[] receiveKey)
    {
        _frames = frames;
        PeerId = peerId;
        PeerName = peerName;
        Code = KeyDerivation.FormatCode(code);
        Secret = secret;
        _send = new SessionCipher(sendKey);
        _receive = new SessionCipher(receiveKey);
        PeerDecision = ReadPeerDecisionAsync(_cts.Token);
    }

    public string PeerId { get; }

    public string PeerName { get; }

    /// <summary>"123 456": the same on both screens unless someone is in the middle.</summary>
    public string Code { get; }

    /// <summary>Store this only when <see cref="CompleteAsync"/> returned true.</summary>
    public byte[] Secret { get; }

    /// <summary>The other user's answer: true accepted, false declined or the connection dropped.</summary>
    public Task<bool> PeerDecision { get; }

    /// <summary>Sends this side's answer and waits for the other side's. True when both accepted.</summary>
    public async Task<bool> CompleteAsync(bool accept, TimeSpan timeout, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _decided, 1) != 0)
        {
            throw new InvalidOperationException("This pairing was already answered.");
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new PairConfirmFrame(accept), HandoffJson.Default.PairConfirmFrame);
            await _frames.WriteAsync(_send.Seal(payload), ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return false;
        }

        if (!accept)
        {
            return false;
        }

        try
        {
            return await PeerDecision.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task<bool> ReadPeerDecisionAsync(CancellationToken ct)
    {
        try
        {
            var frame = await _frames.ReadAsync(ct);
            if (frame is null)
            {
                return false;
            }

            var confirm = JsonSerializer.Deserialize(_receive.Open(frame), HandoffJson.Default.PairConfirmFrame);
            return confirm?.Accepted == true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException
            or CryptographicException or JsonException or InvalidDataException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _frames.DisposeAsync();
        await ((Task)PeerDecision).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _send.Dispose();
        _receive.Dispose();
        _cts.Dispose();
    }
}

/// <summary>The handshakes, phone side and computer side. The phone always dials the computer.</summary>
public static class Handshake
{
    /// <summary>How long one handshake step may take before the connection is dropped.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Phone: start pairing with a computer that has pairing open.</summary>
    public static async Task<PairingSession> PairAsPhoneAsync(FrameStream frames, string phoneId, string phoneName, CancellationToken ct = default)
    {
        using var key = new PairingKey();
        var publicKey = key.PublicKey;
        var nonce = KeyDerivation.NewNonce();
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.PairStart)
        {
            PeerId = phoneId,
            Name = phoneName,
            Commitment = KeyDerivation.Commitment(publicKey, nonce),
        }, ct);

        var reply = (await ReadStepAsync(frames, ct)).Expect(HandshakeFrame.PairKey);
        var computerKey = Required(reply.PublicKey, "key");
        var computerNonce = RequiredNonce(reply.Nonce);
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.PairReveal) { PublicKey = publicKey, Nonce = nonce }, ct);

        var shared = Derive(key, computerKey);
        var secret = KeyDerivation.PairingSecret(shared, nonce, computerNonce);
        var code = KeyDerivation.ComparisonCode(shared, publicKey, computerKey, nonce, computerNonce);
        var (toComputer, toPhone) = KeyDerivation.PairConfirmKeys(secret, nonce, computerNonce);
        return new PairingSession(frames, Required(reply.PeerId, "id"), Discovery.Beacon.Clip(reply.Name ?? "Computer"), code, secret, toComputer, toPhone);
    }

    /// <summary>Computer: answer a <see cref="HandshakeFrame.PairStart"/> already read from the connection.</summary>
    public static async Task<PairingSession> PairAsComputerAsync(FrameStream frames, HandshakeFrame start, string computerId, string computerName, CancellationToken ct = default)
    {
        var commitment = Required(start.Commitment, "commitment");
        var phoneId = Required(start.PeerId, "id");
        using var key = new PairingKey();
        var publicKey = key.PublicKey;
        var nonce = KeyDerivation.NewNonce();
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.PairKey)
        {
            PeerId = computerId,
            Name = computerName,
            PublicKey = publicKey,
            Nonce = nonce,
        }, ct);

        var reveal = (await ReadStepAsync(frames, ct)).Expect(HandshakeFrame.PairReveal);
        var phoneKey = Required(reveal.PublicKey, "key");
        var phoneNonce = RequiredNonce(reveal.Nonce);
        if (!CryptographicOperations.FixedTimeEquals(commitment, KeyDerivation.Commitment(phoneKey, phoneNonce)))
        {
            throw new HandshakeException("The phone's key didn't match what it promised. Try pairing again.");
        }

        var shared = Derive(key, phoneKey);
        var secret = KeyDerivation.PairingSecret(shared, phoneNonce, nonce);
        var code = KeyDerivation.ComparisonCode(shared, phoneKey, publicKey, phoneNonce, nonce);
        var (toComputer, toPhone) = KeyDerivation.PairConfirmKeys(secret, phoneNonce, nonce);
        return new PairingSession(frames, phoneId, Discovery.Beacon.Clip(start.Name ?? "Phone"), code, secret, toPhone, toComputer);
    }

    /// <summary>Phone: open an encrypted session with a computer it's paired with.</summary>
    public static async Task<PeerChannel> ConnectAsPhoneAsync(
        FrameStream frames, string phoneId, string expectedComputerId, byte[] secret, HelloMessage hello, CancellationToken ct = default)
    {
        var nonce = KeyDerivation.NewNonce();
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.Hello) { PeerId = phoneId, Nonce = nonce }, ct);
        var ack = (await ReadStepAsync(frames, ct)).Expect(HandshakeFrame.HelloAck);
        if (ack.PeerId != expectedComputerId)
        {
            throw new HandshakeException("A different computer answered at that address.");
        }

        var (toComputer, toPhone) = KeyDerivation.SessionKeys(secret, nonce, RequiredNonce(ack.Nonce));
        return await PeerChannel.OpenAsync(frames, toComputer, toPhone, hello, ct);
    }

    /// <summary>Computer: answer a <see cref="HandshakeFrame.Hello"/> already read; <paramref name="secret"/> is the phone's stored secret.</summary>
    public static async Task<PeerChannel> AcceptAsComputerAsync(
        FrameStream frames, HandshakeFrame hello, string computerId, byte[] secret, HelloMessage ownHello, CancellationToken ct = default)
    {
        var nonce = KeyDerivation.NewNonce();
        await HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.HelloAck) { PeerId = computerId, Nonce = nonce }, ct);
        var (toComputer, toPhone) = KeyDerivation.SessionKeys(secret, RequiredNonce(hello.Nonce), nonce);
        return await PeerChannel.OpenAsync(frames, toPhone, toComputer, ownHello, ct);
    }

    public static Task RefuseAsync(FrameStream frames, string error, CancellationToken ct = default) =>
        HandshakeFrame.SendAsync(frames, new HandshakeFrame(HandshakeFrame.Refused) { Error = error }, ct);

    public static async Task<HandshakeFrame> ReadStepAsync(FrameStream frames, CancellationToken ct)
    {
        try
        {
            return await HandshakeFrame.ReadAsync(frames, ct).WaitAsync(StepTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new HandshakeException("The other device stopped answering.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new HandshakeException("The connection dropped.");
        }
    }

    private static byte[] Derive(PairingKey key, byte[] peerPublicKey)
    {
        try
        {
            return key.DeriveSharedSecret(peerPublicKey);
        }
        catch (CryptographicException)
        {
            throw new HandshakeException("The other device sent an invalid key.");
        }
    }

    private static T Required<T>(T? value, string what) where T : class =>
        value ?? throw new HandshakeException($"The other device left out its {what}.");

    private static byte[] RequiredNonce(byte[]? nonce) =>
        nonce is { Length: KeyDerivation.NonceLength } ? nonce : throw new HandshakeException("The other device sent a bad nonce.");
}
