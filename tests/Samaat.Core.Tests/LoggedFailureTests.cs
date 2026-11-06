using Samaat.Core.Commands;
using Samaat.Core.Diagnostics;
using Samaat.Core.Drivers;
using Samaat.Core.Settings;

namespace Samaat.Core.Tests;

/// <summary>Failures the code handles (and used to drop silently) now leave a line in the log.</summary>
[Collection(LogSinkCollection.Name)]
public sealed class LoggedFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-logged-" + Guid.NewGuid().ToString("N"));

    public LoggedFailureTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void UnreadableSettings_AreLogged()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        using var log = new CapturingLog();

        new SettingsStore(path).Load();

        // Other test classes may log in parallel, so look for this entry rather than counting.
        Assert.Contains(log.Entries, e => e is { Source: "Settings", Level: LogLevel.Warning } && e.Message.Contains("unreadable", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedSettingsSave_IsLogged()
    {
        var blocker = Path.Combine(_dir, "file");
        File.WriteAllText(blocker, "");
        using var log = new CapturingLog();

        Assert.False(new SettingsStore(Path.Combine(blocker, "settings.json")).Save(new AppSettings()));

        Assert.Contains(log.Entries, e => e.Source == "Settings" && e.Message.StartsWith("Settings could not be saved", StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenCatalogEntry_IsLogged()
    {
        using var log = new CapturingLog();

        var catalog = DeviceCatalog.Parse(["{ broken"]);

        Assert.Single(catalog.Errors);
        Assert.Contains(log.Entries, e => e.Source == "Catalog" && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ACommandThatThrows_IsLoggedWithItsError()
    {
        using var log = new CapturingLog();
        var error = new InvalidOperationException("channel closed");

        var result = await CommandRunner.RunAsync(_ => throw error, TimeSpan.FromSeconds(3));

        Assert.Equal(CommandStatus.Failed, result.Status);
        Assert.Contains(log.Entries, e => e.Source == "Commands" && ReferenceEquals(e.Error, error));
    }
}
