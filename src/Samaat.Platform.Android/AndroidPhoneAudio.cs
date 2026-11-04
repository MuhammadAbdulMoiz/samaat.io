using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Samaat.Core.Commands;
using Samaat.Handoff.Moving;

namespace Samaat.Platform.Android;

/// <summary>Asks the user to finish a connect or disconnect Android won't let an app do itself.</summary>
public interface IAudioUserPrompt
{
    void AskToConnect(string deviceName);

    void AskToDisconnect(string deviceName);

    /// <summary>Clears any prompt once the change happened.</summary>
    void Dismiss();
}

/// <summary>
/// The phone's Bluetooth audio, through the A2DP and headset profile proxies. Android has no public "connect this
/// headset" call for apps, so connect and disconnect try the hidden profile methods first, then ask the user
/// (docs/plans/2026-11-04-phone-handoff.md, "Android limits"). Either way a change is confirmed only once Android
/// reports the profile state. Unverified on hardware.
/// </summary>
public sealed class AndroidPhoneAudio : Java.Lang.Object, IPhoneAudioLink, IBluetoothProfileServiceListener
{
    /// <summary>How long the hidden call gets before the user is asked.</summary>
    private static readonly TimeSpan SilentAttempt = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(300);

    private readonly Context _context;
    private readonly BluetoothAdapter? _adapter;
    private readonly IAudioUserPrompt _prompt;
    private readonly StateReceiver _receiver;
    private IBluetoothProfile? _a2dp;
    private IBluetoothProfile? _headset;

    public AndroidPhoneAudio(Context context, IAudioUserPrompt prompt)
    {
        _context = context.ApplicationContext ?? context;
        _prompt = prompt;
        _adapter = (_context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
        _receiver = new StateReceiver(this);

        var filter = new IntentFilter();
        filter.AddAction(BluetoothA2dp.ActionConnectionStateChanged);
        filter.AddAction(BluetoothHeadset.ActionConnectionStateChanged);
        filter.AddAction(BluetoothAdapter.ActionStateChanged);
        filter.AddAction(BluetoothDevice.ActionBondStateChanged);
        filter.AddAction(BluetoothDevice.ActionAclConnected);
        filter.AddAction(BluetoothDevice.ActionAclDisconnected);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _context.RegisterReceiver(_receiver, filter, ReceiverFlags.Exported);
        }
        else
        {
            _context.RegisterReceiver(_receiver, filter);
        }

        OpenProxies();
    }

    /// <summary>What the last connect or disconnect tried, for the diagnostics report.</summary>
    public string? LastAttempt { get; private set; }

    public bool IsBluetoothOn => _adapter?.IsEnabled == true;

    public string? Blocker =>
        _adapter is null ? "This phone has no Bluetooth."
        : OperatingSystem.IsAndroidVersionAtLeast(31) && _context.CheckSelfPermission(global::Android.Manifest.Permission.BluetoothConnect) != Permission.Granted
            ? "Allow Samaat to find and connect to nearby devices."
            : null;

    public IReadOnlyList<PhoneAudioDevice> Devices
    {
        get
        {
            if (Blocker is not null || _adapter?.BondedDevices is not { } bonded)
            {
                return [];
            }

            return [.. bonded
                .Where(IsAudio)
                .Select(d => new PhoneAudioDevice(d.Address ?? "", DisplayName(d), IsConnected(d)))
                .Where(d => d.Address.Length > 0)];
        }
    }

    public event EventHandler? Changed;

    internal static bool IsAudio(BluetoothDevice device) =>
        device.BluetoothClass?.MajorDeviceClass == MajorDeviceClass.AudioVideo;

    internal static string DisplayName(BluetoothDevice device) =>
        (OperatingSystem.IsAndroidVersionAtLeast(30) ? device.Alias : null) ?? device.Name ?? device.Address ?? "Bluetooth device";

    public bool IsConnected(string address) => Find(address) is { } device && IsConnected(device);

    private bool IsConnected(BluetoothDevice device) =>
        _a2dp?.GetConnectionState(device) == ProfileState.Connected || _headset?.GetConnectionState(device) == ProfileState.Connected;

    public Task<CommandResult> ConnectAsync(string address, TimeSpan timeout, CancellationToken ct) => ChangeAsync(address, true, timeout, ct);

    public Task<CommandResult> DisconnectAsync(string address, TimeSpan timeout, CancellationToken ct) => ChangeAsync(address, false, timeout, ct);

    private async Task<CommandResult> ChangeAsync(string address, bool connect, TimeSpan timeout, CancellationToken ct)
    {
        if (Blocker is { } blocker)
        {
            return CommandResult.Rejected(blocker);
        }

        if (Find(address) is not { } device)
        {
            return CommandResult.Rejected("These earbuds aren't paired with the phone.");
        }

        if (IsConnected(device) == connect)
        {
            return CommandResult.Confirmed;
        }

        var name = DisplayName(device);
        var started = DateTime.UtcNow;
        var attempts = new List<string>();

        // 1. The hidden profile methods. Works on older Android and some phones; refused (or missing) elsewhere.
        var method = connect ? "connect" : "disconnect";
        foreach (var (label, proxy) in new[] { ("A2DP", _a2dp), ("headset", _headset) })
        {
            attempts.Add($"{method} {label}: {InvokeHidden(proxy, method, device)}");
        }

        if (await WaitForAsync(device, connect, SilentAttempt, ct))
        {
            LastAttempt = $"{string.Join("; ", attempts)}; done in {(DateTime.UtcNow - started).TotalSeconds:0} s";
            return CommandResult.Confirmed;
        }

        // 2. Ask the user: Android's output switcher or Bluetooth settings, one tap.
        if (connect)
        {
            _prompt.AskToConnect(name);
        }
        else
        {
            _prompt.AskToDisconnect(name);
        }

        attempts.Add("asked the user");
        var remaining = timeout - (DateTime.UtcNow - started);
        var done = remaining > TimeSpan.Zero && await WaitForAsync(device, connect, remaining, ct);
        _prompt.Dismiss();
        LastAttempt = $"{string.Join("; ", attempts)}; {(done ? "done" : "gave up")} after {(DateTime.UtcNow - started).TotalSeconds:0} s";
        if (done)
        {
            return CommandResult.Confirmed;
        }

        return CommandResult.Rejected(connect
            ? $"{name} didn't connect on the phone. Choose them in the phone's Bluetooth or audio output settings."
            : $"{name} didn't disconnect from the phone. Disconnect them in the phone's Bluetooth settings.");
    }

    /// <summary>Calls BluetoothA2dp/BluetoothHeadset.connect or disconnect through Java reflection. Returns what happened, in words.</summary>
    private static string InvokeHidden(IBluetoothProfile? proxy, string name, BluetoothDevice device)
    {
        if (proxy is not Java.Lang.Object target)
        {
            return "no proxy";
        }

        try
        {
            var method = target.Class.GetMethod(name, Java.Lang.Class.FromType(typeof(BluetoothDevice)));
            var result = method.Invoke(target, device);
            return result is Java.Lang.Boolean ok && ok.BooleanValue() ? "accepted" : "returned false";
        }
        catch (Java.Lang.NoSuchMethodException)
        {
            return "not available";
        }
        catch (Java.Lang.Reflect.InvocationTargetException ex)
        {
            return $"refused ({ex.Cause?.Class.SimpleName ?? "error"})";
        }
        catch (Java.Lang.Exception ex)
        {
            return $"refused ({ex.Class.SimpleName})";
        }
    }

    private async Task<bool> WaitForAsync(BluetoothDevice device, bool connected, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsConnected(device) == connected)
            {
                return true;
            }

            await Task.Delay(Poll, ct);
        }

        return IsConnected(device) == connected;
    }

    private BluetoothDevice? Find(string address)
    {
        if (_adapter is null || !BluetoothAdapter.CheckBluetoothAddress(address.ToUpperInvariant()))
        {
            return null;
        }

        return _adapter.BondedDevices?.FirstOrDefault(d => string.Equals(d.Address, address, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenProxies()
    {
        if (_adapter is null || Blocker is not null)
        {
            return;
        }

        if (_a2dp is null)
        {
            _adapter.GetProfileProxy(_context, this, ProfileType.A2dp);
        }

        if (_headset is null)
        {
            _adapter.GetProfileProxy(_context, this, ProfileType.Headset);
        }
    }

    /// <summary>Call after the Bluetooth permission is granted.</summary>
    public void Refresh()
    {
        OpenProxies();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OnServiceConnected(ProfileType profile, IBluetoothProfile? proxy)
    {
        if (profile == ProfileType.A2dp)
        {
            _a2dp = proxy;
        }
        else if (profile == ProfileType.Headset)
        {
            _headset = proxy;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OnServiceDisconnected(ProfileType profile)
    {
        if (profile == ProfileType.A2dp)
        {
            _a2dp = null;
        }
        else if (profile == ProfileType.Headset)
        {
            _headset = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged()
    {
        OpenProxies();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _context.UnregisterReceiver(_receiver);
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                // Never registered.
            }

            if (_a2dp is not null)
            {
                _adapter?.CloseProfileProxy(ProfileType.A2dp, _a2dp);
            }

            if (_headset is not null)
            {
                _adapter?.CloseProfileProxy(ProfileType.Headset, _headset);
            }
        }

        base.Dispose(disposing);
    }

    private sealed class StateReceiver(AndroidPhoneAudio owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => owner.OnStateChanged();
    }
}
