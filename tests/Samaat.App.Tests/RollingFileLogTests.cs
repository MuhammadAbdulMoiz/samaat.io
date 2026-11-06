using Microsoft.Extensions.Time.Testing;
using Samaat.App.Diagnostics;
using Samaat.Core.Diagnostics;

namespace Samaat.App.Tests;

/// <summary>The app's log file: next to the settings, one line per entry, capped in size.</summary>
public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-log-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Lives_InALogsFolderBesideTheSettings()
    {
        var log = RollingFileLog.BesideSettings(Path.Combine(_dir, "settings.json"), _time);

        Assert.Equal(Path.Combine(_dir, "logs", "samaat.log"), log.Path);
    }

    [Fact]
    public void Writes_OneFormattedLinePerEntry()
    {
        var log = new RollingFileLog(Path.Combine(_dir, "samaat.log"), _time);

        log.Write(LogLevel.Warning, "Settings", "Couldn't save settings", new IOException("disk full"));
        _time.Advance(TimeSpan.FromSeconds(1));
        log.Write(LogLevel.Info, "App", "Started", null);

        Assert.Equal(
            ["2026-11-05T09:00:00.000Z WARN  Settings: Couldn't save settings | IOException: disk full", "2026-11-05T09:00:01.000Z INFO  App: Started"],
            File.ReadAllLines(log.Path));
    }

    [Fact]
    public void RollsOver_SoTheFilesStayUnderTheCap()
    {
        var log = new RollingFileLog(Path.Combine(_dir, "samaat.log"), _time, maxBytes: 1_000);

        for (var i = 0; i < 200; i++)
        {
            log.Write(LogLevel.Info, "Test", $"entry {i:D3}", null);
        }

        Assert.True(new FileInfo(log.Path).Length <= 1_000);
        Assert.True(new FileInfo(log.Path + ".1").Length <= 1_000);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
        Assert.EndsWith("entry 199", File.ReadAllLines(log.Path)[^1]);
    }

    [Fact]
    public void Tail_ReturnsTheNewestLines_AcrossARollover()
    {
        var log = new RollingFileLog(Path.Combine(_dir, "samaat.log"), _time, maxBytes: 300);
        for (var i = 0; i < 20; i++)
        {
            log.Write(LogLevel.Info, "Test", $"entry {i:D2}", null);
        }

        var tail = log.Tail(6);

        Assert.Equal(Enumerable.Range(14, 6).Select(i => $"entry {i:D2}"), tail.Select(l => l[^8..]));
    }

    [Fact]
    public void Tail_IsEmpty_BeforeAnythingIsWritten() =>
        Assert.Empty(new RollingFileLog(Path.Combine(_dir, "samaat.log"), _time).Tail(10));

    [Fact]
    public void AFolderThatCantBeWritten_NeverThrows()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(blocker, "");
        var log = new RollingFileLog(Path.Combine(blocker, "samaat.log"), _time);

        log.Write(LogLevel.Error, "Test", "nowhere to go", null);

        Assert.Empty(log.Tail(5));
    }
}
