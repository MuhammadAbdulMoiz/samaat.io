// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Core.Drivers;
using Samaat.Transport;

namespace Samaat.Protocols.Sony;

/// <summary>
/// Sony headphones and earbuds over RFCOMM (protocol v1): battery, and the current noise mode where the model has
/// one. With <c>allowWrites</c> false (the default) only init, queries and ACKs are ever written, and noise is
/// read-only; with it true the noise setter sends SonyHeadphonesClient's set command.
/// </summary>
public sealed class SonyDriver(bool allowWrites = false, TimeSpan? commandTimeout = null) : IEarbudDriver
{
    public const string DriverId = "sony-mdr";

    internal const string ReadOnlyMessage = "Changing this on Sony headphones isn't supported yet.";

    private static readonly Guid Service = Guid.Parse("96CC203E-5068-46AD-B32D-E316F5E069BA");
    private static readonly TimeSpan InitWait = TimeSpan.FromSeconds(3);

    public string Id => DriverId;

    public string DisplayName => "Sony headphones";

    /// <summary>Returns the features once the headset answers init (as v1) and the queries; otherwise nothing. Never throws except on cancellation.</summary>
    public async Task<IReadOnlyList<IFeature>> OpenAsync(DriverContext context, CancellationToken cancellationToken = default)
    {
        if (!SonyModels.TryGet(context.Entry.Id, out var model))
        {
            return [];
        }

        IRfcommChannel? channel = null;
        SonySession? session = null;
        try
        {
            channel = await context.Transports.OpenRfcommAsync(Service, cancellationToken);
            if (channel is null)
            {
                return [];
            }

            session = new SonySession(channel, allowWrites, context.Time, commandTimeout ?? TimeSpan.FromSeconds(2));
            session.Attach();
            var features = await QueryFeaturesAsync(session, model, cancellationToken);
            if (features.Count == 0)
            {
                await DisposeQuietlyAsync(session, channel);
            }

            return features;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(session, channel);
            throw;
        }
        catch (Exception ex)
        {
            // A closed channel and Bluetooth errors all just mean no driver features.
            SamaatLog.Info("Sony", "The Sony link didn't open", ex);
            await DisposeQuietlyAsync(session, channel);
            return [];
        }
    }

    private static async Task<IReadOnlyList<IFeature>> QueryFeaturesAsync(SonySession session, SonyModel model, CancellationToken cancellationToken)
    {
        var init = await AskAsync(session, SonyCommands.Init, InitReplyOf, InitWait, cancellationToken)
            ?? await AskAsync(session, SonyCommands.Init, InitReplyOf, InitWait, cancellationToken);
        if (init is null)
        {
            SamaatLog.Info("Sony", "The headset didn't answer init");
            return [];
        }

        if (init.Length != SonyCommands.InitReplyLengthV1)
        {
            SamaatLog.Info("Sony", "Not protocol v1");
            return [];
        }

        var batteries = new List<SonyBatteryReport>();
        byte[] types = model.Battery == SonyBatteryKind.BudsAndCase ? [1, 2] : [0];
        foreach (var type in types)
        {
            var report = await AskAsync(session, SonyCommands.BatteryQuery(type), p => p is [SonyCommands.BatteryReply, ..] && SonyBatteryReport.TryParse(p) is { } r && r.Type == type ? r : null, session.CommandTimeout, cancellationToken);
            if (report is not null)
            {
                batteries.Add(report);
            }
            else if (type != 2)
            {
                return [];
            }
        }

        var features = new List<IFeature> { new SonyBatteryFeature(session, batteries) };
        if (model.Noise
            && await AskAsync(session, SonyCommands.NoiseQuery, p => p is [SonyCommands.NoiseReply, ..] ? SonyNoiseReport.TryParse(p) : null, session.CommandTimeout, cancellationToken) is { } noise)
        {
            features.Add(new SonyNoiseControlFeature(session, noise));
        }

        return features;
    }

    private static byte[]? InitReplyOf(byte[] payload) => payload is [SonyCommands.InitReply, ..] ? payload : null;

    /// <summary>Sends one command and waits for the first inbound command <paramref name="parse"/> accepts. Null on no match in time, or when the channel closes.</summary>
    private static async Task<T?> AskAsync<T>(SonySession session, byte[] command, Func<byte[], T?> parse, TimeSpan wait, CancellationToken cancellationToken)
        where T : class
    {
        var reply = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(byte[] payload)
        {
            if (parse(payload) is { } value)
            {
                reply.TrySetResult(value);
            }
        }

        session.Message += OnMessage;
        try
        {
            // No ACK means no seq handoff: a reply that arrived anyway isn't trusted.
            if (!(await session.SendCommandAsync(command, cancellationToken)).Succeeded)
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var finished = await Task.WhenAny(reply.Task, session.Closed, Task.Delay(wait, session.Time, timeoutCts.Token));
            await timeoutCts.CancelAsync();
            if (finished == reply.Task)
            {
                return await reply.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        finally
        {
            session.Message -= OnMessage;
        }
    }

    private static async Task DisposeQuietlyAsync(SonySession? session, IRfcommChannel? channel)
    {
        try
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // Releasing a dead channel can fail; discovery must not.
        }
    }
}
