using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Samaat.Android;

/// <summary>
/// Keeps the computer link alive while Samaat is in the background, so a move started on the computer can reach the
/// phone. A connected-device foreground service with a quiet, ongoing notification.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class LinkService : Service
{
    private const string ChannelId = "samaat.link";
    private const int NotificationId = 1001;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelId, "Computer link", NotificationImportance.Low)
        {
            Description = "Shown while Samaat stays reachable from your computer.",
        });

        var open = PackageManager?.GetLaunchIntentForPackage(PackageName ?? "");
        var pending = open is null ? null : PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable);
        var notification = new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetContentTitle("Samaat")
            .SetContentText("Ready to move your earbuds between this phone and your computer.")
            .SetContentIntent(pending)
            .SetOngoing(true)
            .Build();

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        catch (Java.Lang.Exception)
        {
            // Android 14 refuses a connected-device service until the Bluetooth permission is granted.
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        _ = AndroidHost.Get(this).StartAsync();
        return StartCommandResult.Sticky;
    }
}
