using System.Diagnostics;

namespace Samaat.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Where log entries go. A sink stamps the time and must never throw.</summary>
public interface ILogSink
{
    void Write(LogLevel level, string source, string message, Exception? error);
}

/// <summary>A sink that can hand back its newest lines, for Diagnostics → Copy report.</summary>
public interface IRecentLog
{
    IReadOnlyList<string> Tail(int lines);
}

/// <summary>
/// The process-wide log: a tiny facade so every layer can record a failure it handles, without a logging framework or a
/// logger threaded through every constructor. The app installs a file sink at start-up; until then entries go to
/// <see cref="Trace"/>, as before. Writing never throws, so logging can't turn a handled failure into a crash.
/// </summary>
public static class SamaatLog
{
    private static ILogSink _sink = TraceLogSink.Instance;

    public static ILogSink Sink
    {
        get => Volatile.Read(ref _sink);
        set => Volatile.Write(ref _sink, value ?? TraceLogSink.Instance);
    }

    public static void Info(string source, string message, Exception? error = null) => Write(LogLevel.Info, source, message, error);

    public static void Warn(string source, string message, Exception? error = null) => Write(LogLevel.Warning, source, message, error);

    public static void Error(string source, string message, Exception? error = null) => Write(LogLevel.Error, source, message, error);

    public static void Write(LogLevel level, string source, string message, Exception? error = null)
    {
        try
        {
            Sink.Write(level, source, message, error);
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Samaat log sink failed: {e.Message}");
        }
    }

    /// <summary>
    /// Runs fire-and-forget work, such as an event handler's, and logs it if it fails. The returned task never faults, so
    /// it replaces <c>async void</c>: <c>_ = SamaatLog.Guard(...)</c>. Cancellation is expected and isn't logged.
    /// </summary>
    public static async Task Guard(string source, string what, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Error(source, $"{what} failed", e);
        }
    }
}

/// <summary>One log line: UTC time to the millisecond, level, source, message, and the error's type and message.</summary>
public static class LogFormat
{
    public static string Line(DateTimeOffset time, LogLevel level, string source, string message, Exception? error)
    {
        var text = $"{time.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fff'Z'} {Label(level),-5} {source}: {Flatten(message)}";
        return error is null ? text : $"{text} | {error.GetType().Name}: {Flatten(error.Message)}";
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        _ => "ERROR",
    };

    private static string Flatten(string text) => text.ReplaceLineEndings(" ");
}

/// <summary>The default sink: <see cref="Trace"/>, which is what the code used before the log existed.</summary>
public sealed class TraceLogSink : ILogSink
{
    public static readonly TraceLogSink Instance = new();

    private TraceLogSink()
    {
    }

    public void Write(LogLevel level, string source, string message, Exception? error) =>
        Trace.WriteLine(LogFormat.Line(DateTimeOffset.UtcNow, level, source, message, error));
}
