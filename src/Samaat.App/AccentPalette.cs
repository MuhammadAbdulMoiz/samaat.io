using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Samaat.Core.Settings;

namespace Samaat.App;

/// <summary>The accent presets. Applying one replaces the accent brushes in both theme dictionaries.</summary>
public static class AccentPalette
{
    private sealed record Tone(string Accent, string On, byte Soft);

    // Values chosen against the surface, track and rail tokens; ContrastTests checks every preset in both themes.
    private static readonly string[] SliderKeys =
    [
        "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
        "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
    ];

    private static readonly Dictionary<AccentChoice, (Tone Dark, Tone Light)> Presets = new()
    {
        [AccentChoice.Lime] = (new("#C8F169", "#0B0D10", 0x26), new("#4A7A00", "#FFFFFF", 0x1F)),
        [AccentChoice.Emerald] = (new("#3DDC97", "#0B0D10", 0x26), new("#00704A", "#FFFFFF", 0x1F)),
        [AccentChoice.Ice] = (new("#6CC4FF", "#0B0D10", 0x26), new("#0A66B8", "#FFFFFF", 0x1F)),
        [AccentChoice.Mono] = (new("#E6E9EC", "#0B0D10", 0x26), new("#23282E", "#FFFFFF", 0x1F)),
    };

    public static void Apply(Application app, AccentChoice choice)
    {
        var (dark, light) = Presets[choice];
        foreach (var dictionary in Dictionaries(app))
        {
            foreach (var (variant, tone) in new[] { (ThemeVariant.Dark, dark), (ThemeVariant.Light, light) })
            {
                if (dictionary.ThemeDictionaries.TryGetValue(variant, out var provider)
                    && provider is IResourceDictionary themed && themed.ContainsKey("Samaat.Accent"))
                {
                    var accent = Color.Parse(tone.Accent);
                    themed["Samaat.Accent"] = new SolidColorBrush(accent);
                    themed["Samaat.Wave"] = new SolidColorBrush(accent);
                    themed["Samaat.AccentSoft"] = new SolidColorBrush(Color.FromArgb(tone.Soft, accent.R, accent.G, accent.B));
                    themed["Samaat.OnAccent"] = new SolidColorBrush(Color.Parse(tone.On));
                    themed["SystemAccentColor"] = accent;
                    // Fluent derives the slider colours from its own light/dark accent shades, so set them directly.
                    foreach (var key in SliderKeys)
                    {
                        themed[key] = new SolidColorBrush(accent);
                    }
                }
            }
        }
    }

    private static IEnumerable<IResourceDictionary> Dictionaries(Application app)
    {
        yield return app.Resources;
        foreach (var merged in app.Resources.MergedDictionaries)
        {
            object resolved = merged is ResourceInclude include ? include.Loaded : merged;
            if (resolved is IResourceDictionary dictionary)
            {
                yield return dictionary;
            }
        }
    }
}
