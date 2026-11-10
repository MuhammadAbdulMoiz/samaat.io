namespace Samaat.Handoff.Tests;

/// <summary>A stream whose writes can be parked once armed, and which honours the caller's token while parked (like a socket cancelled mid-write).</summary>
internal sealed class GatedStream(Stream inner) : Stream
{
    private TaskCompletionSource? _gate;

    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The next write blocks until <see cref="Release"/>.</summary>
    public void Arm() => _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _gate?.TrySetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_gate is { } gate)
        {
            WriteStarted.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
        }

        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
