using System.Security.Cryptography;
using System.Threading.Channels;
using Samaat.Transport;

namespace Samaat.Protocols.Jieli;

/// <summary>
/// Jieli's raw (unframed) auth handshake over AE01/AE02. These are the only bytes the adapter ever
/// writes to the earbuds: <c>00 | random</c>, <c>02 "pass"</c>, <c>01 | cipher(challenge)</c>.
/// </summary>
public static class RcspAuth
{
    private static readonly byte[] Pass = [0x02, 0x70, 0x61, 0x73, 0x73];

    /// <summary>
    /// Runs the handshake. <paramref name="replies"/> receives every raw AE02 notification. Returns false
    /// on a wrong reply, a failed write, or no reply within <paramref name="stepTimeout"/>. Only
    /// cancellation of <paramref name="cancellationToken"/> throws.
    /// </summary>
    public static async Task<bool> RunAsync(
        IGattCharacteristic write,
        ChannelReader<byte[]> replies,
        TimeSpan stepTimeout,
        CancellationToken cancellationToken)
    {
        var random = RandomNumberGenerator.GetBytes(16);
        if (await ExchangeAsync(write, replies, [0x00, .. random], stepTimeout, cancellationToken) is not { Length: 17 } first
            || first[0] != 0x01
            || !first.AsSpan(1).SequenceEqual(JlAuthCipher.Encrypt(random)))
        {
            return false;
        }

        if (await ExchangeAsync(write, replies, Pass, stepTimeout, cancellationToken) is not { Length: 17 } challenge
            || challenge[0] != 0x00)
        {
            return false;
        }

        var last = await ExchangeAsync(write, replies, [0x01, .. JlAuthCipher.Encrypt(challenge[1..])], stepTimeout, cancellationToken);
        return last is not null && last.AsSpan().SequenceEqual(Pass);
    }

    private static async Task<byte[]?> ExchangeAsync(
        IGattCharacteristic write,
        ChannelReader<byte[]> replies,
        byte[] data,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        await write.WriteAsync(data, withoutResponse: true, cancellationToken)
            ? await NextAsync(replies, timeout, cancellationToken)
            : null;

    private static async Task<byte[]?> NextAsync(ChannelReader<byte[]> replies, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var step = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        step.CancelAfter(timeout);
        try
        {
            return await replies.ReadAsync(step.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
