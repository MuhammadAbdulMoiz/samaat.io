using Samaat.Core.Diagnostics;

namespace Samaat.Core.Tests;

/// <summary>Tests that swap the process-wide log sink run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LogSinkCollection
{
    public const string Name = "SamaatLog sink";
}

/// <summary>Records every entry written while it is installed.</summary>
public sealed class CapturingLog : ILogSink, IDisposable
{
    private readonly ILogSink _previous;
    private readonly List<(LogLevel Level, string Source, string Message, Exception? Error)> _entries = [];

    public CapturingLog()
    {
        _previous = SamaatLog.Sink;
        SamaatLog.Sink = this;
    }

    /// <summary>Other test classes run in parallel and may log too, so assertions filter by a source of their own.</summary>
    public IReadOnlyList<(LogLevel Level, string Source, string Message, Exception? Error)> From(string source) =>
        [.. Entries.Where(e => e.Source == source)];

    public IReadOnlyList<(LogLevel Level, string Source, string Message, Exception? Error)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Write(LogLevel level, string source, string message, Exception? error)
    {
        lock (_entries)
        {
            _entries.Add((level, source, message, error));
        }
    }

    public void Dispose() => SamaatLog.Sink = _previous;
}

[Collection(LogSinkCollection.Name)]
public sealed class SamaatLogTests
{
    private const string Source = nameof(SamaatLogTests);

    [Fact]
    public void Entries_GoToTheInstalledSink()
    {
        using var log = new CapturingLog();
        var error = new IOException("disk full");

        SamaatLog.Warn(Source, "Couldn't save settings", error);
        SamaatLog.Error(Source, "Listener stopped");
        SamaatLog.Info(Source, "Started");

        Assert.Equal(
            [(LogLevel.Warning, Source, "Couldn't save settings", (Exception?)error), (LogLevel.Error, Source, "Listener stopped", null), (LogLevel.Info, Source, "Started", null)],
            log.From(Source));
    }

    [Fact]
    public void ABrokenSink_NeverBreaksTheCaller()
    {
        var previous = SamaatLog.Sink;
        SamaatLog.Sink = new ThrowingSink();
        try
        {
            SamaatLog.Warn("Settings", "Couldn't save settings");
        }
        finally
        {
            SamaatLog.Sink = previous;
        }
    }

    [Fact]
    public async Task Guard_LogsAFailure_InsteadOfCrashing()
    {
        using var log = new CapturingLog();

        await SamaatLog.Guard(Source, "Copy report", () => throw new InvalidOperationException("clipboard busy"));

        var entry = Assert.Single(log.From(Source));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Copy report failed", entry.Message);
        Assert.IsType<InvalidOperationException>(entry.Error);
    }

    [Fact]
    public async Task Guard_StaysQuiet_OnSuccessAndCancellation()
    {
        using var log = new CapturingLog();

        await SamaatLog.Guard(Source, "Copy report", () => Task.CompletedTask);
        await SamaatLog.Guard(Source, "Copy report", () => throw new OperationCanceledException());

        Assert.Empty(log.From(Source));
    }

    [Fact]
    public void Format_IsOneLineWithTimeLevelSourceAndError()
    {
        var line = LogFormat.Line(new DateTimeOffset(2026, 11, 5, 9, 30, 0, 125, TimeSpan.Zero), LogLevel.Warning, "Settings",
            "Couldn't save\nsettings", new IOException("disk full"));

        Assert.Equal("2026-11-05T09:30:00.125Z WARN  Settings: Couldn't save settings | IOException: disk full", line);
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(LogLevel level, string source, string message, Exception? error) => throw new IOException("log file locked");
    }
}
