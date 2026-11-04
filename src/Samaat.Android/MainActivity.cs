using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Samaat.Mobile;

namespace Samaat.Android;

[Activity(
    Label = "Samaat",
    Theme = "@style/Samaat.Theme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<MobileApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        MobileApp.RuntimeFactory = () => AndroidHost.Get(this);
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidHost.Foreground = this;
        // Back from settings or another app: check the computer link and Bluetooth again straight away.
        var runtime = AndroidHost.Get(this);
        runtime.Computer.Wake();
        AndroidHost.Audio?.Refresh();
    }

    protected override void OnPause()
    {
        if (ReferenceEquals(AndroidHost.Foreground, this))
        {
            AndroidHost.Foreground = null;
        }

        base.OnPause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        AndroidHost.OnPermissionResult(requestCode, grantResults);
    }
}
