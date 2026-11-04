namespace Samaat.Core.Audio;

/// <summary>
/// Jitter buffer between a capture thread (<see cref="Write"/>) and an output thread (<see cref="Read"/>).
/// Holds a fill target of base latency plus <see cref="DelayMs"/>, nudging the fill back by dropping or repeating frames.
/// </summary>
public sealed class PlayoutBuffer(int sampleRate, int channels, int baseLatencyMs = 60)
{
    private const int CapacitySeconds = 2;
    private const int ToleranceMs = 20;
    private const int CorrectionInterval = 1000;

    private readonly object _lock = new();
    private readonly float[] _data = new float[sampleRate * CapacitySeconds * channels];
    private readonly int _capacityFrames = sampleRate * CapacitySeconds;
    private int _head;
    private int _frames;
    private bool _priming = true;
    private int _sinceCorrection;
    private int _delayMs;
    private long _dropped;
    private long _repeated;

    public int DelayMs
    {
        get { lock (_lock) return _delayMs; }
        set
        {
            lock (_lock)
            {
                var clamped = Math.Clamp(value, 0, 500);
                if (clamped == _delayMs) return;
                _delayMs = clamped;
                _priming = true;
            }
        }
    }

    public int FillMs { get { lock (_lock) return (int)((long)_frames * 1000 / sampleRate); } }
    public long DroppedFrames { get { lock (_lock) return _dropped; } }
    public long RepeatedFrames { get { lock (_lock) return _repeated; } }

    private int TargetFrames => (baseLatencyMs + _delayMs) * sampleRate / 1000;
    private int ToleranceFrames => ToleranceMs * sampleRate / 1000;

    public void Write(ReadOnlySpan<float> interleaved)
    {
        lock (_lock)
        {
            var frames = interleaved.Length / channels;
            if (frames > _capacityFrames)
            {
                _dropped += frames - _capacityFrames;
                interleaved = interleaved[((frames - _capacityFrames) * channels)..];
                frames = _capacityFrames;
            }
            var overflow = _frames + frames - _capacityFrames;
            if (overflow > 0)
            {
                _head = (_head + overflow) % _capacityFrames;
                _frames -= overflow;
                _dropped += overflow;
            }
            var tail = (_head + _frames) % _capacityFrames;
            for (var f = 0; f < frames; f++)
            {
                interleaved.Slice(f * channels, channels).CopyTo(_data.AsSpan(tail * channels, channels));
                if (++tail == _capacityFrames) tail = 0;
            }
            _frames += frames;
        }
    }

    public int Read(Span<float> interleaved)
    {
        lock (_lock)
        {
            var frames = interleaved.Length / channels;
            for (var f = 0; f < frames; f++)
            {
                var o = interleaved.Slice(f * channels, channels);
                if (_priming && _frames >= TargetFrames && _frames > 0) _priming = false;
                if (_priming) { o.Clear(); continue; }
                if (_frames == 0) { o.Clear(); _priming = true; continue; }

                _data.AsSpan(_head * channels, channels).CopyTo(o);
                var consume = 1;
                // shortcut: drop/repeat single frames, can click on pure tones; upgrade to adaptive resampling if users hear it
                if (++_sinceCorrection >= CorrectionInterval)
                {
                    _sinceCorrection = 0;
                    if (_frames > TargetFrames + ToleranceFrames && _frames > 1) { consume = 2; _dropped++; }
                    else if (_frames < TargetFrames - ToleranceFrames) { consume = 0; _repeated++; }
                }
                _head = (_head + consume) % _capacityFrames;
                _frames -= consume;
            }
            if (frames * channels < interleaved.Length) interleaved[(frames * channels)..].Clear();
            return interleaved.Length;
        }
    }
}
