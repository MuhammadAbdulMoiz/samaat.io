using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Samaat.UI;

public static class UiConverters
{
    /// <summary>Looks up an icon geometry by resource key, e.g. "Icon.Phone".</summary>
    public static readonly IValueConverter IconByKey =
        new FuncValueConverter<string?, Geometry?>(key =>
            key is not null && Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null);
}
