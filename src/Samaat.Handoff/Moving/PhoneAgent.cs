using Samaat.Core.Commands;
using Samaat.Handoff.Protocol;

namespace Samaat.Handoff.Moving;

/// <summary>One Bluetooth audio device paired with the phone.</summary>
public sealed record PhoneAudioDevice(string Address, string Name, bool Connected);

/// <summary>
/// The phone's own Bluetooth audio: which earbuds it's paired with, and connecting or disconnecting them.
/// Android implements it with whatever the OS allows, falling back to asking the user (docs/plans/2026-11-04-phone-handoff.md, "Android limits").
/// </summary>
public interface IPhoneAudioLink
{
    bool IsBluetoothOn { get; }

    /// <summary>Why Samaat can't use Bluetooth right now (a permission not granted), or null.</summary>
    string? Blocker { get; }

    IReadOnlyList<PhoneAudioDevice> Devices { get; }

    /// <summary>Confirmed only once the phone's audio profile is connected.</summary>
    Task<CommandResult> ConnectAsync(string address, TimeSpan timeout, CancellationToken ct);

    Task<CommandResult> DisconnectAsync(string address, TimeSpan timeout, CancellationToken ct);

    /// <summary>Raised when the device list or a connection changes. Any thread.</summary>
    event EventHandler? Changed;
}

/// <summary>Answers the computer's move requests on the phone.</summary>
public sealed class PhoneAgent(IPhoneAudioLink audio)
{
    public IPhoneAudioLink Audio { get; } = audio;

    public IReadOnlyList<PeerDevice> ListDevices() =>
        [.. Audio.Devices.Select(d => new PeerDevice(MessageCodec.NormalizeAddress(d.Address), d.Name, d.Connected, CanConnect: true, CanDisconnect: true))];

    /// <summary>Handles one request from the computer. Null for messages that aren't the agent's.</summary>
    public async Task<ReplyMessage?> HandleAsync(PeerMessage message, CancellationToken ct)
    {
        CommandResult? result = message switch
        {
            PrepareMessage prepare => Prepare(prepare.Address),
            ConnectAudioMessage connect => await ConnectAsync(connect.Address, ct),
            ReleaseAudioMessage release => await ReleaseAsync(release.Address, ct),
            _ => null,
        };
        return result is null ? null : ReplyMessage.From(message.Id, result);
    }

    public CommandResult Prepare(string address)
    {
        if (Audio.Blocker is { } blocker)
        {
            return CommandResult.Rejected(blocker);
        }

        if (!Audio.IsBluetoothOn)
        {
            return CommandResult.Rejected("Bluetooth is off on the phone.");
        }

        return Find(address) is null
            ? CommandResult.Rejected("These earbuds aren't paired with the phone. Pair them in the phone's Bluetooth settings first.")
            : CommandResult.Confirmed;
    }

    private async Task<CommandResult> ConnectAsync(string address, CancellationToken ct)
    {
        var check = Prepare(address);
        if (!check.Succeeded)
        {
            return check;
        }

        return Find(address)!.Connected ? CommandResult.Confirmed : await Audio.ConnectAsync(Find(address)!.Address, PhoneTimeout, ct);
    }

    private async Task<CommandResult> ReleaseAsync(string address, CancellationToken ct)
    {
        var device = Find(address);
        if (device is null || !device.Connected)
        {
            return CommandResult.Confirmed;
        }

        return await Audio.DisconnectAsync(device.Address, PhoneTimeout, ct);
    }

    /// <summary>A little under the computer's wait, so the phone's own reason arrives before the computer gives up.</summary>
    internal static TimeSpan PhoneTimeout => HandoffTimeouts.Default.PhoneLink - TimeSpan.FromSeconds(3);

    private PhoneAudioDevice? Find(string address)
    {
        var wanted = MessageCodec.NormalizeAddress(address);
        return Audio.Devices.FirstOrDefault(d => MessageCodec.NormalizeAddress(d.Address) == wanted);
    }
}
