using Samaat.Core.Settings;

namespace Samaat.Core.Tests;

public sealed class DashboardSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));

    public DashboardSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "settings.json");

    private AppSettings LoadJson(string json)
    {
        File.WriteAllText(FilePath, json);
        return new SettingsStore(FilePath).Load();
    }

    [Fact]
    public void Defaults_StudioLayoutAndConnectedDevices()
    {
        var s = new AppSettings();

        Assert.Equal(DashboardView.Studio, s.Dashboard);
        Assert.Equal(DeviceFilter.Connected, s.DeviceFilter);
        Assert.Equal(AppSettings.CurrentVersion, s.Version);
        Assert.Empty(s.LastConnected);
    }

    [Fact]
    public void Load_VersionOneFile_UsesDefaultsAndKeepsEverythingElse()
    {
        var loaded = LoadJson("""{ "theme": "light", "accent": "ice", "lowThreshold": 25 }""");

        Assert.Equal(DashboardView.Studio, loaded.Dashboard);
        Assert.Equal(DeviceFilter.Connected, loaded.DeviceFilter);
        Assert.Equal(1, loaded.Version);
        Assert.Equal(ThemeChoice.Light, loaded.Theme);
        Assert.Equal(AccentChoice.Ice, loaded.Accent);
        Assert.Equal(25, loaded.LowThreshold);
    }

    [Fact]
    public void Load_UnknownEnumValues_FallBackWithoutDiscardingTheFile()
    {
        var loaded = LoadJson("""{ "dashboard": "orbit", "deviceFilter": "nearby", "theme": "sepia", "accent": "ice", "lowThreshold": 30 }""");
        var store = new SettingsStore(FilePath);
        store.Load();

        Assert.Equal(DashboardView.Studio, loaded.Dashboard);
        Assert.Equal(DeviceFilter.Connected, loaded.DeviceFilter);
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal(AccentChoice.Ice, loaded.Accent);
        Assert.Equal(30, loaded.LowThreshold);
        Assert.Null(store.LastError);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_OutOfRangeNumbers_Normalize()
    {
        var loaded = LoadJson("""{ "dashboard": 7, "deviceFilter": -1, "accent": 2 }""");

        Assert.Equal(DashboardView.Studio, loaded.Dashboard);
        Assert.Equal(DeviceFilter.Connected, loaded.DeviceFilter);
        Assert.Equal(AccentChoice.Ice, loaded.Accent);
    }

    [Theory]
    [InlineData(DashboardView.Studio, DeviceFilter.Connected)]
    [InlineData(DashboardView.Focus, DeviceFilter.Recent)]
    [InlineData(DashboardView.Glance, DeviceFilter.Paired)]
    public void LayoutAndFilter_RoundTrip(DashboardView view, DeviceFilter filter)
    {
        var store = new SettingsStore(FilePath);
        var seen = new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);
        store.Save(new AppSettings
        {
            Dashboard = view,
            DeviceFilter = filter,
            LastConnected = new Dictionary<string, DateTimeOffset> { ["buds"] = seen },
        });

        var loaded = store.Load();

        Assert.Equal(view, loaded.Dashboard);
        Assert.Equal(filter, loaded.DeviceFilter);
        Assert.Equal(seen, loaded.LastConnected["buds"]);
        Assert.Contains("\"dashboard\": \"" + char.ToLowerInvariant(view.ToString()[0]) + view.ToString()[1..] + "\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Save_AfterVersionOne_WritesCurrentVersion()
    {
        var store = new SettingsStore(FilePath);
        var loaded = LoadJson("""{ "theme": "light" }""");

        store.Save(loaded.Upgraded());

        Assert.Equal(AppSettings.CurrentVersion, store.Load().Version);
    }

    [Fact]
    public void Save_FileFromNewerBuild_KeepsItsVersion()
    {
        var store = new SettingsStore(FilePath);
        var loaded = LoadJson("""{ "version": 9 }""");

        store.Save(loaded.Upgraded());

        Assert.Equal(9, store.Load().Version);
    }

    [Fact]
    public void Load_NullLastConnected_LoadsAsEmpty() =>
        Assert.Empty(LoadJson("""{ "lastConnected": null }""").LastConnected);

    [Theory]
    [InlineData(DeviceFilter.Paired, false, null, true)]
    [InlineData(DeviceFilter.Connected, true, null, true)]
    [InlineData(DeviceFilter.Connected, false, 0.0, false)]
    [InlineData(DeviceFilter.Recent, true, null, true)]
    [InlineData(DeviceFilter.Recent, false, 2.0, true)]
    [InlineData(DeviceFilter.Recent, false, 6.9, true)]
    [InlineData(DeviceFilter.Recent, false, 7.1, false)]
    [InlineData(DeviceFilter.Recent, false, null, false)]
    public void DeviceFilter_Includes(DeviceFilter filter, bool connected, double? daysSinceConnected, bool expected)
    {
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset? last = daysSinceConnected is { } d ? now.AddDays(-d) : null;

        Assert.Equal(expected, DeviceFilters.Includes(filter, connected, last, now));
    }
}
