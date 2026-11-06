using System.Text;
using Samaat.Core.Diagnostics;

namespace Samaat.App.Diagnostics;

/// <summary>
/// The app's log file, <c>logs/samaat.log</c> beside the settings. When it passes <c>maxBytes</c> it becomes
/// <c>samaat.log.1</c> (replacing the previous one), so the two files never hold more than twice the cap. Thread-safe;
/// a file that can't be written is skipped silently, since the log must never break the app.
/// </summary>
public sealed class RollingFileLog : ILogSink, IRecentLog
{
    public const long DefaultMaxBytes = 256 * 1024;

    private readonly TimeProvider _time;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public RollingFileLog(string path, TimeProvider? time = null, long maxBytes = DefaultMaxBytes)
    {
        Path = path;
        _time = time ?? TimeProvider.System;
        _maxBytes = maxBytes;
    }

    public string Path { get; }

    private string Previous => Path + ".1";

    public static RollingFileLog BesideSettings(string settingsPath, TimeProvider? time = null) =>
        new(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(settingsPath) ?? ".", "logs", "samaat.log"), time);

    public void Write(LogLevel level, string source, string message, Exception? error)
    {
        var line = LogFormat.Line(_time.GetUtcNow(), level, source, message, error) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(line);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");
                var current = new FileInfo(Path);
                if (current.Exists && current.Length + bytes > _maxBytes)
                {
                    File.Move(Path, Previous, overwrite: true);
                }

                File.AppendAllText(Path, line, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nowhere to write: the entry is dropped rather than failing the code that logged it.
            }
        }
    }

    public IReadOnlyList<string> Tail(int lines)
    {
        lock (_gate)
        {
            var newest = Read(Path);
            if (newest.Length >= lines)
            {
                return newest[^lines..];
            }

            var older = Read(Previous);
            return [.. older.TakeLast(lines - newest.Length), .. newest];
        }
    }

    private static string[] Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
