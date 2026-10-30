using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Samaat.App.Controls;

/// <summary>Concentric rings fading outwards from a point: the "hearing" motif behind the device hero.</summary>
public sealed class SoundWaves : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SoundWaves, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<SoundWaves, int>(nameof(Count), 7);

    /// <summary>Origin as a fraction of the bounds.</summary>
    public static readonly StyledProperty<RelativePoint> OriginProperty =
        AvaloniaProperty.Register<SoundWaves, RelativePoint>(nameof(Origin), new RelativePoint(0.72, 0.5, RelativeUnit.Relative));

    static SoundWaves()
    {
        AffectsRender<SoundWaves>(StrokeProperty, CountProperty, OriginProperty);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public RelativePoint Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Stroke is null || Count <= 0)
        {
            return;
        }

        var center = Origin.ToPixels(Bounds.Size);
        var step = Math.Max(Bounds.Width, Bounds.Height) / (Count + 1) * 0.75;
        for (var i = 1; i <= Count; i++)
        {
            using (context.PushOpacity(1.0 - (double)i / (Count + 1)))
            {
                var radius = step * i;
                context.DrawEllipse(null, new Pen(Stroke, 1), center, radius, radius);
            }
        }
    }
}
