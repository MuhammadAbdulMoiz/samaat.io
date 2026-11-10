using Samaat.Core.Battery;
using Samaat.Core.Settings;

namespace Samaat.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string InDir(string name = "settings.json") => Path.Combine(_dir, name);

    [Fact]
    public async Task Save_ManyInParallel_AllSucceedAndLeaveNoTempFiles()
    {
        var store = new SettingsStore(InDir());

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i =>
            Task.Run(() => store.Save(new AppSettings { ReduceMotion = i % 2 == 0 }))));

        Assert.All(results, Assert.True);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        var reader = new SettingsStore(InDir());
        reader.Load();
        Assert.Null(reader.LastError);
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var s = new AppSettings();

        Assert.Equal(20, s.LowThreshold);
        Assert.Equal(10, s.CriticalThreshold);
        Assert.False(s.IncludeCase);
        Assert.True(s.SystemNotifications);
        Assert.True(s.CloseToTray);
        Assert.False(s.StartWithWindows);
        Assert.Equal(AccentChoice.Lime, s.Accent);
        Assert.Null(s.ReduceMotion);
    }

    [Fact]
    public void Load_OlderFileWithoutAccentOrMotion_UsesLimeAndFollowsTheSystem()
    {
        File.WriteAllText(InDir(), """{ "theme": "light", "lowThreshold": 25 }""");

        var loaded = new SettingsStore(InDir()).Load();

        Assert.Equal(AccentChoice.Lime, loaded.Accent);
        Assert.Null(loaded.ReduceMotion);
        Assert.Equal(25, loaded.LowThreshold);
    }

    [Fact]
    public void AccentAndReduceMotion_RoundTrip()
    {
        var store = new SettingsStore(InDir());

        Assert.True(store.Save(new AppSettings { Accent = AccentChoice.Emerald, ReduceMotion = true }));

        var loaded = new SettingsStore(InDir()).Load();
        Assert.Equal(AccentChoice.Emerald, loaded.Accent);
        Assert.True(loaded.ReduceMotion);
        Assert.Contains("\"accent\": \"emerald\"", File.ReadAllText(InDir()));
    }

    [Fact]
    public void NoMotionPreference_NeverReducesMotion() =>
        Assert.False(new Samaat.Core.Platform.NoMotionPreference().ReduceMotion);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(501, 500)]
    [InlineData(250, 250)]
    public void Settings_ShareDelays_ClampedOnNormalize(int delay, int expected) =>
        Assert.Equal(expected, new AppSettings { ShareDelays = new Dictionary<string, int> { ["tv"] = delay } }.Normalize().ShareDelays["tv"]);

    [Fact]
    public void Settings_NullShareDelays_LoadsAsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(InDir(), "{\"lowThreshold\":30,\"shareDelays\":null}");

        var loaded = new SettingsStore(InDir()).Load();

        Assert.Equal(30, loaded.LowThreshold);
        Assert.Empty(loaded.ShareDelays);
    }

    [Fact]
    public void Settings_ShareDelays_RoundTrip()
    {
        var store = new SettingsStore(InDir());
        var saved = new AppSettings { ShareDelays = new Dictionary<string, int> { ["speaker-1"] = 120, ["hp"] = 0 } };

        Assert.True(store.Save(saved));

        var loaded = new SettingsStore(InDir()).Load();
        Assert.Equal(120, loaded.ShareDelays["speaker-1"]);
        Assert.Equal(0, loaded.ShareDelays["hp"]);
        Assert.Equal(2, loaded.ShareDelays.Count);
    }

    [Theory]
    [InlineData(2, 1, 5, 1)]
    [InlineData(99, 99, 50, 49)]
    [InlineData(20, 20, 20, 19)]
    [InlineData(20, 0, 20, 1)]
    [InlineData(5, 4, 5, 4)]
    [InlineData(5, 9, 5, 4)]
    public void Normalize_ClampsThresholds(int low, int critical, int expectedLow, int expectedCritical)
    {
        var n = new AppSettings { LowThreshold = low, CriticalThreshold = critical }.Normalize();

        Assert.Equal(expectedLow, n.LowThreshold);
        Assert.Equal(expectedCritical, n.CriticalThreshold);
    }

    [Fact]
    public void RoundTrips()
    {
        var store = new SettingsStore(InDir());
        var saved = new AppSettings
        {
            Theme = ThemeChoice.Light, LowThreshold = 30, CriticalThreshold = 12, IncludeCase = true,
            SystemNotifications = false, CloseToTray = false, StartWithWindows = true,
        };

        Assert.True(store.Save(saved));

        // Normalize copies the dictionaries, so reference equality on the dictionary would fail; compare the rest by value.
        Assert.Equal(saved, new SettingsStore(InDir()).Load() with { ShareDelays = saved.ShareDelays, ArtworkChoices = saved.ArtworkChoices, LastConnected = saved.LastConnected });
        Assert.Contains("\"theme\": \"light\"", File.ReadAllText(InDir()));
        Assert.Null(store.LastError);
        Assert.False(File.Exists(InDir() + ".tmp"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithoutError()
    {
        var store = new SettingsStore(InDir());

        Assert.Equal(new AppSettings(), store.Load());
        Assert.Null(store.LastError);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndKeepsCopy()
    {
        File.WriteAllText(InDir(), "{ not json");
        var store = new SettingsStore(InDir());

        Assert.Equal(new AppSettings(), store.Load());

        Assert.False(File.Exists(InDir()));
        Assert.Equal("{ not json", File.ReadAllText(InDir() + ".bad"));
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void Load_CorruptFile_OverwritesOlderBackup()
    {
        File.WriteAllText(InDir() + ".bad", "old");
        File.WriteAllText(InDir(), "[]");
        var store = new SettingsStore(InDir());

        store.Load();

        Assert.Equal("[]", File.ReadAllText(InDir() + ".bad"));
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        File.WriteAllText(InDir(), """{ "lowThreshold": 500, "criticalThreshold": 400 }""");

        var loaded = new SettingsStore(InDir()).Load();

        Assert.Equal(50, loaded.LowThreshold);
        Assert.Equal(49, loaded.CriticalThreshold);
    }

    [Fact]
    public void Load_DirectoryInPlaceOfFile_ReturnsDefaultsAndReports()
    {
        Directory.CreateDirectory(InDir());
        var store = new SettingsStore(InDir());

        Assert.Equal(new AppSettings(), store.Load());
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void Save_UnwritableLocation_ReturnsFalseAndReports()
    {
        File.WriteAllText(InDir("blocker"), "");
        var store = new SettingsStore(Path.Combine(InDir("blocker"), "sub", "settings.json"));

        Assert.False(store.Save(new AppSettings()));
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var store = new SettingsStore(Path.Combine(_dir, "nested", "settings.json"));

        Assert.True(store.Save(new AppSettings { LowThreshold = 25 }));
        Assert.Equal(25, store.Load().LowThreshold);
    }

    [Fact]
    public void DefaultPath_IsUnderApplicationData() =>
        Assert.EndsWith(Path.Combine("Samaat", "settings.json"), SettingsStore.DefaultPath);

    [Fact]
    public void Detector_PolicyChangeAppliesToNextEvaluate()
    {
        var detector = new LowBatteryDetector();
        var state = BatteryState.Empty.With(new BatteryReading(BatteryComponent.Combined, 30, DateTimeOffset.UnixEpoch));
        Assert.Empty(detector.Evaluate("d", state));

        detector.Policy = detector.Policy with { LowThreshold = 40 };

        var alert = Assert.Single(detector.Evaluate("d", state));
        Assert.Equal(BatteryAlertLevel.Low, alert.Level);
    }
}
