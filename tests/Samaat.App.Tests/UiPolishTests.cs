using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using Samaat.App.Features.Settings;
using Samaat.App.Shared.Controls;
using Samaat.App.Shell;
using Samaat.Core.Battery;
using Samaat.Core.Platform;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public class ShellBehaviourTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel Vm, FakeTimeProvider Time)> ShowAsync()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), time);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, time);
    }

    [AvaloniaFact]
    public async Task Toasts_HideThemselvesAfterAWhile()
    {
        var (window, vm, time) = await ShowAsync();
        Assert.NotEmpty(vm.Alerts);

        time.Advance(MainWindowViewModel.ToastLifetime + TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Alerts);
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task AlertCounter_TracksDevicesWithLowBattery_AndJumpsToThem()
    {
        var (window, vm, _) = await ShowAsync();

        Assert.Equal(1, vm.AlertingDeviceCount);
        Assert.True(vm.HasAlertingDevices);

        vm.NavigateCommand.Execute(AppSection.Sound);
        vm.ShowAlertingDeviceCommand.Execute(null);

        Assert.Equal("Simulated Buds Lite", vm.SelectedDevice?.Name);
        Assert.True(vm.IsOverview);
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task CurrentPage_FollowsSectionAndDevice()
    {
        var (window, vm, _) = await ShowAsync();

        Assert.IsType<OverviewPageModel>(vm.CurrentPage);
        vm.NavigateCommand.Execute(AppSection.Connections);
        var page = Assert.IsType<ConnectionsPageModel>(vm.CurrentPage);
        Assert.Same(vm.SelectedDevice, page.Device);

        vm.SelectedDevice = vm.Devices[2];
        Assert.Same(vm.Devices[2], Assert.IsType<ConnectionsPageModel>(vm.CurrentPage).Device);
        window.Close();
        await vm.DisposeAsync();
    }
}

public class ResponsivePanelTests
{
    private static Border Child(double width, double height) => new() { Width = width, Height = height };

    private static void Layout(Panel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
    }

    [AvaloniaFact]
    public void SplitPanel_SideBySideWhenWide()
    {
        var primary = Child(double.NaN, 100);
        var secondary = Child(400, 150);
        var panel = new SplitPanel { MinPrimaryWidth = 300, Spacing = 20, Children = { primary, secondary } };

        Layout(panel, 1000);

        Assert.Equal(0, primary.Bounds.X);
        Assert.Equal(580, primary.Bounds.Width);
        Assert.Equal(600, secondary.Bounds.X);
        Assert.Equal(150, panel.DesiredSize.Height);
        Assert.False(panel.IsStacked);
    }

    [AvaloniaFact]
    public void SplitPanel_StacksWhenNarrow()
    {
        var primary = Child(double.NaN, 100);
        var secondary = Child(400, 150);
        var panel = new SplitPanel { MinPrimaryWidth = 300, Spacing = 20, Children = { primary, secondary } };

        Layout(panel, 600);

        Assert.True(panel.IsStacked);
        Assert.Equal(0, secondary.Bounds.X, 0);
        Assert.Equal(120, secondary.Bounds.Y);
        Assert.Equal(270, panel.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void SplitPanel_SecondaryFirst_PutsTheFixedPartOnTheLeft_AndOnTopWhenStacked()
    {
        var primary = Child(double.NaN, 100);
        var secondary = Child(260, 150);
        var panel = new SplitPanel { MinPrimaryWidth = 480, Spacing = 16, SecondaryFirst = true, Children = { primary, secondary } };

        Layout(panel, 900);

        Assert.False(panel.IsStacked);
        Assert.Equal(0, secondary.Bounds.X);
        Assert.Equal(276, primary.Bounds.X);
        Assert.Equal(624, primary.Bounds.Width);

        Layout(panel, 700);

        Assert.True(panel.IsStacked);
        Assert.Equal(0, secondary.Bounds.Y);
        Assert.Equal(166, primary.Bounds.Y);
        Assert.Equal(700, primary.Bounds.Width);
    }

    [AvaloniaTheory]
    [InlineData(1000, 4)]
    [InlineData(600, 3)]
    [InlineData(400, 2)]
    [InlineData(100, 1)]
    public void AdaptiveColumns_FitAsManyColumnsAsAllowed(double width, int expectedColumns)
    {
        var panel = new AdaptiveColumns { MinColumnWidth = 180, MaxColumns = 4, Spacing = 0 };
        for (var i = 0; i < 4; i++)
        {
            panel.Children.Add(Child(double.NaN, 50));
        }

        Layout(panel, width);

        Assert.Equal(expectedColumns, panel.Columns);
        Assert.Equal(width / expectedColumns, panel.Children[0].Bounds.Width, 3);
    }
}

public class ContrastTests
{
    private static Color Brush(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryGetResource(key, theme, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Flattens a translucent colour onto its background, as it renders.</summary>
    private static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Ratio(Color a, Color b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    public static TheoryData<string, string, double> TextPairs => new()
    {
        // WCAG 2.1 AA: 4.5:1 for body text.
        { "Samaat.Text", "Samaat.Bg", 4.5 },
        { "Samaat.Text", "Samaat.Surface", 4.5 },
        { "Samaat.Text", "Samaat.Surface2", 4.5 },
        { "Samaat.Muted", "Samaat.Surface", 4.5 },
        { "Samaat.Muted", "Samaat.Surface2", 4.5 },
        { "Samaat.Muted", "Samaat.Bg", 4.5 },
        { "Samaat.Muted", "Samaat.Rail", 4.5 },
        { "Samaat.Text", "Samaat.Rail", 4.5 },
        { "Samaat.Muted", "Samaat.Well", 4.5 },
        { "Samaat.Text", "Samaat.Key", 4.5 },
        { "Samaat.OnAccent", "Samaat.Accent", 4.5 },
        { "Samaat.Accent", "Samaat.Surface", 4.5 },
        { "Samaat.Warn", "Samaat.Surface", 4.5 },
        { "Samaat.Critical", "Samaat.Surface", 4.5 },
        // 3:1 for large text, icons and the meaningful parts of graphics.
        { "Samaat.Accent", "Samaat.Track", 3.0 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(TextPairs))]
    public void TokensMeetWcagAA_InBothThemes(string foreground, string background, double minimum)
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var ratio = Ratio(Brush(foreground, theme), Brush(background, theme));
            Assert.True(ratio >= minimum, $"{foreground} on {background} ({theme}) is {ratio:0.00}:1, needs {minimum}:1");
        }
    }


    public static TheoryData<AccentChoice> Presets => new(Enum.GetValues<AccentChoice>());

    [AvaloniaTheory]
    [MemberData(nameof(Presets))]
    public void EveryAccentPresetMeetsWcagAA_InBothThemes(AccentChoice preset)
    {
        var settings = new SettingsViewModel(
            new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json")),
            new LowBatteryDetector(), new NoStartupRegistration());
        try
        {
            settings.SetAccentCommand.Execute(preset);
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var accent = Brush("Samaat.Accent", theme);
                void Check(string what, Color foreground, Color background, double minimum)
                {
                    var ratio = Ratio(foreground, background);
                    Assert.True(ratio >= minimum, $"{preset}/{theme}: {what} is {ratio:0.00}:1, needs {minimum}:1");
                }

                Check("OnAccent on Accent", Brush("Samaat.OnAccent", theme), accent, 4.5);
                Check("Accent text on Surface", accent, Brush("Samaat.Surface", theme), 4.5);
                Check("Accent text on Surface2", accent, Brush("Samaat.Surface2", theme), 4.5);
                Check("Accent fill on Track", accent, Brush("Samaat.Track", theme), 3.0);
                Check("Accent icon on the active sidebar row", accent, Over(Brush("Samaat.AccentSoft", theme), Brush("Samaat.Rail", theme)), 3.0);
                Check("Wave on Surface", Brush("Samaat.Wave", theme), Brush("Samaat.Surface", theme), 3.0);
                Check("Selected option text on accent-soft", Brush("Samaat.Text", theme), Over(Brush("Samaat.AccentSoft", theme), Brush("Samaat.Surface", theme)), 4.5);
            }
        }
        finally
        {
            settings.SetAccentCommand.Execute(AccentChoice.Lime);
        }
    }
    [AvaloniaFact]
    public void Logo_HasFixedColours_WhateverTheAccent()
    {
        var png = Samaat.App.Tray.AppIcon.RenderPng(64);
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
        var pixels = new byte[64 * 64 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 64, 64), handle.AddrOfPinnedObject(), pixels.Length, 64 * 4);
        }
        finally
        {
            handle.Free();
        }

        // A point on the tile, clear of the "S": the logo's lime (BGRA), not the current accent.
        var i = (32 * 64 + 6) * 4;
        Assert.Equal(Samaat.UI.Logo.TileColor, Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]));
    }

    [AvaloniaFact]
    public void SelectedOptionDescription_UsesTheTextColour()
    {
        // Muted on accent-soft is 4.45:1 in dark mode, under AA; the style switches it to the text colour.
        var meta = new TextBlock { Text = "No processing", Classes = { "meta" } };
        var button = new Button { Classes = { "option", "selected" }, Content = meta };
        var window = new Window { Content = button };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(Brush("Samaat.Text", ThemeVariant.Dark), ((ISolidColorBrush)meta.Foreground!).Color);
        window.Close();
    }

    [AvaloniaFact]
    public void AlertPillsAreReadable_InBothThemes()
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var surface = Brush("Samaat.HeroBase", theme);
            foreach (var (text, soft) in new[] { ("Samaat.Warn", "Samaat.WarnSoft"), ("Samaat.Critical", "Samaat.CriticalSoft") })
            {
                var ratio = Ratio(Brush(text, theme), Over(Brush(soft, theme), surface));
                Assert.True(ratio >= 4.5, $"{text} on {soft} ({theme}) is {ratio:0.00}:1");
            }
        }
    }
}
