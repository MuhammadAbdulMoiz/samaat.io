using System.Collections.Concurrent;
using Samaat.Core.Commands;
using Samaat.Core.Diagnostics;
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _connecting = new(StringComparer.Ordinal);

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

        return Find(address)!.Connected ? CommandResult.Confirmed : await ConnectUnlessBluetoothGoesOffAsync(Find(address)!.Address, ct);
    }

    /// <summary>Stops waiting as soon as the phone's Bluetooth turns off, so the computer hears why at once and takes the earbuds back.</summary>
    private async Task<CommandResult> ConnectUnlessBluetoothGoesOffAsync(string address, CancellationToken ct)
    {
        using var bluetoothOff = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var key = MessageCodec.NormalizeAddress(address);
        _connecting[key] = bluetoothOff; // a ReleaseAudio for these earbuds cancels it

        void OnChanged(object? sender, EventArgs e)
        {
            if (!Audio.IsBluetoothOn)
            {
                bluetoothOff.Cancel();
            }
        }

        Task<CommandResult>? connect = null;
        Audio.Changed += OnChanged;
        try
        {
            OnChanged(null, EventArgs.Empty); // in case it went off before the subscription
            bluetoothOff.Token.ThrowIfCancellationRequested(); // don't start a connect once Bluetooth is known to be off
            connect = Audio.ConnectAsync(address, PhoneTimeout, bluetoothOff.Token);
            // WaitAsync: don't depend on the platform honouring the token to answer promptly.
            var result = await connect.WaitAsync(bluetoothOff.Token);
            return Audio.IsBluetoothOn || ct.IsCancellationRequested ? result : BluetoothTurnedOff;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!Audio.IsBluetoothOn)
            {
                return BluetoothTurnedOff;
            }

            // The computer took the earbuds back. If the platform's connect still lands later, undo it so they aren't split.
            _ = connect is null ? Task.CompletedTask : SamaatLog.Guard("Phone agent", "Undoing a late connect", async () =>
            {
                if ((await connect).Succeeded)
                {
                    await Audio.DisconnectAsync(address, PhoneTimeout, CancellationToken.None);
                }
            });
            return CommandResult.Rejected("The computer took the earbuds back.");
        }
        finally
        {
            _connecting.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, bluetoothOff));
            Audio.Changed -= OnChanged;
            // An abandoned connect may still fault later; observe it so it isn't reported as unobserved.
            _ = connect?.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private static CommandResult BluetoothTurnedOff => CommandResult.Rejected("Bluetooth turned off on the phone.");

    private async Task<CommandResult> ReleaseAsync(string address, CancellationToken ct)
    {
        // The computer gave up on a connect and took the earbuds back: stop that connect first.
        if (_connecting.TryGetValue(MessageCodec.NormalizeAddress(address), out var connecting))
        {
            try
            {
                await connecting.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // That connect just finished.
            }
        }

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
