using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Samaat.Core.Drivers;
using Samaat.Handoff.Storage;
using Samaat.Mobile.Runtime;
using Samaat.Platform.Android;

namespace Samaat.Android;

/// <summary>
/// Builds the phone app's runtime once per process. The activity and the foreground service share it, so the computer
/// link keeps running when the screen goes away.
/// </summary>
internal static class AndroidHost
{
    private static readonly Lock Gate = new();
    private static MobileRuntime? _runtime;
    private static AndroidPhoneAudio? _audio;

    /// <summary>The activity while it's on screen; prompts and permission requests need it.</summary>
    public static Activity? Foreground { get; set; }

    public static AndroidPhoneAudio? Audio => _audio;

    public static MobileRuntime Get(Context context)
    {
        lock (Gate)
        {
            if (_runtime is not null)
            {
                return _runtime;
            }

            var app = context.ApplicationContext ?? context;
            var prompt = new AndroidUserPrompt(app, () => Foreground, Resource.Drawable.icon);
            _audio = new AndroidPhoneAudio(app, prompt);
            var registry = new DriverRegistry(LoadCatalog(app));
            registry.Register(new Samaat.Protocols.Jieli.JieliDriver());
            registry.Register(new Samaat.Protocols.FastPair.FastPairDriver());
            var store = new PeerStore(Path.Combine(app.FilesDir!.AbsolutePath, "computers.json"));
            _runtime = new MobileRuntime(
                [new AndroidDeviceSource(app, _audio, registry)],
                _audio,
                store,
                PhoneName(app),
                new AndroidBeaconListener(app),
                new AndroidMobilePlatform(app, _audio));
            return _runtime;
        }
    }

    /// <summary>The devices/ catalog ships as APK assets.</summary>
    private static DeviceCatalog LoadCatalog(Context context)
    {
        var documents = new List<string>();
        void Walk(string path)
        {
            foreach (var name in context.Assets?.List(path) ?? [])
            {
                var child = $"{path}/{name}";
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = context.Assets!.Open(child);
                    using var reader = new StreamReader(stream);
                    documents.Add(reader.ReadToEnd());
                }
                else if (!name.Contains('.'))
                {
                    Walk(child);
                }
            }
        }

        try
        {
            Walk("devices");
            return DeviceCatalog.Parse(documents);
        }
        catch (Exception)
        {
            return DeviceCatalog.Empty;
        }
    }

    /// <summary>The name the user gave the phone, as the computer will show it.</summary>
    private static string PhoneName(Context context)
    {
        var name = global::Android.Provider.Settings.Global.GetString(context.ContentResolver, "device_name");
        return string.IsNullOrWhiteSpace(name) ? $"{Build.Manufacturer} {Build.Model}".Trim() : name;
    }

    // Permission requests: the activity forwards results here.
    public const int PermissionRequestCode = 4711;
    private static TaskCompletionSource<bool>? _permission;

    public static Task<bool> RequestPermissionsAsync(string[] permissions)
    {
        if (Foreground is not { } activity || permissions.Length == 0)
        {
            return Task.FromResult(false);
        }

        _permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() => activity.RequestPermissions(permissions, PermissionRequestCode));
        return _permission.Task;
    }

    public static void OnPermissionResult(int requestCode, Permission[] results)
    {
        if (requestCode == PermissionRequestCode)
        {
            _permission?.TrySetResult(results.All(r => r == Permission.Granted));
        }
    }
}

/// <summary>What only Android can do for the shared UI.</summary>
internal sealed class AndroidMobilePlatform(Context context, AndroidPhoneAudio audio) : IMobilePlatform
{
    public string Name => "android";

    public async Task<bool> RequestPermissionsAsync()
    {
        var wanted = new List<string>();
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            wanted.Add(global::Android.Manifest.Permission.BluetoothConnect);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            wanted.Add(global::Android.Manifest.Permission.PostNotifications);
        }

        var missing = wanted.Where(p => context.CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (missing.Length > 0)
        {
            await AndroidHost.RequestPermissionsAsync(missing);
        }

        audio.Refresh();
        return audio.Blocker is null;
    }

    public void OpenBluetoothSettings()
    {
        var adapter = (context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
        var action = adapter is { IsEnabled: false } ? BluetoothAdapter.ActionRequestEnable : global::Android.Provider.Settings.ActionBluetoothSettings;
        var intent = new Intent(action).AddFlags(ActivityFlags.NewTask);
        (AndroidHost.Foreground ?? context).StartActivity(intent);
    }

    public void KeepLinkAlive(bool enabled)
    {
        var intent = new Intent(context, typeof(LinkService));
        if (!enabled)
        {
            context.StopService(intent);
            return;
        }

        try
        {
            context.StartForegroundService(intent);
        }
        catch (Java.Lang.Exception)
        {
            // Not allowed right now (app in the background on Android 12+); the link still runs while the app is open.
        }
    }
}
