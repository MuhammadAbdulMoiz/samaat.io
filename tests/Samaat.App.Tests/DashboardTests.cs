using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using Samaat.App.Shell;
using Samaat.Core.Devices;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

/// <summary>The layouts (Studio, Focus, Glance) and the device filter (Connected, Recent, All paired).</summary>
public class DashboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class SwitchableDevice(string id, bool connected) : IEarbudDevice
    {
        private ConnectionState _connection = connected ? ConnectionState.Connected : ConnectionState.Disconnected;

        public DeviceIdentity Identity { get; } = new(id, id);

        public ConnectionState Connection => _connection;

        public IReadOnlyCollection<IFeature> Features { get; } = [];

        public event EventHandler? ConnectionChanged;

        public void SetConnected(bool value)
        {
            _connection = value ? ConnectionState.Connected : ConnectionState.Disconnected;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeSource(params IEarbudDevice[] devices) : IDeviceSource
    {
        public string Name => "Fake";

        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<IEarbudDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IEarbudDevice>>(devices);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string NewSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json");

    private static async Task<MainWindowViewModel> CreateAsync(string settingsPath, params IEarbudDevice[] devices)
    {
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), time: new FakeTimeProvider(Now),
            settings: new SettingsStore(settingsPath), sources: [new FakeSource(devices)]);
        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static Task<MainWindowViewModel> CreateAsync(params IEarbudDevice[] devices) => CreateAsync(NewSettingsPath(), devices);

    private static string[] Listed(MainWindowViewModel vm) => vm.VisibleDevices.Select(d => d.Name).ToArray();

    [AvaloniaFact]
    public async Task ByDefault_OnlyConnectedDevicesAreListed()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true), new SwitchableDevice("b", false), new SwitchableDevice("c", true));

        Assert.Equal(3, vm.Devices.Count);
        Assert.Equal(["a", "c"], Listed(vm));
        Assert.Equal("Connected devices", vm.DeviceListTitle);
        Assert.Equal("a", vm.SelectedDevice!.Name);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task AllPaired_ListsEveryDevice()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true), new SwitchableDevice("b", false));

        vm.SetDeviceFilterCommand.Execute(DeviceFilter.Paired);

        Assert.Equal(["a", "b"], Listed(vm));
        Assert.Equal("Paired devices", vm.DeviceListTitle);
        Assert.Equal("Show devices: All paired", vm.DeviceFilterButtonName);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Recent_ListsDevicesConnectedInTheLastWeek()
    {
        var path = NewSettingsPath();
        new SettingsStore(path).Save(new AppSettings
        {
            DeviceFilter = DeviceFilter.Recent,
            LastConnected = new Dictionary<string, DateTimeOffset> { ["yesterday"] = Now.AddDays(-1), ["last month"] = Now.AddDays(-30) },
        });

        var vm = await CreateAsync(path, new SwitchableDevice("now", true), new SwitchableDevice("yesterday", false),
            new SwitchableDevice("last month", false), new SwitchableDevice("never", false));

        Assert.Equal(["now", "yesterday"], Listed(vm));
        Assert.Equal("Recent devices", vm.DeviceListTitle);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task NothingConnected_ShowsTheFilterEmptyStateUntilTheFilterWidens()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", false), new SwitchableDevice("b", false));

        Assert.Empty(vm.VisibleDevices);
        Assert.Null(vm.SelectedDevice);
        Assert.True(vm.ShowFilterEmpty);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.ShowStudio);
        Assert.Equal("2 paired devices are hidden. Connect your earbuds in Windows, or show all paired devices.", vm.FilterEmptyDetail);

        vm.SetDeviceFilterCommand.Execute(DeviceFilter.Paired);

        Assert.False(vm.ShowFilterEmpty);
        Assert.Equal("a", vm.SelectedDevice!.Name);
        Assert.True(vm.ShowStudio);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ADeviceThatConnects_IsListedAndRemembered()
    {
        var path = NewSettingsPath();
        var later = new SwitchableDevice("b", false);
        var vm = await CreateAsync(path, new SwitchableDevice("a", true), later);

        later.SetConnected(true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["a", "b"], Listed(vm));
        Assert.Equal(Now, new SettingsStore(path).Load().LastConnected["b"]);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task TheSelectedDevice_StaysListedAfterDisconnecting_UntilTheSelectionMoves()
    {
        var a = new SwitchableDevice("a", true);
        var vm = await CreateAsync(a, new SwitchableDevice("b", true));

        a.SetConnected(false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["a", "b"], Listed(vm));
        Assert.Equal("a", vm.SelectedDevice!.Name);

        vm.SelectDeviceCommand.Execute(vm.VisibleDevices[1]);

        Assert.Equal(["b"], Listed(vm));
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task OtherDevicesDisconnecting_LeaveTheList()
    {
        var b = new SwitchableDevice("b", true);
        var vm = await CreateAsync(new SwitchableDevice("a", true), b);

        b.SetConnected(false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["a"], Listed(vm));
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Studio_IsTheDefaultLayout()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true));

        Assert.Equal(DashboardView.Studio, vm.Dashboard);
        Assert.True(vm.ShowStudio);
        Assert.False(vm.ShowFocus);
        Assert.False(vm.ShowGlance);
        Assert.Equal("Layout: Studio", vm.LayoutButtonName);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task SwitchingLayout_KeepsTheDeviceAndTab_AndPersists()
    {
        var path = NewSettingsPath();
        var vm = await CreateAsync(path, new SwitchableDevice("a", true), new SwitchableDevice("b", true));
        vm.SelectDeviceCommand.Execute(vm.Devices[1]);
        vm.NavigateCommand.Execute(AppSection.Sound);

        vm.SetDashboardCommand.Execute(DashboardView.Focus);

        Assert.Equal("b", vm.SelectedDevice!.Name);
        Assert.Equal(AppSection.Sound, vm.Section);
        Assert.True(vm.ShowFocus);
        Assert.False(vm.ShowStudio);
        Assert.Equal("Layout: Focus", vm.LayoutButtonName);
        Assert.Equal(DashboardView.Focus, new SettingsStore(path).Load().Dashboard);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Focus_ShowsTheHeroOnOverview_AndOtherPagesWithBack()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true));
        vm.SetDashboardCommand.Execute(DashboardView.Focus);

        Assert.True(vm.ShowFocusHero);
        Assert.False(vm.ShowFocusPage);

        vm.NavigateCommand.Execute(AppSection.Connections);

        Assert.False(vm.ShowFocusHero);
        Assert.True(vm.ShowFocusPage);
        Assert.IsType<ConnectionsPageModel>(vm.DevicePage);

        vm.NavigateCommand.Execute(AppSection.Overview);

        Assert.True(vm.ShowFocusHero);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task SwitchingLayoutInSettings_StaysInSettings()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true));
        vm.NavigateCommand.Execute(AppSection.Settings);

        vm.SetDashboardCommand.Execute(DashboardView.Glance);

        Assert.True(vm.IsSettings);
        Assert.False(vm.ShowGlance);

        vm.SelectDeviceCommand.Execute(vm.Devices[0]);

        Assert.True(vm.ShowGlance);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Glance_UsesTheIconRail_AndStudioGivesTheSidebarBack()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true));
        vm.SetWindowNarrow(false);

        vm.SetDashboardCommand.Execute(DashboardView.Glance);
        Assert.True(vm.IsSidebarCompact);

        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.IsSidebarCompact);

        vm.SetDashboardCommand.Execute(DashboardView.Studio);
        Assert.False(vm.IsSidebarCompact);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task OpenFromGlance_ShowsTheDeviceInStudio()
    {
        var vm = await CreateAsync(new SwitchableDevice("a", true), new SwitchableDevice("b", true));
        vm.SetDashboardCommand.Execute(DashboardView.Glance);
        vm.NavigateCommand.Execute(AppSection.Details);

        vm.OpenDeviceCommand.Execute(vm.Devices[1]);

        Assert.Equal(DashboardView.Studio, vm.Dashboard);
        Assert.Equal("b", vm.SelectedDevice!.Name);
        Assert.Equal(AppSection.Overview, vm.Section);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task OlderSettingsFile_OpensInStudioWithConnectedDevices()
    {
        var path = NewSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "theme": "light", "accent": "ice" }""");

        var vm = await CreateAsync(path, new SwitchableDevice("a", true));

        Assert.Equal(DashboardView.Studio, vm.Dashboard);
        Assert.Equal(DeviceFilter.Connected, vm.Settings.DeviceFilter);
        Assert.Equal(AccentChoice.Ice, vm.Settings.Accent);
        Assert.Equal(AppSettings.CurrentVersion, new SettingsStore(path).Load().Version);
        await vm.DisposeAsync();
    }

    // ---- The window ----

    private static async Task<(MainWindow Window, MainWindowViewModel Vm)> ShowAsync()
    {
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: new SettingsStore(NewSettingsPath()));
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 780 };
        window.Show();
        await vm.RefreshDevicesAsync();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    [AvaloniaFact]
    public async Task Window_ShowsOneLayoutAtATime()
    {
        var (window, vm) = await ShowAsync();
        var studio = window.FindControl<Control>("DeviceLayout")!;
        var focus = window.FindControl<Control>("FocusLayout")!;
        var glance = window.FindControl<Control>("GlanceLayout")!;

        foreach (var (view, shown) in new[] { (DashboardView.Studio, studio), (DashboardView.Focus, focus), (DashboardView.Glance, glance) })
        {
            vm.SetDashboardCommand.Execute(view);
            Settle();

            Assert.All(new[] { studio, focus, glance }, c => Assert.Equal(ReferenceEquals(c, shown), c.IsVisible));
        }

        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Window_GlanceAllowsASmallerWindow()
    {
        var (window, vm) = await ShowAsync();

        vm.SetDashboardCommand.Execute(DashboardView.Glance);
        Assert.Equal(MainWindow.GlanceMinWidth, window.MinWidth);

        vm.SetDashboardCommand.Execute(DashboardView.Focus);
        Assert.Equal(MainWindow.StandardMinWidth, window.MinWidth);

        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Window_CtrlShiftNumber_SwitchesLayout()
    {
        var (window, vm) = await ShowAsync();

        window.KeyPress(Key.D3, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Digit3, "3");
        Assert.Equal(DashboardView.Glance, vm.Dashboard);

        window.KeyPress(Key.D2, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Digit2, "2");
        Assert.Equal(DashboardView.Focus, vm.Dashboard);

        window.KeyPress(Key.D1, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Digit1, "1");
        Assert.Equal(DashboardView.Studio, vm.Dashboard);

        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Window_SwitchingLayout_MovesFocusIntoTheNewLayout()
    {
        var (window, vm) = await ShowAsync();
        Settle();
        var tab = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("tab") && b.IsEffectivelyVisible);
        tab.Focus();

        vm.SetDashboardCommand.Execute(DashboardView.Glance);
        Settle();

        var focused = window.FocusManager!.GetFocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.True(focused!.IsEffectivelyVisible);
        Assert.Contains(window.FindControl<Control>("GlanceLayout")!, focused.GetVisualAncestors());

        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaTheory]
    [InlineData(DashboardView.Studio)]
    [InlineData(DashboardView.Focus)]
    [InlineData(DashboardView.Glance)]
    public async Task Window_EveryButtonIsNamedAndAtLeast32Pixels(DashboardView view)
    {
        var (window, vm) = await ShowAsync();

        foreach (var device in vm.Devices.ToList())
        {
            vm.SelectDeviceCommand.Execute(device);
            vm.SetDashboardCommand.Execute(view);
            Settle();

            // Scroll bar arrows belong to the platform theme, not to the layouts.
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && b is not RepeatButton && b.FindAncestorOfType<ScrollBar>() is null).ToList();
            Assert.NotEmpty(buttons);
            Assert.All(buttons, b =>
            {
                Assert.True(b.Bounds.Height >= 32, $"{Describe(b)} is {b.Bounds.Height} px high in {view}");
                Assert.True(b.Bounds.Width >= 32, $"{Describe(b)} is {b.Bounds.Width} px wide in {view}");
                Assert.False(string.IsNullOrWhiteSpace(Describe(b)), $"An unnamed button in {view}");
            });
        }

        window.Close();
        await vm.DisposeAsync();
    }

    private static string? Describe(Button b) =>
        AutomationProperties.GetName(b) is { Length: > 0 } name ? name : b.Content as string;

    [AvaloniaFact]
    public async Task Window_FilterLeavesNothing_ShowsTheEmptyStateWithAWayOut()
    {
        var (window, vm) = await ShowAsync();
        foreach (var device in vm.Devices.Select(d => d.Device).OfType<Samaat.Simulator.SimulatedEarbudDevice>())
        {
            device.SetConnected(false);
        }

        Settle();

        vm.SelectDeviceCommand.Execute(vm.Devices[0]);
        vm.SetDeviceFilterCommand.Execute(DeviceFilter.Paired);
        vm.SetDeviceFilterCommand.Execute(DeviceFilter.Connected);
        Settle();

        Assert.True(window.FindControl<Control>("FilterEmptyState")!.IsVisible);
        Assert.Empty(vm.VisibleDevices);

        window.Close();
        await vm.DisposeAsync();
    }
}
