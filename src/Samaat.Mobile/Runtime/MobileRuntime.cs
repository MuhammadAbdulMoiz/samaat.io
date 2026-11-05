using Samaat.Core.Devices;
using Samaat.Core.Profiles;
using Samaat.Core.Settings;
using Samaat.Handoff;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Moving;
using Samaat.Handoff.Storage;

namespace Samaat.Mobile.Runtime;

/// <summary>What only the phone's OS can do. Android implements it; the simulator and tests use <see cref="NoMobilePlatform"/>.</summary>
public interface IMobilePlatform
{
    /// <summary>"android", "simulator", …: shown on the computer.</summary>
    string Name { get; }

    /// <summary>Asks for the Bluetooth and notification permissions. True when Samaat can use Bluetooth.</summary>
    Task<bool> RequestPermissionsAsync();

    void OpenBluetoothSettings();

    /// <summary>Keeps the computer link alive while the app is in the background (Android: a foreground service).</summary>
    void KeepLinkAlive(bool enabled);
}

public sealed class NoMobilePlatform : IMobilePlatform
{
    public string Name => "simulator";

    public int PermissionRequests { get; private set; }

    public int SettingsOpened { get; private set; }

    public Task<bool> RequestPermissionsAsync()
    {
        PermissionRequests++;
        return Task.FromResult(true);
    }

    public void OpenBluetoothSettings() => SettingsOpened++;

    public void KeepLinkAlive(bool enabled)
    {
    }
}

/// <summary>
/// Everything the phone app runs, once per process: the device registry, the phone's audio link and the computer link.
/// On Android the foreground service and the activity share it, so the link outlives the screen.
/// </summary>
public sealed class MobileRuntime : IAsyncDisposable
{
    private int _started;

    public MobileRuntime(
        IReadOnlyList<IDeviceSource> sources,
        IPhoneAudioLink audio,
        PeerStore store,
        string phoneName,
        IBeaconListener beacons,
        IMobilePlatform platform,
        TimeProvider? time = null,
        PhoneLinkClientOptions? clientOptions = null,
        DeviceProfileDb? profiles = null,
        SettingsStore? settings = null)
    {
        Profiles = profiles ?? DeviceProfileDb.Empty;
        Looks = new ArtworkChoices(settings);
        Time = time ?? TimeProvider.System;
        Registry = new DeviceRegistry(sources);
        Audio = audio;
        Platform = platform;
        PhoneName = phoneName;
        Computer = new PhoneLinkClient(store, phoneName, new PhoneAgent(audio), beacons, Time, clientOptions);
    }

    public DeviceRegistry Registry { get; }

    /// <summary>The devices/ profiles: model names and the desktop's artwork photos.</summary>
    public DeviceProfileDb Profiles { get; }

    /// <summary>The artwork look picked per device ("Change look"), as on the desktop.</summary>
    public ArtworkChoices Looks { get; }

    public IPhoneAudioLink Audio { get; }

    public PhoneLinkClient Computer { get; }

    public IMobilePlatform Platform { get; }

    public string PhoneName { get; }

    public TimeProvider Time { get; }

    /// <summary>Starts discovery and the computer link. Safe to call more than once.</summary>
    public async Task StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            Computer.Wake();
            return;
        }

        Computer.Start();
        await Registry.RefreshAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Computer.DisposeAsync();
        await Registry.DisposeAsync();
    }
}

/// <summary>
/// The "Change look" choice per device, kept in <see cref="AppSettings.ArtworkChoices"/> like the desktop.
/// Without a settings file (tests, simulator) choices last until the app closes.
/// </summary>
public sealed class ArtworkChoices(SettingsStore? store)
{
    private readonly Dictionary<string, string> _choices = new(store?.Load().ArtworkChoices ?? new Dictionary<string, string>());

    public string? Get(string deviceId) => _choices.GetValueOrDefault(deviceId);

    public void Set(string deviceId, string? choice)
    {
        if (choice is null or ArtworkLook.Automatic)
        {
            _choices.Remove(deviceId);
        }
        else
        {
            _choices[deviceId] = choice;
        }

        if (store is not null)
        {
            store.Save(store.Load() with { ArtworkChoices = new Dictionary<string, string>(_choices) });
        }
    }
}
