using Android.App;
using Android.Content;
using Android.Media;
using Android.Provider;

namespace Samaat.Platform.Android;

/// <summary>
/// Asks the user for the one tap Android requires: with the app on screen it opens the system output switcher
/// (Android 14+) or the media output panel, else Bluetooth settings; in the background it posts a notification,
/// because apps can't start activities from the background.
/// </summary>
public sealed class AndroidUserPrompt(Context context, Func<Activity?> foreground, int smallIcon) : IAudioUserPrompt
{
    public const string ChannelId = "samaat.moves";
    private const int NotificationId = 2001;

    private readonly Context _context = context.ApplicationContext ?? context;

    public void AskToConnect(string deviceName)
    {
        if (foreground() is { } activity)
        {
            activity.RunOnUiThread(() => OpenOutputPicker(activity));
        }

        Notify($"Choose {deviceName} to finish", "Tap, then pick your earbuds to play audio on this phone.", OutputPickerIntent());
    }

    public void AskToDisconnect(string deviceName)
    {
        if (foreground() is { } activity)
        {
            activity.RunOnUiThread(() => activity.StartActivity(BluetoothSettingsIntent()));
        }

        Notify($"Disconnect {deviceName} here", "Tap, then disconnect your earbuds so the computer can take them.", BluetoothSettingsIntent());
    }

    public void Dismiss() => (_context.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(NotificationId);

    private static void OpenOutputPicker(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            try
            {
                if (MediaRouter2.GetInstance(activity).ShowSystemOutputSwitcher())
                {
                    return;
                }
            }
            catch (Java.Lang.Exception)
            {
                // Fall through to the settings screens.
            }
        }

        try
        {
            activity.StartActivity(OutputPickerIntent(activity.PackageName));
        }
        catch (ActivityNotFoundException)
        {
            activity.StartActivity(BluetoothSettingsIntent());
        }
    }

    /// <summary>The media output panel (Android 10–13); phones without it fall back to Bluetooth settings.</summary>
    private Intent OutputPickerIntent() => OperatingSystem.IsAndroidVersionAtLeast(29) && !OperatingSystem.IsAndroidVersionAtLeast(34)
        ? OutputPickerIntent(_context.PackageName)
        : BluetoothSettingsIntent();

    private static Intent OutputPickerIntent(string? packageName)
    {
        var intent = new Intent("com.android.settings.panel.action.MEDIA_OUTPUT");
        intent.PutExtra("com.android.settings.panel.extra.PACKAGE_NAME", packageName);
        intent.AddFlags(ActivityFlags.NewTask);
        return intent;
    }

    private static Intent BluetoothSettingsIntent() => new Intent(Settings.ActionBluetoothSettings).AddFlags(ActivityFlags.NewTask);

    private void Notify(string title, string text, Intent intent)
    {
        if (_context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Moving earbuds", NotificationImportance.High)
        {
            Description = "Asks you to pick your earbuds when Android won't let Samaat connect them itself.",
        });
        var pending = PendingIntent.GetActivity(_context, NotificationId, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(_context, ChannelId)
            .SetSmallIcon(smallIcon)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetContentIntent(pending)
            .SetAutoCancel(true)
            .SetCategory(Notification.CategoryCall)
            .Build();
        manager.Notify(NotificationId, notification);
    }
}
