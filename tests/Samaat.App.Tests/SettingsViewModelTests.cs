using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Settings;
using Samaat.App.Shell;
using Samaat.Core.Battery;
using Samaat.Core.Platform;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

internal sealed class FakeStartup(bool supported = true, bool enabled = false, bool fail = false) : IStartupRegistration
{
    public bool IsSupported { get; } = supported;

    public bool IsEnabled { get; private set; } = enabled;

    public void SetEnabled(bool value)
    {
        if (fail)
        {
            throw new UnauthorizedAccessException("denied");
        }

        IsEnabled = value;
    }
}

internal sealed class FakeMotion(bool reduce) : IMotionPreference
{
    public bool ReduceMotion { get; } = reduce;
}

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private SettingsStore Store() => new(Path.Combine(_dir, "settings.json"));

    private static SettingsViewModel Create(SettingsStore store, LowBatteryDetector? detector = null, IStartupRegistration? startup = null,
        IMotionPreference? motion = null) =>
        new(store, detector ?? new LowBatteryDetector(), startup ?? new FakeStartup(), motion);

    [AvaloniaFact]
    public void ChangingTheme_KeepsShareSettings()
    {
        var store = Store();
        store.Save(new AppSettings { ShareTargetId = "tv", ShareDelayMs = 80 });
        var vm = Create(store);
        vm.Theme = ThemeChoice.Dark;
        var saved = store.Load();
        Assert.Equal("tv", saved.ShareTargetId);
        Assert.Equal(80, saved.ShareDelayMs);
        vm.SetShare("hp", 20);
        Assert.Equal(("hp", 20), (store.Load().ShareTargetId, store.Load().ShareDelayMs));
    }

    [AvaloniaFact]
    public void LoadsSavedSettings_AndAppliesThemAtStartup()
    {
        var store = Store();
        store.Save(new AppSettings { Theme = ThemeChoice.Light, LowThreshold = 35, CriticalThreshold = 15, IncludeCase = true });
        var detector = new LowBatteryDetector();

        var vm = Create(store, detector);

        Assert.Equal(ThemeChoice.Light, vm.Theme);
        Assert.Equal(35, vm.LowThreshold);
        Assert.Equal(35, detector.Policy.LowThreshold);
        Assert.Equal(15, detector.Policy.CriticalThreshold);
        Assert.True(detector.Policy.IncludeCase);
        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public void ChangingThresholds_PersistsAndUpdatesDetector()
    {
        var store = Store();
        var detector = new LowBatteryDetector();
        var vm = Create(store, detector);

        vm.LowThreshold = 30;
        vm.CriticalThreshold = 12;
        vm.IncludeCase = true;

        Assert.Equal(30, detector.Policy.LowThreshold);
        Assert.Equal(12, detector.Policy.CriticalThreshold);
        Assert.True(detector.Policy.IncludeCase);
        var saved = Store().Load();
        Assert.Equal(30, saved.LowThreshold);
        Assert.Equal(12, saved.CriticalThreshold);
        Assert.True(saved.IncludeCase);
    }

    [AvaloniaFact]
    public void LoweringLowThreshold_PullsCriticalBelowIt()
    {
        var vm = Create(Store());

        vm.LowThreshold = 8;

        Assert.Equal(8, vm.LowThreshold);
        Assert.Equal(7, vm.CriticalThreshold);
        Assert.Equal(7, vm.CriticalMax);
        Assert.Equal(7, Store().Load().CriticalThreshold);
    }

    [AvaloniaFact]
    public void ChangingTheme_AppliesVariantAndPersists()
    {
        var vm = Create(Store());

        vm.SetThemeCommand.Execute(ThemeChoice.Light);

        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        Assert.True(vm.IsLightTheme);
        Assert.False(vm.IsDarkTheme);
        Assert.Equal(ThemeChoice.Light, Store().Load().Theme);

        vm.SetThemeCommand.Execute(ThemeChoice.System);
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public void StartWithWindows_TogglesRegistration()
    {
        var startup = new FakeStartup();
        var vm = Create(Store(), startup: startup);

        vm.StartWithWindows = true;

        Assert.True(startup.IsEnabled);
        Assert.True(Store().Load().StartWithWindows);
        Assert.False(vm.HasError);
    }

    [AvaloniaFact]
    public void StartWithWindows_ReflectsTheRegistryAtStartup()
    {
        var store = Store();
        store.Save(new AppSettings { StartWithWindows = true });

        Assert.False(Create(store, startup: new FakeStartup(enabled: false)).StartWithWindows);
        Assert.True(Create(store, startup: new FakeStartup(enabled: true)).StartWithWindows);
    }

    [AvaloniaFact]
    public void StartWithWindows_FailureRevertsToggleAndExplains()
    {
        var vm = Create(Store(), startup: new FakeStartup(fail: true));

        vm.StartWithWindows = true;

        Assert.False(vm.StartWithWindows);
        Assert.True(vm.HasError);
        Assert.Contains("denied", vm.ErrorText);
    }

    [AvaloniaFact]
    public void StartWithWindows_Unsupported_IsDisabledWithReason()
    {
        var vm = Create(Store(), startup: new FakeStartup(supported: false));

        Assert.False(vm.StartupSupported);
        Assert.False(vm.StartWithWindows);
        Assert.False(string.IsNullOrEmpty(vm.StartupNote));
    }

    [AvaloniaFact]
    public void SaveFailure_IsShownAndKeepsInMemoryValues()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");
        var vm = Create(new SettingsStore(Path.Combine(blocker, "settings.json")));

        vm.CloseToTray = false;

        Assert.False(vm.CloseToTray);
        Assert.True(vm.HasError);
    }

    private static Color AccentColor(ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource("Samaat.Accent", variant, out var brush));
        return ((ISolidColorBrush)brush!).Color;
    }

    [AvaloniaFact]
    public void Accent_DefaultsToLime_AndOlderFilesLoadAsLime()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "theme": "dark" }""");

        var vm = Create(Store());

        Assert.Equal(AccentChoice.Lime, vm.Accent);
        Assert.True(vm.IsLimeAccent);
        Assert.Equal(Color.Parse("#C8F169"), AccentColor(ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void ChoosingAnAccent_PersistsAndReplacesTheAccentBrushes()
    {
        var vm = Create(Store());
        try
        {
            vm.SetAccentCommand.Execute(AccentChoice.Emerald);

            Assert.Equal(AccentChoice.Emerald, Store().Load().Accent);
            Assert.True(vm.IsEmeraldAccent);
            Assert.NotEqual(Color.Parse("#C8F169"), AccentColor(ThemeVariant.Dark));
            Assert.NotEqual(Color.Parse("#4A7A00"), AccentColor(ThemeVariant.Light));
            Assert.True(Application.Current!.TryGetResource("SystemAccentColor", ThemeVariant.Dark, out var system));
            Assert.Equal(AccentColor(ThemeVariant.Dark), (Color)system!);
        }
        finally
        {
            vm.SetAccentCommand.Execute(AccentChoice.Lime);
        }

        Assert.Equal(Color.Parse("#C8F169"), AccentColor(ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void AccentChange_ReachesControlsUsingTheDynamicResource()
    {
        var vm = Create(Store());
        var box = new Border();
        box.Bind(Border.BackgroundProperty, box.GetResourceObservable("Samaat.Accent"));
        var window = new Window { Content = box };
        window.Show();
        try
        {
            var before = ((ISolidColorBrush)box.Background!).Color;

            vm.SetAccentCommand.Execute(AccentChoice.Ice);

            Assert.NotEqual(before, ((ISolidColorBrush)box.Background!).Color);
        }
        finally
        {
            vm.SetAccentCommand.Execute(AccentChoice.Lime);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReduceMotion_NullFollowsTheSystem(bool system)
    {
        var vm = Create(Store(), motion: new FakeMotion(system));

        Assert.Equal(system, vm.ReduceMotion);
        Assert.Null(vm.Current.ReduceMotion);
    }

    [AvaloniaFact]
    public void ChangingAnUnrelatedSetting_KeepsReduceMotionNullOnDisk()
    {
        var vm = Create(Store(), motion: new FakeMotion(true));

        vm.CloseToTray = false;

        Assert.True(vm.ReduceMotion);
        Assert.Null(Store().Load().ReduceMotion);
    }

    [AvaloniaFact]
    public void TogglingReduceMotion_WritesAnExplicitBool()
    {
        var vm = Create(Store(), motion: new FakeMotion(true));

        vm.ReduceMotion = false;

        Assert.False(Store().Load().ReduceMotion);
        Assert.False(Create(Store(), motion: new FakeMotion(true)).ReduceMotion);
    }
}
