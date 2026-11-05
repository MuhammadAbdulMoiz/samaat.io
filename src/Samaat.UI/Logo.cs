using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Samaat.UI;

/// <summary>
/// The Samaat logo: a dark rounded tile with a glowing lime "S" (Assets/samaat-icon.png). Its colours are fixed (they don't
/// follow the accent preset or the theme), so the mark is the same on the desktop sidebar, the window and tray icon, the
/// phone and the Android launcher (samaat-mobile repo: src/Samaat.Android/Resources/drawable/icon.png, the same file).
/// </summary>
public static class Logo
{
    /// <summary>The logo as an image source: <c>&lt;Image Source="{x:Static ui:Logo.Image}" /&gt;</c>.</summary>
    public static IImage Image { get; } = new Bitmap(AssetLoader.Open(new Uri("avares://Samaat.UI/Assets/samaat-icon.png")));

    /// <summary>The logo as PNG bytes, for window and tray icons. Must run on the UI thread.</summary>
    public static byte[] RenderPng(int size = 64)
    {
        var visual = new Avalonia.Controls.Image { Source = Image, Width = size, Height = size, Stretch = Stretch.Uniform };
        visual.Measure(new Size(size, size));
        visual.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(visual);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
