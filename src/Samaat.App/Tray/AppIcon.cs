using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Samaat.App.Tray;

/// <summary>The app icon, drawn at runtime (accent rounded square with an "S") so no binary asset is shipped.</summary>
public static class AppIcon
{
    /// <summary>Must run on the UI thread. Returns PNG bytes.</summary>
    public static byte[] RenderPng(int size = 64)
    {
        var px = new PixelSize(size, size);
        var visual = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.3),
            Background = new SolidColorBrush(Color.Parse("#C8F169")),
            Child = new TextBlock
            {
                Text = "S",
                FontSize = size * 0.62,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#0B0D10")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        visual.Measure(new Size(size, size));
        visual.Arrange(new Rect(0, 0, size, size));

        using var bitmap = new RenderTargetBitmap(px);
        bitmap.Render(visual);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
