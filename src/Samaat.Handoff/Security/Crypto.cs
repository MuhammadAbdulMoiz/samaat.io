using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Samaat.Handoff.Security;

/// <summary>Key derivation for pairing and sessions (docs/plans/2026-11-04-phone-handoff.md, "Session security").</summary>
public static class KeyDerivation
{
    public const int SecretLength = 32;
    public const int NonceLength = 16;

    private static readonly byte[] PairInfo = "samaat-pair-v1"u8.ToArray();
    private static readonly byte[] SasInfo = "samaat-sas-v1"u8.ToArray();
    private static readonly byte[] PairConfirmInfo = "samaat-pair-confirm-v1"u8.ToArray();
    private static readonly byte[] SessionInfo = "samaat-session-v1"u8.ToArray();

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    /// <summary>The long-term secret both sides store after pairing.</summary>
    public static byte[] PairingSecret(byte[] sharedSecret, byte[] phoneNonce, byte[] computerNonce) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, SecretLength, Concat(phoneNonce, computerNonce), PairInfo);

    /// <summary>
    /// The 6-digit code both screens show. It covers both public keys and both nonces, so a man in the middle
    /// gets a different code on each side unless he guesses (one in a million, and the commitment stops grinding).
    /// </summary>
    public static int ComparisonCode(byte[] sharedSecret, byte[] phonePublicKey, byte[] computerPublicKey, byte[] phoneNonce, byte[] computerNonce)
    {
        var transcript = SHA256.HashData(Concat(phonePublicKey, computerPublicKey, phoneNonce, computerNonce));
        var bytes = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 4, transcript, SasInfo);
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(bytes) % 1_000_000);
    }

    public static string FormatCode(int code) => $"{code / 1000:000} {code % 1000:000}";

    /// <summary>What the phone commits to before it sees the computer's key.</summary>
    public static byte[] Commitment(byte[] phonePublicKey, byte[] phoneNonce) => SHA256.HashData(Concat(phonePublicKey, phoneNonce));

    /// <summary>Keys for the pairing confirmation exchange, from the fresh pairing secret.</summary>
    public static (byte[] PhoneToComputer, byte[] ComputerToPhone) PairConfirmKeys(byte[] pairingSecret, byte[] phoneNonce, byte[] computerNonce) =>
        Split(HKDF.DeriveKey(HashAlgorithmName.SHA256, pairingSecret, 64, Concat(phoneNonce, computerNonce), PairConfirmInfo));

    /// <summary>One key per direction for a session, from the stored secret and both sides' fresh nonces.</summary>
    public static (byte[] PhoneToComputer, byte[] ComputerToPhone) SessionKeys(byte[] secret, byte[] phoneNonce, byte[] computerNonce) =>
        Split(HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 64, Concat(phoneNonce, computerNonce), SessionInfo));

    private static (byte[], byte[]) Split(byte[] keys) => (keys[..32], keys[32..]);

    internal static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}

/// <summary>
/// AES-256-GCM for one direction of one session. The nonce is a counter, never sent: TCP keeps frames in order,
/// so a dropped, repeated or reordered frame fails authentication and the connection closes.
/// </summary>
public sealed class SessionCipher : IDisposable
{
    public const int TagLength = 16;

    private readonly AesGcm _aes;
    private ulong _counter;

    public SessionCipher(byte[] key)
    {
        if (!AesGcm.IsSupported)
        {
            throw new PlatformNotSupportedException("AES-GCM isn't available on this device, so Samaat can't secure the link.");
        }

        _aes = new AesGcm(key, TagLength);
    }

    public byte[] Seal(ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[plaintext.Length + TagLength];
        _aes.Encrypt(NextNonce(), plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length));
        return output;
    }

    /// <exception cref="CryptographicException">Wrong key, tampered, replayed or out-of-order frame.</exception>
    public byte[] Open(ReadOnlySpan<byte> sealedFrame)
    {
        if (sealedFrame.Length < TagLength)
        {
            throw new CryptographicException("Frame too short.");
        }

        var length = sealedFrame.Length - TagLength;
        var plaintext = new byte[length];
        _aes.Decrypt(NextNonce(), sealedFrame[..length], sealedFrame[length..], plaintext);
        return plaintext;
    }

    private byte[] NextNonce()
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), _counter);
        _counter = checked(_counter + 1);
        return nonce;
    }

    public void Dispose() => _aes.Dispose();
}

/// <summary>An ephemeral P-256 key for one pairing attempt.</summary>
public sealed class PairingKey : IDisposable
{
    private readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public byte[] PublicKey => _ecdh.ExportSubjectPublicKeyInfo();

    /// <exception cref="CryptographicException">The peer's key isn't a valid P-256 public key.</exception>
    public byte[] DeriveSharedSecret(byte[] peerPublicKey)
    {
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
        if (peer.KeySize != 256)
        {
            throw new CryptographicException("The other device's key isn't P-256.");
        }

        // SHA-256 of the shared point: supported on every platform .NET runs on, Android included.
        // A key on a different curve throws here.
        return _ecdh.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256);
    }

    public void Dispose() => _ecdh.Dispose();
}
