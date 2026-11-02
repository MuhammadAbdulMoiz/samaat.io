using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Samaat.App.Shared.Controls;

/// <summary>A circular battery gauge: a full track with an arc for the charge, starting at 12 o'clock.</summary>
public sealed class BatteryRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<BatteryRing, double>(nameof(Value));

    /// <summary>False draws only the track (level unknown or not reported).</summary>
    public static readonly StyledProperty<bool> HasValueProperty =
        AvaloniaProperty.Register<BatteryRing, bool>(nameof(HasValue), true);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<BatteryRing, double>(nameof(Thickness), 8);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<BatteryRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<BatteryRing, IBrush?>(nameof(ValueBrush));

    static BatteryRing()
    {
        AffectsRender<BatteryRing>(ValueProperty, HasValueProperty, ThicknessProperty, TrackBrushProperty, ValueBrushProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool HasValue
    {
        get => GetValue(HasValueProperty);
        set => SetValue(HasValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? ValueBrush
    {
        get => GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var radius = (size - Thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (TrackBrush is not null)
        {
            context.DrawEllipse(null, new Pen(TrackBrush, Thickness), center, radius, radius);
        }

        var fraction = Math.Clamp(Value / 100.0, 0, 1);
        if (!HasValue || ValueBrush is null || fraction <= 0)
        {
            return;
        }

        var pen = new Pen(ValueBrush, Thickness, lineCap: PenLineCap.Round);
        if (fraction >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
