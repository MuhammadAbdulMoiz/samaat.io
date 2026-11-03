namespace Samaat.Core.Audio;

/// <summary>A render endpoint that audio can be shared to.</summary>
public sealed record AudioOutput(string Id, string Name, bool IsDefault);

public enum AudioShareState { Off, Starting, Sharing, Stopped }

/// <summary>Plays the default output's audio on a second output. Implemented by the state machine in Core.</summary>
public interface IAudioShare : IAsyncDisposable
{
    bool IsSupported { get; }

    /// <summary>Active render endpoints, the default flagged.</summary>
    IReadOnlyList<AudioOutput> Outputs { get; }

    AudioShareState State { get; }

    string? TargetId { get; }

    /// <summary>Why it stopped or failed; null while fine.</summary>
    string? StatusText { get; }

    int DelayMs { get; set; }

    /// <summary>May fire on any thread.</summary>
    event EventHandler? Changed;

    Task StartAsync(string targetId, CancellationToken ct = default);

    Task StopAsync();
}

/// <summary>Used where sharing is not available on the platform.</summary>
public sealed class NoAudioShare : IAudioShare
{
    public bool IsSupported => false;

    public IReadOnlyList<AudioOutput> Outputs { get; } = [];

    public AudioShareState State => AudioShareState.Off;

    public string? TargetId => null;

    public string? StatusText => null;

    public int DelayMs { get; set; }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task StartAsync(string targetId, CancellationToken ct = default) =>
        throw new NotSupportedException("Audio sharing is not available on this platform.");

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>What the platform supplies. AudioShare is the only caller.</summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>Render/Multimedia endpoints; the default flagged.</summary>
    IReadOnlyList<AudioOutput> GetOutputs();

    /// <summary>Added, removed, state or default changed (Render and Multimedia only).</summary>
    event EventHandler? OutputsChanged;

    /// <summary>Throws on failure.</summary>
    IAudioPipe Open(string sourceId, string targetId, int delayMs);
}

/// <summary>One running capture-to-output pipe. The backend owns its <c>PlayoutBuffer</c>, since only it knows the capture rate and target channels.</summary>
public interface IAudioPipe : IDisposable
{
    int DelayMs { set; }

    /// <summary>Capture or output ended; null means a normal stop.</summary>
    event EventHandler<Exception?>? Stopped;
}
