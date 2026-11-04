using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
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
    public static HandoffTimeouts Default { get; } = new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));
}

/// <summary>
/// Moves earbuds between this computer and a phone, on any earbuds: check, release on one side, connect on the other,
/// and put them back if the connect fails (docs/plans/2026-11-04-phone-handoff.md, "Flows").
/// </summary>
public sealed class HandoffCoordinator(TimeProvider? time = null, HandoffTimeouts? timeouts = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly HandoffTimeouts _timeouts = timeouts ?? HandoffTimeouts.Default;

    public async Task<HandoffResult> RunAsync(
        IEarbudDevice device, IPhoneEndpoint phone, HandoffDirection direction, IProgress<HandoffProgress>? progress = null, CancellationToken ct = default)
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

        progress?.Report(new HandoffProgress(HandoffStage.Checking, $"Checking {phone.Name}…"));
        var ready = await Step(c => phone.PrepareAsync(address, direction, _timeouts.Prepare, c), _timeouts.Prepare, ct);
        if (!ready.Succeeded)
        {
            return NotStarted($"{phone.Name} can't take part: {Reason(ready)}");
        }

        return direction == HandoffDirection.ToPhone
            ? await ToPhoneAsync(buds, address, pcLink, phone, hereConnected, progress, ct)
            : await ToComputerAsync(buds, address, pcLink, phone, thereConnected, progress, ct);
    }

    private async Task<HandoffResult> ToPhoneAsync(
        string buds, string address, IPcLinkFeature pcLink, IPhoneEndpoint phone, bool hereConnected, IProgress<HandoffProgress>? progress, CancellationToken ct)
    {
        if (hereConnected)
        {
            progress?.Report(new HandoffProgress(HandoffStage.Releasing, "Disconnecting from this computer…"));
            var released = await Step(pcLink.DisconnectAsync, _timeouts.ComputerLink, ct);
            if (!released.Succeeded)
            {
                return NotStarted($"Couldn't disconnect {buds} from this computer: {Reason(released)}");
            }
        }

        progress?.Report(new HandoffProgress(HandoffStage.Connecting, $"Connecting on {phone.Name}…"));
        var connected = await Step(c => phone.ConnectAudioAsync(address, _timeouts.PhoneLink, c), _timeouts.PhoneLink, ct);
        if (connected.Succeeded)
        {
            return Done(progress, $"{buds} moved to {phone.Name}.");
        }

        if (!hereConnected)
        {
            return Finish(progress, HandoffOutcome.RolledBack, $"{phone.Name} couldn't connect {buds}: {Reason(connected)}");
        }

        progress?.Report(new HandoffProgress(HandoffStage.RollingBack, "Reconnecting to this computer…"));
        var back = await Step(pcLink.ConnectAsync, _timeouts.ComputerLink, CancellationToken.None);
        return back.Succeeded
            ? Finish(progress, HandoffOutcome.RolledBack, $"{phone.Name} couldn't connect {buds} ({Reason(connected)}). They're back on this computer.")
            : Finish(progress, HandoffOutcome.Stranded,
                $"{phone.Name} couldn't connect {buds} ({Reason(connected)}), and reconnecting here failed too. Connect them from Bluetooth settings on either device.");
    }

    private async Task<HandoffResult> ToComputerAsync(
        string buds, string address, IPcLinkFeature pcLink, IPhoneEndpoint phone, bool thereConnected, IProgress<HandoffProgress>? progress, CancellationToken ct)
    {
        if (thereConnected)
        {
            progress?.Report(new HandoffProgress(HandoffStage.Releasing, $"Disconnecting from {phone.Name}…"));
            var released = await Step(c => phone.ReleaseAudioAsync(address, _timeouts.PhoneLink, c), _timeouts.PhoneLink, ct);
            if (!released.Succeeded)
            {
                return NotStarted($"{phone.Name} couldn't disconnect {buds}: {Reason(released)}");
            }
        }

        progress?.Report(new HandoffProgress(HandoffStage.Connecting, "Connecting to this computer…"));
        var connected = await Step(pcLink.ConnectAsync, _timeouts.ComputerLink, ct);
        if (connected.Succeeded)
        {
            return Done(progress, $"{buds} moved to this computer.");
        }

        if (!thereConnected)
        {
            return Finish(progress, HandoffOutcome.RolledBack, $"Couldn't connect {buds} to this computer: {Reason(connected)}");
        }

        progress?.Report(new HandoffProgress(HandoffStage.RollingBack, $"Reconnecting on {phone.Name}…"));
        var back = await Step(c => phone.ConnectAudioAsync(address, _timeouts.PhoneLink, c), _timeouts.PhoneLink, CancellationToken.None);
        return back.Succeeded
            ? Finish(progress, HandoffOutcome.RolledBack, $"Couldn't connect {buds} to this computer ({Reason(connected)}). They're back on {phone.Name}.")
            : Finish(progress, HandoffOutcome.Stranded,
                $"Couldn't connect {buds} to this computer ({Reason(connected)}), and {phone.Name} couldn't take them back. Connect them from Bluetooth settings on either device.");
    }

    /// <summary>Every step gets a little longer than its own timeout so the step's own message wins over a generic one.</summary>
    private Task<CommandResult> Step(Func<CancellationToken, Task<CommandResult>> step, TimeSpan timeout, CancellationToken ct) =>
        CommandRunner.RunAsync(step, timeout + TimeSpan.FromSeconds(2), _time, ct);

    private static string Reason(CommandResult result) => result.UserMessage ?? "unknown error";

    private static HandoffResult NotStarted(string message) => new(HandoffOutcome.NotStarted, message);

    private static HandoffResult Done(IProgress<HandoffProgress>? progress, string message) =>
        Finish(progress, HandoffOutcome.Moved, message);

    private static HandoffResult Finish(IProgress<HandoffProgress>? progress, HandoffOutcome outcome, string message)
    {
        progress?.Report(new HandoffProgress(HandoffStage.Done, message));
        return new HandoffResult(outcome, message);
    }
}
