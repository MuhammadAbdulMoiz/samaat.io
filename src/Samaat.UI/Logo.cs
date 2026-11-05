using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace Samaat.UI;

/// <summary>
/// The Samaat logo: a lime tile with a dark "S". Its colours are fixed (they don't follow the accent preset or the theme),
/// so the mark is the same on the desktop sidebar, the window and tray icon, the phone and the Android launcher.
/// The paths are the Android launcher icon's (samaat-mobile repo: src/Samaat.Android/Resources/drawable/icon.xml), moved to a tight 64×64 box;
/// docs/images/logo.svg is the same drawing for documents.
/// </summary>
public static class Logo
{
    public static readonly Color TileColor = Color.Parse("#C8F169");
    public static readonly Color MarkColor = Color.Parse("#0B0D10");

    public const string TilePath = "M8,0h48a8,8 0,0 1,8 8v48a8,8 0,0 1,-8 8h-48a8,8 0,0 1,-8 -8v-48a8,8 0,0 1,8 -8z";

    public const string MarkPath =
        "M44,18c-2,-4 -6,-6 -12,-6c-7,0 -12,4 -12,10c0,6 5,8 11,9.5c5,1.2 7,2.3 7,5c0,3 -3,5 -7,5c-4.5,0 -7.5,-2 -9,-5.5l-5.5,3" +
        "c2.3,5 7.5,8.5 14.5,8.5c8,0 13.5,-4.3 13.5,-11c0,-6.5 -5,-8.7 -11.5,-10.2c-4.6,-1.1 -6.5,-2 -6.5,-4.6c0,-2.5 2.4,-4.2 5.8,-4.2" +
        "c3.4,0 5.8,1.5 7,4z";

    /// <summary>The logo as an image source: <c>&lt;Image Source="{x:Static ui:Logo.Image}" /&gt;</c>.</summary>
    public static DrawingImage Image { get; } = new(new DrawingGroup
    {
        Children =
        {
            new GeometryDrawing { Geometry = Geometry.Parse(TilePath), Brush = new ImmutableSolidColorBrush(TileColor) },
            new GeometryDrawing { Geometry = Geometry.Parse(MarkPath), Brush = new ImmutableSolidColorBrush(MarkColor) },
        },
    });

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
