using Avalonia.Controls;
using Avalonia.Platform;

namespace Samaat.App.Tray;

/// <summary>The app icon (the "Bold S" tile). Source art: docs/logo/samaat-icon.svg.</summary>
public static class AppIcon
{
    /// <summary>256px PNG bytes, used for toasts.</summary>
    public static byte[] Png()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Samaat/Assets/samaat-icon.png"));
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The multi-size .ico, so Windows picks a sharp size for the taskbar and tray.</summary>
    public static WindowIcon Window() => new(AssetLoader.Open(new Uri("avares://Samaat/Assets/samaat.ico")));
}
