using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Overview;
using Samaat.App.Shell;
using Samaat.App.Tray;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;
using Samaat.Core.Settings;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class PcLinkViewModelTests
{
    private sealed class FakePcLink(Device device) : IPcLinkFeature
    {
        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public CommandResult Result { get; set; } = CommandResult.Confirmed;

        /// <summary>When set, calls wait for it, so a test can look at the busy state.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task<CommandResult> ConnectAsync(CancellationToken ct = default)
        {
            ConnectCalls++;
            return await Finish(ConnectionState.Connected);
        }

        public async Task<CommandResult> DisconnectAsync(CancellationToken ct = default)
        {
            DisconnectCalls++;
            return await Finish(ConnectionState.Disconnected);
        }

        private async Task<CommandResult> Finish(ConnectionState state)
        {
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            if (Result.Succeeded)
            {
                device.Set(state);
            }

            return Result;
        }
    }

    private sealed class Device : IEarbudDevice
    {
        public DeviceIdentity Identity { get; } = new("id", "Quantum");

        public ConnectionState Connection { get; private set; } = ConnectionState.Connected;

        public IReadOnlyCollection<IFeature> Features { get; set; } = [];

        public event EventHandler? ConnectionChanged;

        public void Set(ConnectionState state)
        {
            Connection = state;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static (DeviceViewModel Vm, FakePcLink Link, Device Device, List<string> Messages) Create(ConnectionState state, bool withLink = true)
    {
        var device = new Device();
        device.Set(state);
        var link = new FakePcLink(device);
        if (withLink)
        {
            device.Features = [link];
        }

        var messages = new List<string>();
        var vm = new DeviceViewModel(device, new(), TimeProvider.System, (_, _) => { }, onMessage: messages.Add);
        return (vm, link, device, messages);
    }

    [AvaloniaFact]
    public async Task ConnectCommand_Disconnected_CallsFeature_AndUpdates()
    {
        var (vm, link, _, _) = Create(ConnectionState.Disconnected);
        Assert.True(vm.ShowConnect);
        Assert.False(vm.ShowDisconnect);
        Assert.True(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        await vm.ConnectCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, link.ConnectCalls);
        Assert.True(vm.IsConnected);
        Assert.True(vm.ShowDisconnect);
        Assert.False(vm.ShowConnect);
        Assert.False(vm.IsLinkBusy);
    }

    [AvaloniaFact]
    public async Task DisconnectCommand_Connected_CallsFeature()
    {
        var (vm, link, _, _) = Create(ConnectionState.Connected);
        Assert.Equal("Disconnect Quantum from this computer", vm.LinkAccessibleName);

        await vm.DisconnectCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, link.DisconnectCalls);
        Assert.False(vm.IsConnected);
        Assert.Equal("Connect Quantum to this computer", vm.LinkAccessibleName);
    }

    [AvaloniaFact]
    public void NoFeature_HidesButtons_ShowsSettingsLink()
    {
        var (vm, _, _, _) = Create(ConnectionState.Connected, withLink: false);

        Assert.False(vm.HasPcLink);
        Assert.False(vm.ShowConnect);
        Assert.False(vm.ShowDisconnect);
        Assert.True(vm.ShowSettingsFallback);
        Assert.Equal("Use Windows Bluetooth settings to connect this device.", vm.SettingsFallbackText);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.DisconnectCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Failure_ShowsError()
    {
        var (vm, link, _, messages) = Create(ConnectionState.Disconnected);
        link.Result = CommandResult.Rejected("Windows refused the request (0x80070005).");

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(["Windows refused the request (0x80070005)."], messages);
        Assert.Equal("Windows refused the request (0x80070005).", vm.Connections.ErrorText);
        Assert.False(vm.IsConnected);
        Assert.False(vm.IsLinkBusy);
    }

    [AvaloniaFact]
    public async Task Busy_DisablesButtonsWhileRunning()
    {
        var (vm, link, _, _) = Create(ConnectionState.Disconnected);
        link.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.IsLinkBusy);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        link.Gate.SetResult();
        await run;
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsLinkBusy);
        Assert.True(vm.DisconnectCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ConnectWhileDisconnectRunning_Ignored()
    {
        var (vm, link, _, _) = Create(ConnectionState.Connected);
        link.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = vm.DisconnectCommand.ExecuteAsync(null);
        await vm.ConnectCommand.ExecuteAsync(null); // e.g. a tray click while busy
        await vm.DisconnectCommand.ExecuteAsync(null);

        link.Gate.SetResult();
        await run;

        Assert.Equal(0, link.ConnectCalls);
        Assert.Equal(1, link.DisconnectCalls);
    }

    [Theory]
    [InlineData(true, true, "Disconnect Quantum")]
    [InlineData(false, true, "Connect Quantum")]
    [InlineData(true, false, null)]
    public void TrayLinkItem_TextPerState(bool connected, bool hasLink, string? expected) =>
        Assert.Equal(expected, TraySummary.LinkItem(new("Quantum", "90%", connected, hasLink)));

    [AvaloniaFact]
    public async Task TrayDisconnect_Failure_ShowsShellMessage()
    {
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var main = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: settings);
        await main.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        var device = main.Devices.First();
        ((SimulatedPcLinkFeature)device.Device.GetFeature<IPcLinkFeature>()!).FailNextCommand = true;
        main.Alerts.Clear();

        await device.DisconnectCommand.ExecuteAsync(null); // what the tray item calls

        Assert.Contains(main.Alerts, a => a == "The earbuds refused the change (simulated failure).");
        await main.DisposeAsync();
    }
}
