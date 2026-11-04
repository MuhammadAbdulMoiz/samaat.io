using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Shell;
using Samaat.Core.Devices;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public class SelectionTests
{
    private sealed class PlainDevice(string id) : IEarbudDevice
    {
        public DeviceIdentity Identity { get; } = new(id, id);

        public ConnectionState Connection => ConnectionState.Connected;

        public IReadOnlyCollection<IFeature> Features { get; } = [];

        public event EventHandler? ConnectionChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeSource : IDeviceSource
    {
        public List<IEarbudDevice> Devices { get; set; } = [];

        public string Name => "Fake";

        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IEarbudDevice>>(Devices.ToList());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task SyncDevices_RebuiltSelectedDevice_KeepsSelectionById()
    {
        var source = new FakeSource { Devices = [new PlainDevice("a"), new PlainDevice("b"), new PlainDevice("c")] };
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: store, sources: [source]);

        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        vm.SelectedDevice = vm.Devices[1];

        source.Devices = [source.Devices[0], new PlainDevice("b"), source.Devices[2]];
        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("b", vm.SelectedDevice!.Device.Identity.Id);
        Assert.Same(source.Devices[1], vm.SelectedDevice.Device);
        await vm.DisposeAsync();
    }
}
