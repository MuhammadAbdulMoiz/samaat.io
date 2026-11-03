using Avalonia;
using Avalonia.Controls;

namespace Samaat.App.Shared.Controls;

/// <summary>
/// Two children: the first fills the remaining width, the second keeps its natural width beside it.
/// When the first would get less than <see cref="MinPrimaryWidth"/>, the second moves underneath.
/// </summary>
public sealed class SplitPanel : Panel
{
    public static readonly StyledProperty<double> MinPrimaryWidthProperty =
        AvaloniaProperty.Register<SplitPanel, double>(nameof(MinPrimaryWidth), 280);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SplitPanel, double>(nameof(Spacing), 24);

    public static readonly StyledProperty<bool> SecondaryFirstProperty =
        AvaloniaProperty.Register<SplitPanel, bool>(nameof(SecondaryFirst));

    public static readonly DirectProperty<SplitPanel, bool> IsStackedProperty =
        AvaloniaProperty.RegisterDirect<SplitPanel, bool>(nameof(IsStacked), p => p.IsStacked);

    private bool _isStacked;

    static SplitPanel()
    {
        AffectsMeasure<SplitPanel>(MinPrimaryWidthProperty, SpacingProperty);
        AffectsArrange<SplitPanel>(SecondaryFirstProperty);
    }

    public double MinPrimaryWidth
    {
        get => GetValue(MinPrimaryWidthProperty);
        set => SetValue(MinPrimaryWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Puts the natural-width part on the left, or on top when stacked.</summary>
    public bool SecondaryFirst
    {
        get => GetValue(SecondaryFirstProperty);
        set => SetValue(SecondaryFirstProperty, value);
    }

    public bool IsStacked
    {
        get => _isStacked;
        private set => SetAndRaise(IsStackedProperty, ref _isStacked, value);
    }

    private (Control? Primary, Control? Secondary) Parts() =>
        (Children.Count > 0 ? Children[0] : null, Children.Count > 1 ? Children[1] : null);

    private bool ShouldStack(double width, double secondaryWidth) =>
        !double.IsInfinity(width) && width - secondaryWidth - Spacing < MinPrimaryWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var (primary, secondary) = Parts();
        if (primary is null)
        {
            return default;
        }

        if (secondary is null)
        {
            primary.Measure(availableSize);
            return primary.DesiredSize;
        }

        secondary.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var sw = secondary.DesiredSize.Width;
        IsStacked = ShouldStack(availableSize.Width, sw);

        if (IsStacked)
        {
            primary.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return new Size(
                Math.Max(primary.DesiredSize.Width, sw),
                primary.DesiredSize.Height + Spacing + secondary.DesiredSize.Height);
        }

        var primaryWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width - sw - Spacing;
        primary.Measure(new Size(primaryWidth, double.PositiveInfinity));
        return new Size(
            primary.DesiredSize.Width + Spacing + sw,
            Math.Max(primary.DesiredSize.Height, secondary.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (primary, secondary) = Parts();
        if (primary is null)
        {
            return finalSize;
        }

        if (secondary is null)
        {
            primary.Arrange(new Rect(finalSize));
            return finalSize;
        }

        var sw = Math.Min(secondary.DesiredSize.Width, finalSize.Width);
        IsStacked = ShouldStack(finalSize.Width, sw);
        var secondaryFirst = SecondaryFirst;
        if (IsStacked)
        {
            var (ph, sh) = (primary.DesiredSize.Height, secondary.DesiredSize.Height);
            primary.Arrange(new Rect(0, secondaryFirst ? sh + Spacing : 0, finalSize.Width, ph));
            secondary.Arrange(new Rect(0, secondaryFirst ? 0 : ph + Spacing, sw, sh));
        }
        else
        {
            var pw = finalSize.Width - sw - Spacing;
            primary.Arrange(new Rect(secondaryFirst ? sw + Spacing : 0, 0, pw, finalSize.Height));
            secondary.Arrange(new Rect(secondaryFirst ? 0 : finalSize.Width - sw, 0, sw, finalSize.Height));
        }

        return finalSize;
    }
}
