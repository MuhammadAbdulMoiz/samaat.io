using Samaat.Core.Diagnostics;

namespace Samaat.Core.Devices;

/// <summary>Merges the devices of every registered source into one list.</summary>
public sealed class DeviceRegistry : IAsyncDisposable
{
    private readonly IReadOnlyList<IDeviceSource> _sources;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<IEarbudDevice> _devices = [];

    public DeviceRegistry(IEnumerable<IDeviceSource> sources)
    {
        _sources = sources.ToList();
        foreach (var source in _sources)
        {
            source.DevicesChanged += OnSourceChanged;
        }
    }

    public IReadOnlyList<IDeviceSource> Sources => _sources;

    public IReadOnlyList<IEarbudDevice> Devices => _devices;

    /// <summary>Raised after <see cref="Devices"/> changes. May fire on any thread.</summary>
    public event EventHandler? DevicesChanged;

    /// <summary>Raised when a source fails to enumerate; the other sources still contribute.</summary>
    public event EventHandler<(IDeviceSource Source, Exception Error)>? SourceFailed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var devices = new List<IEarbudDevice>();
            foreach (var source in _sources)
            {
                try
                {
                    devices.AddRange(await source.DiscoverAsync(cancellationToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    SourceFailed?.Invoke(this, (source, ex));
                }
            }

            _devices = devices;
        }
        finally
        {
            _refreshLock.Release();
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _ = SamaatLog.Guard("Devices", "Refreshing after a source changed", async () =>
    {
        try
        {
            await RefreshAsync();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while a change notification was in flight.
        }
    });

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources)
        {
            source.DevicesChanged -= OnSourceChanged;
            await source.DisposeAsync();
        }

        _refreshLock.Dispose();
    }
}
