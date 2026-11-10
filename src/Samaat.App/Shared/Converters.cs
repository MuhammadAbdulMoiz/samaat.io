using Avalonia.Media;
using Avalonia.Data.Converters;

namespace Samaat.App.Shared;

public static class Converters
{
    /// <summary>Mirrors a glyph left-to-right when true (the right bud is the left bud flipped).</summary>
    public static readonly IValueConverter MirrorIf =
        new FuncValueConverter<bool, ITransform?>(mirror => mirror ? new ScaleTransform(-1, 1) : null);

    /// <summary>Looks up an icon geometry by resource key, e.g. "Icon.Phone".</summary>
    public static readonly IValueConverter IconByKey = Samaat.UI.UiConverters.IconByKey;
}
