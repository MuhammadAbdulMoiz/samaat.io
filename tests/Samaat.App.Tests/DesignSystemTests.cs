using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

/// <summary>
/// Keeps the design system the single source of truth: colours live in Tokens.axaml, and shape, type and elevation
/// in Metrics.axaml (both in Samaat.UI). Styles and views refer to those keys instead of repeating literal values,
/// so a change to a token changes the whole app, and the phone app that shares Samaat.UI.
/// </summary>
public class DesignSystemTests
{
    private static readonly string[] TokenFiles = ["Tokens.axaml", "Metrics.axaml"];

    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Samaat.slnx")))
        {
            dir = dir.Parent ?? throw new InvalidOperationException("Samaat.slnx not found above the test output");
        }

        return dir;
    }

    /// <summary>Every .axaml file in the desktop app and the shared design system, except the token dictionaries.</summary>
    public static TheoryData<string> MarkupFiles()
    {
        var data = new TheoryData<string>();
        foreach (var project in new[] { "Samaat.App", "Samaat.UI" })
        {
            var dir = Path.Combine(Root.FullName, "src", project);
            foreach (var file in Directory.EnumerateFiles(dir, "*.axaml", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                     && !TokenFiles.Contains(Path.GetFileName(f)))
                         .Order())
            {
                data.Add(Path.GetRelativePath(Root.FullName, file).Replace('\\', '/'));
            }
        }

        return data;
    }

    private static IReadOnlyList<string> Violations(string relativePath, Regex pattern)
    {
        var lines = File.ReadAllLines(Path.Combine(Root.FullName, relativePath));
        return lines
            .Select((line, i) => (line, number: i + 1))
            .Where(l => pattern.IsMatch(l.line))
            .Select(l => $"{relativePath}:{l.number}: {l.line.Trim()}")
            .ToList();
    }

    private static Regex Attribute(string property, string value) =>
        new($"""(\b{property}="{value}"|Property="{property}"\s+Value="{value}")""", RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void Markup_UsesColourTokens_NotHexLiterals(string file)
    {
        Assert.Empty(Violations(file, new Regex("#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\\b")));
    }

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void Markup_UsesTheTypeRamp_NotLiteralFontSizes(string file)
    {
        Assert.Empty(Violations(file, Attribute("FontSize", "[0-9][^\"]*")));
    }

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void Markup_UsesElevationTokens_NotLiteralShadows(string file)
    {
        Assert.Empty(Violations(file, Attribute("BoxShadow", "[0-9][^\"]*")));
    }

    /// <summary>
    /// A radius that has a token must use it. Round shapes (a radius of half the element's size, such as a 32 px
    /// swatch's 16) are geometry rather than style, so they may stay literal.
    /// </summary>
    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void Markup_UsesRadiusTokens_WhereOneExists(string file)
    {
        Assert.Empty(Violations(file, Attribute("CornerRadius", "(2|6|7|8|10)")));
    }

    [AvaloniaTheory]
    [InlineData("Samaat.Radius.Hairline")]
    [InlineData("Samaat.Radius.Control")]
    [InlineData("Samaat.Radius.Tab")]
    [InlineData("Samaat.Radius.Tile")]
    [InlineData("Samaat.Radius.Panel")]
    [InlineData("Samaat.Radius.Round20")]
    [InlineData("Samaat.FontSize.Label")]
    [InlineData("Samaat.FontSize.Meta")]
    [InlineData("Samaat.FontSize.Body")]
    [InlineData("Samaat.FontSize.PanelTitle")]
    [InlineData("Samaat.FontSize.Heading")]
    [InlineData("Samaat.FontSize.Brand")]
    [InlineData("Samaat.FontSize.Title")]
    [InlineData("Samaat.FontSize.FigureSmall")]
    [InlineData("Samaat.FontSize.FigureMedium")]
    [InlineData("Samaat.FontSize.Figure")]
    [InlineData("Samaat.FontSize.Code")]
    [InlineData("Samaat.Size.Control")]
    [InlineData("Samaat.Size.Row")]
    [InlineData("Samaat.Size.OptionRow")]
    [InlineData("Samaat.Padding.Panel")]
    [InlineData("Samaat.Padding.Strip")]
    [InlineData("Samaat.Shadow.Key")]
    [InlineData("Samaat.Shadow.Overlay")]
    public void MetricTokens_ResolveInBothThemes(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _), key);
        Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out _), key);
    }

    [AvaloniaTheory]
    [InlineData(AccentChoice.Lime, "Samaat.Swatch.Lime")]
    [InlineData(AccentChoice.Emerald, "Samaat.Swatch.Emerald")]
    [InlineData(AccentChoice.Ice, "Samaat.Swatch.Ice")]
    [InlineData(AccentChoice.Mono, "Samaat.Swatch.Mono")]
    public void AccentSwatch_ShowsThePresetsDarkAccent(AccentChoice choice, string swatchKey)
    {
        var app = Application.Current!;
        try
        {
            AccentPalette.Apply(app, choice);

            Assert.True(app.TryGetResource("Samaat.Accent", ThemeVariant.Dark, out var accent));
            Assert.True(app.TryGetResource(swatchKey, ThemeVariant.Dark, out var swatch), swatchKey);
            Assert.Equal(((ISolidColorBrush)accent!).Color, ((ISolidColorBrush)swatch!).Color);
        }
        finally
        {
            AccentPalette.Apply(app, AccentChoice.Lime);
        }
    }

    [AvaloniaFact]
    public void AccentSwatchCheck_UsesThePresetsInk()
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource("Samaat.OnAccent", ThemeVariant.Dark, out var onAccent));
        Assert.True(app.TryGetResource("Samaat.Swatch.Check", ThemeVariant.Dark, out var check));
        Assert.Equal(((ISolidColorBrush)onAccent!).Color, ((ISolidColorBrush)check!).Color);
    }
}
