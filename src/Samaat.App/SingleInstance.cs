namespace Samaat.App;

/// <summary>Keeps one app per user session: a second launch signals the first (via a named event) and exits.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly EventWaitHandle? _handle;
    private readonly Thread? _listener;
    private volatile bool _disposed;

    private SingleInstance(EventWaitHandle? handle, Action onSecondLaunch)
    {
        _handle = handle;
        if (handle is null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (_disposed)
                {
                    return;
                }

                onSecondLaunch();
            }
        })
        { IsBackground = true, Name = "Samaat.SingleInstance" };
        _listener.Start();
    }

    /// <returns>The guard to keep alive, or null when another instance is running (it has been signalled).</returns>
    public static SingleInstance? TryAcquire(string name, Action onSecondLaunch)
    {
        EventWaitHandle handle;
        bool createdNew;
        try
        {
            handle = new EventWaitHandle(false, EventResetMode.AutoReset, name, out createdNew);
        }
        catch (PlatformNotSupportedException)
        {
            return new SingleInstance(null, onSecondLaunch); // named events need Windows: never block a launch
        }

        if (createdNew)
        {
            return new SingleInstance(handle, onSecondLaunch);
        }

        handle.Set();
        handle.Dispose();
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        _handle?.Set();
        _listener?.Join(TimeSpan.FromSeconds(1)); // the wait keeps the OS handle open until it returns
        _handle?.Dispose();
    }
}
