using System.Diagnostics;

namespace Samaat.Core.Audio;

/// <summary>
/// Shares the default output's audio to a second output. All state changes run under one semaphore;
/// backend and pipe events only queue work on it, and a pipe's Stopped is ignored once its generation is retired.
/// </summary>
public sealed class AudioShare : IAudioShare
{
    private readonly IAudioBackend _backend;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Written only under _gate, read lock-free by the UI.
    private volatile IReadOnlyList<AudioOutput> _outputs;
    private volatile AudioShareState _state;
    private volatile string? _status;
    private volatile string? _targetId;
    private string? _targetName;
    private string? _sourceId;
    private volatile IAudioPipe? _pipe;
    private EventHandler<Exception?>? _pipeHandler;
    private int _generation;
    private bool _disposed;
    private volatile int _delayMs;

    public AudioShare(IAudioBackend backend)
    {
        _backend = backend;
        _outputs = backend.GetOutputs();
        backend.OutputsChanged += OnOutputsChanged;
    }

    public bool IsSupported => true;

    public IReadOnlyList<AudioOutput> Outputs => _outputs;

    public AudioShareState State => _state;

    public string? TargetId => _targetId;

    public string? StatusText => _status;

    public int DelayMs
    {
        get => _delayMs;
        set
        {
            _delayMs = value;
            // shortcut: pushed without the gate so the UI never waits on Open; a retiring pipe must tolerate a late set.
            try
            {
                _pipe?.DelayMs = value;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"AudioShare: pushing delay failed: {ex.Message}");
            }
        }
    }

    public event EventHandler? Changed;

    public async Task StartAsync(string targetId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            RetirePipe();
            _targetId = targetId;
            _status = null;
            var target = _outputs.FirstOrDefault(o => o.Id == targetId);
            _targetName = target?.Name;
            if (target is null)
            {
                Stop(AudioShareState.Stopped, "Couldn't start: output not found");
            }
            else if (target.IsDefault)
            {
                Stop(AudioShareState.Stopped, "Couldn't start: pick an output other than the default");
            }
            else
            {
                await OpenAsync(target.Id).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            RetirePipe();
            Stop(AudioShareState.Off, null);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _backend.OutputsChanged -= OnOutputsChanged;
            RetirePipe();
            _backend.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    // Callers hold _gate.
    private void Stop(AudioShareState state, string? status)
    {
        _state = state;
        _status = status;
    }

    // Opens from the current default to the target, under _gate. COM needs the MTA, and the caller may be the UI thread.
    private async Task OpenAsync(string targetId)
    {
        var source = _outputs.FirstOrDefault(o => o.IsDefault);
        if (source is null)
        {
            Stop(AudioShareState.Stopped, "Couldn't start: no default output");
            return;
        }

        _state = AudioShareState.Starting;
        try
        {
            var delay = DelayMs;
            var pipe = await Task.Run(() => _backend.Open(source.Id, targetId, delay)).ConfigureAwait(false);
            var generation = ++_generation;
            _pipeHandler = (_, ex) => _ = OnPipeStoppedAsync(generation, ex);
            pipe.Stopped += _pipeHandler;
            _sourceId = source.Id;
            _pipe = pipe;
            // Assign first, then compare: a DelayMs set during Open either sees the pipe or is seen here.
            if (_delayMs != delay)
            {
                pipe.DelayMs = _delayMs;
            }

            _state = AudioShareState.Sharing;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"AudioShare: open failed: {ex}");
            Stop(AudioShareState.Stopped, $"Couldn't start: {ex.Message}");
        }
    }

    // Generation first and unsubscribe before Dispose, so a Stopped raised by the disposal itself is stale.
    private void RetirePipe()
    {
        var pipe = _pipe;
        if (pipe is null)
        {
            return;
        }

        _generation++;
        _pipe = null;
        pipe.Stopped -= _pipeHandler;
        _pipeHandler = null;
        _sourceId = null;
        try
        {
            pipe.Dispose();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"AudioShare: pipe dispose failed: {ex.Message}");
        }
    }

    private void OnOutputsChanged(object? sender, EventArgs e) => _ = OnOutputsChangedAsync();

    private async Task OnOutputsChangedAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                _outputs = _backend.GetOutputs();
                if (_pipe is not null)
                {
                    await ReconcileAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }

            RaiseChanged();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"AudioShare: outputs change failed: {ex}");
        }
    }

    // Sharing and the outputs just changed, under _gate.
    private async Task ReconcileAsync()
    {
        var target = _outputs.FirstOrDefault(o => o.Id == _targetId);
        var name = _targetName ?? _targetId;
        if (target is null)
        {
            RetirePipe();
            Stop(AudioShareState.Stopped, $"Stopped: {name} disconnected");
        }
        else if (target.IsDefault)
        {
            RetirePipe();
            Stop(AudioShareState.Stopped, $"Stopped: {name} became the default output");
        }
        else if (!_outputs.Any(o => o.IsDefault))
        {
            RetirePipe();
            Stop(AudioShareState.Stopped, "Stopped: no default output");
        }
        else if (_outputs.First(o => o.IsDefault).Id != _sourceId)
        {
            RetirePipe();
            await OpenAsync(target.Id).ConfigureAwait(false);
        }
    }

    private async Task OnPipeStoppedAsync(int generation, Exception? error)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || generation != _generation)
                {
                    return;
                }

                RetirePipe();
                Stop(AudioShareState.Stopped, error is null ? "Stopped." : $"Stopped: {error.Message}");
            }
            finally
            {
                _gate.Release();
            }

            RaiseChanged();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"AudioShare: pipe stop failed: {ex}");
        }
    }

    // Always called after releasing _gate: a handler may call StartAsync/StopAsync.
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"AudioShare: Changed handler failed: {ex}");
        }
    }
}
