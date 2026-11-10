using Avalonia.Headless.XUnit;
using Samaat.App.Shell;
using Samaat.Core.Devices;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public sealed class ShutdownTests
{
    private sealed class CountingSource : IDeviceSource
    {
        public int Disposed;

        public string Name => "Counting";

        public event EventHandler? DevicesChanged { add { } remove { } }

        public Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IEarbudDevice>>([]);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposed);
            return ValueTask.CompletedTask;
        }
    }

    private static MainWindowViewModel Create(CountingSource source, Func<Samaat.Core.Audio.IAudioShare> audio) =>
        new(new AppOptions(Simulate: true, IncludeNonAudio: false),
            settings: new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json")),
            audioShareFactory: audio, sources: [source]);

    [AvaloniaFact]
    public void Shutdown_DisposesRegistryAndAudioShare_Once()
    {
        var source = new CountingSource();
        var shares = new List<FakeAudioShare>();
        var vm = Create(source, () =>
        {
            var share = new FakeAudioShare();
            shares.Add(share);
            return share;
        });

        vm.Shutdown(TimeSpan.FromSeconds(3));
        vm.Shutdown(TimeSpan.FromSeconds(3));

        Assert.Equal(1, source.Disposed);
        Assert.Equal(1, shares[0].DisposeCalls);
    }

    [AvaloniaFact]
    public async Task DisposeAsync_AfterShutdown_IsANoOp()
    {
        var source = new CountingSource();
        var vm = Create(source, () => new FakeAudioShare());

        vm.Shutdown(TimeSpan.FromSeconds(3));
        await vm.DisposeAsync();

        Assert.Equal(1, source.Disposed);
    }

    [AvaloniaFact]
    public void Shutdown_ReturnsWithinTheTimeout_WhenADisposalHangs()
    {
        var vm = Create(new CountingSource(), () => new FakeAudioShare { StopGate = new TaskCompletionSource().Task });
        var watch = System.Diagnostics.Stopwatch.StartNew();

        vm.Shutdown(TimeSpan.FromMilliseconds(200));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [AvaloniaFact]
    public void Shutdown_DoesNotThrow_AndStillReleasesDevices_WhenADisposalThrows()
    {
        var source = new CountingSource();
        var vm = Create(source, () => new FakeAudioShare { ThrowOnDispose = true });

        vm.Shutdown(TimeSpan.FromSeconds(3));

        Assert.Equal(1, source.Disposed);
    }
}
