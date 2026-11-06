using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Diagnostics;
using Samaat.Handoff.Protocol;

namespace Samaat.Handoff.Moving;

/// <summary>The phone, as the computer's coordinator sees it. Over the network this is a <see cref="PhoneLinkHost"/> session.</summary>
public interface IPhoneEndpoint
{
    string Name { get; }

    /// <summary>What the phone last said about these earbuds; null when it hasn't listed them.</summary>
    PeerDevice? FindDevice(string address);

    Task<CommandResult> PrepareAsync(string address, HandoffDirection direction, TimeSpan timeout, CancellationToken ct);

    Task<CommandResult> ConnectAudioAsync(string address, TimeSpan timeout, CancellationToken ct);

    Task<CommandResult> ReleaseAudioAsync(string address, TimeSpan timeout, CancellationToken ct);
}

public sealed record HandoffResult(HandoffOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == HandoffOutcome.Moved;
}

public sealed record HandoffProgress(HandoffStage Stage, string Text);

/// <summary>How long each step may take. The phone's steps are long because it may need one tap from the user.</summary>
public sealed record HandoffTimeouts(TimeSpan Prepare, TimeSpan ComputerLink, TimeSpan PhoneLink)
{
    /// <summary>
    /// The phone's steps get 20 s: enough for the silent connect and one tap, short enough that a hung phone (seen on hardware,
    /// 2026-11-06: 56 s with Bluetooth off) doesn't leave the earbuds disconnected everywhere.
    /// </summary>
    public static readonly TimeSpan PhoneStepLimit = TimeSpan.FromSeconds(20);

    public static HandoffTimeouts Default { get; } = new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), PhoneStepLimit);
}

/// <summary>
/// Moves earbuds between this computer and a phone, on any earbuds: check, release on one side, connect on the other,
/// and put them back if the connect fails (docs/plans/2026-11-04-phone-handoff.md, "Flows").
/// </summary>
public sealed class HandoffCoordinator(TimeProvider? time = null, HandoffTimeouts? timeouts = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly HandoffTimeouts _timeouts = timeouts ?? HandoffTimeouts.Default;

    private const string Log = "Handoff";

    /// <summary>
    /// Runs one move. Cancelling <paramref name="ct"/> stops it only before anything changed; once the earbuds were released,
    /// a cancelled move is treated as a failed one and put back.
    /// </summary>
    public async Task<HandoffResult> RunAsync(
        IEarbudDevice device, IPhoneEndpoint phone, HandoffDirection direction, IProgress<HandoffProgress>? progress = null, CancellationToken ct = default)
    {
        var buds = device.Identity.Name;
        SamaatLog.Info(Log, $"{buds}: Starting a move {direction} with {phone.Name}");
        try
        {
            var result = await RunCoreAsync(device, phone, direction, new LoggedProgress(buds, progress), ct);
            SamaatLog.Info(Log, $"{buds}: {result.Outcome}: {result.Message}");
            return result;
        }
        catch (OperationCanceledException)
        {
            SamaatLog.Info(Log, $"{buds}: move cancelled before anything changed");
            throw;
        }
    }

    private async Task<HandoffResult> RunCoreAsync(
        IEarbudDevice device, IPhoneEndpoint phone, HandoffDirection direction, IProgress<HandoffProgress> progress, CancellationToken ct)
    {
        var buds = device.Identity.Name;
        if (device.Identity.Details?.Address is not { Length: > 0 } rawAddress)
        {
            return NotStarted($"Samaat doesn't know {buds}' Bluetooth address, so it can't find them on {phone.Name}.");
        }

        if (device.GetFeature<IPcLinkFeature>() is not { } pcLink)
        {
            return NotStarted($"This computer can't connect or disconnect {buds} from Samaat. Use Bluetooth settings instead.");
        }

        var address = MessageCodec.NormalizeAddress(rawAddress);
        var hereConnected = device.Connection == ConnectionState.Connected;
        var thereConnected = phone.FindDevice(address)?.Connected == true;

        if (direction == HandoffDirection.ToPhone && thereConnected && !hereConnected)
        {
            return Done(progress, $"{buds} are already on {phone.Name}.");
        }

        if (direction == HandoffDirection.ToComputer && hereConnected)
        {
            return Done(progress, $"{buds} are already on this computer.");
        }

        progress.Report(new HandoffProgress(HandoffStage.Checking, $"Checking {phone.Name}…"));
        var ready = await Step(buds, "prepare on the phone", c => phone.PrepareAsync(address, direction, _timeouts.Prepare, c), _timeouts.Prepare, ct);
        if (!ready.Succeeded)
        {
            return NotStarted($"{phone.Name} can't take part: {Reason(ready)}");
        }

        ct.ThrowIfCancellationRequested();
        return direction == HandoffDirection.ToPhone
            ? await ToPhoneAsync(buds, address, pcLink, phone, hereConnected, progress, ct)
            : await ToComputerAsync(buds, address, pcLink, phone, thereConnected, progress, ct);
    }

    private async Task<HandoffResult> ToPhoneAsync(
        string buds, string address, IPcLinkFeature pcLink, IPhoneEndpoint phone, bool hereConnected, IProgress<HandoffProgress> progress, CancellationToken ct)
    {
        if (hereConnected)
        {
            progress.Report(new HandoffProgress(HandoffStage.Releasing, "Disconnecting from this computer…"));
            // Not cancellable: a disconnect that lands after a cancel would leave nobody to reconnect the earbuds.
            var released = await Step(buds, "disconnect here", pcLink.DisconnectAsync, _timeouts.ComputerLink, CancellationToken.None);
            if (!released.Succeeded)
            {
                return NotStarted($"Couldn't disconnect {buds} from this computer: {Reason(released)}");
            }
        }

        progress.Report(new HandoffProgress(HandoffStage.Connecting, $"Connecting on {phone.Name}…"));
        var connected = await StepAfterRelease(buds, $"connect on {phone.Name}", c => phone.ConnectAudioAsync(address, _timeouts.PhoneLink, c), _timeouts.PhoneLink, ct);
        if (connected.Succeeded)
        {
            return Done(progress, $"{buds} moved to {phone.Name}.");
        }

        if (!hereConnected)
        {
            return Finish(progress, HandoffOutcome.RolledBack, $"{phone.Name} couldn't connect {buds}: {Reason(connected)}");
        }

        SamaatLog.Info(Log, $"{buds}: Rolling back, reconnecting here");
        progress.Report(new HandoffProgress(HandoffStage.RollingBack, "Reconnecting to this computer…"));
        // A phone that went silent may still be connecting: tell it to stop, so a late connect doesn't split the earbuds.
        var stopThere = connected.Status is CommandStatus.TimedOut or CommandStatus.Failed
            ? Step(buds, $"stop connecting on {phone.Name}", c => phone.ReleaseAudioAsync(address, _timeouts.Prepare, c), _timeouts.Prepare,
                CancellationToken.None)
            : Task.FromResult(CommandResult.Confirmed);
        var back = await Step(buds, "reconnect here", pcLink.ConnectAsync, _timeouts.ComputerLink, CancellationToken.None);
        await stopThere;
        return back.Succeeded
            ? Finish(progress, HandoffOutcome.RolledBack, $"Couldn't move {buds} to {phone.Name}; kept them on this computer. ({Reason(connected)})")
            : Finish(progress, HandoffOutcome.Stranded,
                $"{phone.Name} couldn't connect {buds} ({Reason(connected)}), and reconnecting here failed too. Connect them from Bluetooth settings on either device.");
    }

    private async Task<HandoffResult> ToComputerAsync(
        string buds, string address, IPcLinkFeature pcLink, IPhoneEndpoint phone, bool thereConnected, IProgress<HandoffProgress> progress, CancellationToken ct)
    {
        if (thereConnected)
        {
            progress.Report(new HandoffProgress(HandoffStage.Releasing, $"Disconnecting from {phone.Name}…"));
            // Not cancellable, for the same reason as the computer's disconnect.
            var released = await Step(buds, $"disconnect on {phone.Name}", c => phone.ReleaseAudioAsync(address, _timeouts.PhoneLink, c), _timeouts.PhoneLink,
                CancellationToken.None);
            if (!released.Succeeded)
            {
                return NotStarted($"{phone.Name} couldn't disconnect {buds}: {Reason(released)}");
            }
        }

        progress.Report(new HandoffProgress(HandoffStage.Connecting, "Connecting to this computer…"));
        var connected = await StepAfterRelease(buds, "connect here", pcLink.ConnectAsync, _timeouts.ComputerLink, ct);
        if (connected.Succeeded)
        {
            return Done(progress, $"{buds} moved to this computer.");
        }

        if (!thereConnected)
        {
            return Finish(progress, HandoffOutcome.RolledBack, $"Couldn't connect {buds} to this computer: {Reason(connected)}");
        }

        SamaatLog.Info(Log, $"{buds}: Rolling back, reconnecting on {phone.Name}");
        progress.Report(new HandoffProgress(HandoffStage.RollingBack, $"Reconnecting on {phone.Name}…"));
        var back = await Step(buds, $"reconnect on {phone.Name}", c => phone.ConnectAudioAsync(address, _timeouts.PhoneLink, c), _timeouts.PhoneLink,
            CancellationToken.None);
        return back.Succeeded
            ? Finish(progress, HandoffOutcome.RolledBack, $"Couldn't connect {buds} to this computer ({Reason(connected)}). They're back on {phone.Name}.")
            : Finish(progress, HandoffOutcome.Stranded,
                $"Couldn't connect {buds} to this computer ({Reason(connected)}), and {phone.Name} couldn't take them back. Connect them from Bluetooth settings on either device.");
    }

    /// <summary>Every step gets a little longer than its own timeout so the step's own message wins over a generic one.</summary>
    private async Task<CommandResult> Step(string buds, string what, Func<CancellationToken, Task<CommandResult>> step, TimeSpan timeout, CancellationToken ct)
    {
        var result = await CommandRunner.RunAsync(step, timeout + TimeSpan.FromSeconds(2), _time, ct);
        SamaatLog.Info(Log, result.Succeeded ? $"{buds}: {what}: {result.Status}" : $"{buds}: {what}: {result.Status} ({Reason(result)})");
        return result;
    }

    /// <summary>A step after the earbuds were released: cancelling it is a failure to roll back from, not a reason to stop.</summary>
    private async Task<CommandResult> StepAfterRelease(
        string buds, string what, Func<CancellationToken, Task<CommandResult>> step, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            // Cancelled during the release: don't start the other side, just put them back.
            ct.ThrowIfCancellationRequested();
            return await Step(buds, what, step, timeout, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SamaatLog.Info(Log, $"{buds}: {what}: cancelled");
            return new CommandResult(CommandStatus.Failed, "The move was cancelled.");
        }
    }

    private static string Reason(CommandResult result) => result.UserMessage ?? "unknown error";

    private static HandoffResult NotStarted(string message) => new(HandoffOutcome.NotStarted, message);

    private static HandoffResult Done(IProgress<HandoffProgress> progress, string message) =>
        Finish(progress, HandoffOutcome.Moved, message);

    private static HandoffResult Finish(IProgress<HandoffProgress> progress, HandoffOutcome outcome, string message)
    {
        progress.Report(new HandoffProgress(HandoffStage.Done, message));
        return new HandoffResult(outcome, message);
    }

    /// <summary>Logs each stage of a move (the result is logged once, by <see cref="RunAsync"/>) and passes it on.</summary>
    private sealed class LoggedProgress(string buds, IProgress<HandoffProgress>? inner) : IProgress<HandoffProgress>
    {
        public void Report(HandoffProgress value)
        {
            if (value.Stage is not (HandoffStage.Done or HandoffStage.RollingBack))
            {
                SamaatLog.Info(Log, $"{buds}: {value.Stage}: {value.Text}");
            }

            inner?.Report(value);
        }
    }
}
