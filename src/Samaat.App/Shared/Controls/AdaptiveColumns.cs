using Avalonia;
using Avalonia.Controls;

namespace Samaat.App.Shared.Controls;

/// <summary>
/// Lays children out in equal-width columns: as many as fit at <see cref="MinColumnWidth"/>, up to
/// <see cref="MaxColumns"/>. Four stat tiles become two rows of two in a narrow window.
/// </summary>
public sealed class AdaptiveColumns : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(MinColumnWidth), 200);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveColumns, int>(nameof(MaxColumns), 4);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(Spacing), 16);

    static AdaptiveColumns()
    {
        AffectsMeasure<AdaptiveColumns>(MinColumnWidthProperty, MaxColumnsProperty, SpacingProperty);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The column count used by the last layout pass.</summary>
    public int Columns { get; private set; } = 1;

    private int ColumnsFor(double width)
    {
        var limit = Math.Max(1, Math.Min(MaxColumns, Children.Count));
        if (double.IsInfinity(width))
        {
            return limit;
        }

        var fit = (int)Math.Floor((width + Spacing) / (MinColumnWidth + Spacing));
        return Math.Clamp(fit, 1, limit);
    }

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinColumnWidth : (width - Spacing * (columns - 1)) / columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        Columns = ColumnsFor(availableSize.Width);
        var columnWidth = ColumnWidth(availableSize.Width, Columns);
        var height = 0.0;
        for (var row = 0; row * Columns < Children.Count; row++)
        {
            var rowHeight = 0.0;
            for (var i = row * Columns; i < Math.Min(Children.Count, (row + 1) * Columns); i++)
            {
                Children[i].Measure(new Size(columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            }

            height += rowHeight + (row > 0 ? Spacing : 0);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? Columns * columnWidth + Spacing * (Columns - 1) : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Columns = ColumnsFor(finalSize.Width);
        var columnWidth = ColumnWidth(finalSize.Width, Columns);
        var y = 0.0;
        for (var row = 0; row * Columns < Children.Count; row++)
        {
            var items = Children.Skip(row * Columns).Take(Columns).ToList();
            var rowHeight = items.Max(c => c.DesiredSize.Height);
            for (var col = 0; col < items.Count; col++)
            {
                items[col].Arrange(new Rect(col * (columnWidth + Spacing), y, columnWidth, rowHeight));
            }

            y += rowHeight + Spacing;
        }

        return finalSize;
    }
}
