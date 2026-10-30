using Avalonia.Data.Converters;

namespace Samaat.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter EnabledOpacity =
        new FuncValueConverter<bool, double>(enabled => enabled ? 1.0 : 0.5);
}
