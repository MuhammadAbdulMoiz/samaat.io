using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Dmo;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Samaat.Core.Audio;
using Samaat.Core.Diagnostics;

namespace Samaat.Platform.Windows;

/// <summary>WASAPI backend for audio sharing: loopback-captures one render endpoint and plays it on another.</summary>
public sealed class WasapiBackend : IAudioBackend
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Notifier _notifier;
    private volatile bool _disposed;

    public WasapiBackend()
    {
        _notifier = new Notifier(this);
        _enumerator.RegisterEndpointNotificationCallback(_notifier);
    }

    public event EventHandler? OutputsChanged;

    public IReadOnlyList<AudioOutput> GetOutputs()
    {
        string? defaultId = null;
        try
        {
            using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = def.ID;
        }
        catch (COMException)
        {
            // no default render endpoint
        }
        var list = new List<AudioOutput>();
        foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (d) list.Add(new AudioOutput(d.ID, d.FriendlyName, d.ID == defaultId));
        }
        return list;
    }

    public IAudioPipe Open(string sourceId, string targetId, int delayMs)
    {
        MMDevice? source = null, target = null;
        WasapiLoopbackCapture? capture = null;
        WasapiOut? output = null;
        try
        {
            source = _enumerator.GetDevice(sourceId);
            target = _enumerator.GetDevice(targetId);
            capture = new WasapiLoopbackCapture(source);
            using var client = target.AudioClient;
            var pipe = new Pipe(capture, delayMs, client.MixFormat);
            output = new WasapiOut(target, AudioClientShareMode.Shared, true, 50);
            output.Init(pipe.CreateOutputProvider());
            pipe.Attach(output);
            capture.StartRecording();
            output.Play();
            return pipe;
        }
        catch
        {
            capture?.Dispose();
            output?.Dispose();
            throw;
        }
        finally
        {
            source?.Dispose();
            target?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.UnregisterEndpointNotificationCallback(_notifier);
        _enumerator.Dispose();
    }

    private void RaiseOutputsChanged() =>
        // MMDevice calls are forbidden inside the notification callback, and the handler calls GetOutputs.
        ThreadPool.QueueUserWorkItem(_ => { if (!_disposed) OutputsChanged?.Invoke(this, EventArgs.Empty); });

    private sealed class Notifier(WasapiBackend owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.RaiseOutputsChanged();
        public void OnDeviceAdded(string pwstrDeviceId) => owner.RaiseOutputsChanged();
        public void OnDeviceRemoved(string deviceId) => owner.RaiseOutputsChanged();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) owner.RaiseOutputsChanged();
        }
    }

    private sealed class Pipe : IAudioPipe, ISampleProvider
    {
        private readonly WasapiLoopbackCapture _capture;
        private readonly PlayoutBuffer _buffer;
        private readonly SampleEncoding _encoding;
        private readonly int _bits;
        private readonly int _captureChannels;
        private readonly int _outChannels;
        private readonly int _targetRate;
        private readonly object _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private float[] _floats = new float[4096];
        private float[] _mapped = new float[4096];
        private long _lastTrace;
        private WasapiOut? _output;
        private EventHandler<Exception?>? _stopped;
        private bool _raised;
        private Exception? _error;
        private bool _disposed;

        public Pipe(WasapiLoopbackCapture capture, int delayMs, WaveFormat targetMix)
        {
            _capture = capture;
            var wf = capture.WaveFormat;
            _bits = wf.BitsPerSample;
            _captureChannels = wf.Channels;
            _encoding = wf.Encoding switch
            {
                WaveFormatEncoding.IeeeFloat => SampleEncoding.Float,
                WaveFormatEncoding.Pcm => SampleEncoding.Pcm,
                WaveFormatEncoding.Extensible when ((WaveFormatExtensible)wf).SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT => SampleEncoding.Float,
                WaveFormatEncoding.Extensible when ((WaveFormatExtensible)wf).SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM => SampleEncoding.Pcm,
                _ => throw new NotSupportedException("Unsupported capture format"),
            };
            _outChannels = Math.Min(targetMix.Channels, 2);
            _targetRate = targetMix.SampleRate;
            _buffer = new PlayoutBuffer(wf.SampleRate, _outChannels, 60) { DelayMs = delayMs };
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnCaptureStopped;
        }

        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(_capture.WaveFormat.SampleRate, _outChannels);

        public int DelayMs { set { if (!_disposed) _buffer.DelayMs = value; } }

        public event EventHandler<Exception?>? Stopped
        {
            add
            {
                bool replay;
                lock (_gate) { _stopped += value; replay = _raised; }
                if (replay && value is not null) ThreadPool.QueueUserWorkItem(_ => value(this, _error));
            }
            remove { lock (_gate) _stopped -= value; }
        }

        public IWaveProvider CreateOutputProvider() =>
            new SampleToWaveProvider(WaveFormat.SampleRate == _targetRate ? this : new WdlResamplingSampleProvider(this, _targetRate));

        public void Attach(WasapiOut output)
        {
            _output = output;
            output.PlaybackStopped += OnOutputStopped;
        }

        public int Read(float[] buffer, int offset, int count) => _buffer.Read(buffer.AsSpan(offset, count));

        private void OnData(object? sender, WaveInEventArgs e)
        {
            var samples = e.BytesRecorded / (_bits / 8);
            if (_floats.Length < samples) _floats = new float[samples];
            var n = SampleConvert.ToFloat(e.Buffer.AsSpan(0, e.BytesRecorded), _encoding, _bits, _floats);
            var mappedLen = n / _captureChannels * _outChannels;
            if (_mapped.Length < mappedLen) _mapped = new float[mappedLen];
            var m = SampleConvert.MapChannels(_floats.AsSpan(0, n), _captureChannels, _mapped, _outChannels);
            _buffer.Write(_mapped.AsSpan(0, m));

            // shortcut: Trace logs Dropped/RepeatedFrames every 60 s; upgrade to a diagnostics view if drift reports come in
            if (_clock.ElapsedMilliseconds - _lastTrace >= 60_000)
            {
                _lastTrace = _clock.ElapsedMilliseconds;
                Trace.TraceInformation($"AudioShare: dropped={_buffer.DroppedFrames} repeated={_buffer.RepeatedFrames} fill={_buffer.FillMs}ms");
            }
        }

        private void OnCaptureStopped(object? sender, StoppedEventArgs e) => Raise(e.Exception);

        private void OnOutputStopped(object? sender, StoppedEventArgs e) => Raise(e.Exception);

        private void Raise(Exception? error)
        {
            EventHandler<Exception?>? h;
            lock (_gate)
            {
                if (_raised || _disposed) return;
                _raised = true;
                _error = error;
                h = _stopped;
            }
            // Off the audio thread: the handler disposes the pipe, which joins that thread.
            if (h is not null) ThreadPool.QueueUserWorkItem(_ => h(this, error));
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _stopped = null;
            }
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnCaptureStopped;
            if (_output is { } o) o.PlaybackStopped -= OnOutputStopped;
            try { _capture.StopRecording(); } catch (Exception e) { SamaatLog.Warn("Audio share", "Stopping the capture failed", e); }
            try { _output?.Stop(); } catch (Exception e) { SamaatLog.Warn("Audio share", "Stopping the output failed", e); }
            _capture.Dispose();
            _output?.Dispose();
        }
    }
}
