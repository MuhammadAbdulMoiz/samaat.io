using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Samaat.App.Shared.Controls;
using Samaat.App.Shell;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

public class RedesignShellTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel Vm)> ShowAsync(double width = 1180, bool refresh = true, bool? systemReduceMotion = null)
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json"));
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: store,
            motion: systemReduceMotion is { } reduce ? new FakeMotion(reduce) : null);
        var window = new MainWindow { DataContext = vm, Width = width };
        window.Show();
        if (refresh)
        {
            await vm.RefreshDevicesAsync();
        }

        Pump();
        return (window, vm);
    }

    private static void Pump(int ticks = 60)
    {
        for (var i = 0; i < ticks; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static IEnumerable<T> All<T>(Visual root) => root.GetVisualDescendants().OfType<T>();

    private static Control Named(Visual root, string name) =>
        All<Control>(root).First(c => AutomationProperties.GetName(c) == name);

    private static async Task CloseAsync(Window window, MainWindowViewModel vm)
    {
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Sidebar_ListsEveryDeviceWithItsBattery()
    {
        var (window, vm) = await ShowAsync();
        var sidebar = window.FindControl<Border>("Sidebar")!;
        var texts = All<TextBlock>(sidebar).Select(t => t.Text).ToList();

        Assert.Equal(3, vm.Devices.Count);
        foreach (var device in vm.Devices)
        {
            Assert.Contains(device.Name, texts);
            Assert.Contains(device.SummaryShort, texts);
        }

        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task SelectingADevice_ChangesTheSelection_AndKeepsTheTab()
    {
        var (window, vm) = await ShowAsync();

        vm.NavigateCommand.Execute(AppSection.Sound);
        vm.SelectDeviceCommand.Execute(vm.Devices[2]);

        Assert.Same(vm.Devices[2], vm.SelectedDevice);
        Assert.True(vm.IsSound);

        vm.NavigateCommand.Execute(AppSection.Settings);
        Assert.All(vm.Devices, d => Assert.False(d.IsCurrent));
        vm.SelectDeviceCommand.Execute(vm.Devices[1]);

        Assert.True(vm.IsOverview);
        Assert.True(vm.Devices[1].IsCurrent);
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task TabWell_SwitchesSections()
    {
        var (window, vm) = await ShowAsync();
        var tabs = new (string Name, AppSection Section)[]
        {
            ("Overview", AppSection.Overview), ("Sound", AppSection.Sound), ("Links", AppSection.Connections),
            ("Details", AppSection.Details), ("Diagnostics", AppSection.Info),
        };

        foreach (var (name, section) in tabs)
        {
            var tab = Assert.IsType<Button>(Named(window, name));
            tab.Command!.Execute(tab.CommandParameter);
            Assert.Equal(section, vm.Section);
        }

        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task Settings_IsReachable_WithAndWithoutDevices()
    {
        var (window, vm) = await ShowAsync(refresh: false);
        Assert.Empty(vm.Devices);
        var settings = Assert.IsType<Button>(Named(window, "Settings"));

        settings.Command!.Execute(settings.CommandParameter);

        Assert.True(vm.IsSettings);
        Assert.IsType<SettingsPageModel>(vm.CurrentPage);
        Assert.False(vm.ShowEmptyState);
        await vm.RefreshDevicesAsync();
        Pump();
        Assert.IsType<SettingsPageModel>(vm.CurrentPage);
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task SidebarIsCompact_BelowNineHundred_AndExpandsAgain()
    {
        var (window, vm) = await ShowAsync(1180);
        var sidebar = window.FindControl<Border>("Sidebar")!;
        Assert.False(vm.IsSidebarCompact);
        Assert.Equal(240, sidebar.Bounds.Width);

        window.Width = 800;
        Pump();
        Assert.True(vm.IsSidebarCompact);
        Assert.Equal(68, sidebar.Bounds.Width);

        window.Width = 1180;
        Pump();
        Assert.False(vm.IsSidebarCompact);
        Assert.Equal(240, sidebar.Bounds.Width);
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task DeviceRail_StacksAboveTheWorkspace_BelowSevenHundredSixtyOfContent()
    {
        var (window, vm) = await ShowAsync(1180);
        var layout = window.FindControl<SplitPanel>("DeviceLayout")!;
        Assert.False(layout.IsStacked);

        window.Width = 800;
        Pump();

        Assert.True(layout.IsStacked);
        await CloseAsync(window, vm);
    }

    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "ANC", "SPP", "HFP", "AVRCP", "A2DP", "VID", "PID", "ID", "BLE", "GATT", "RFCOMM", "SDP", "UUID", "OS", "EQ", "USB", "MAC", "RSSI",
    };

    /// <summary>Entirely-uppercase words of 4+ letters are micro-labels; text with digits is data (addresses, IDs).</summary>
    private static bool IsMicroLabel(string text)
    {
        if (text.Any(char.IsLower) || text.Any(char.IsDigit))
        {
            return false;
        }

        var letters = Regex.Matches(text, "[A-Z]+").Where(m => !Acronyms.Contains(m.Value)).Sum(m => m.Length);
        return letters >= 4;
    }

    [AvaloniaFact]
    public async Task NoPageShowsUppercaseMicroLabels()
    {
        var (window, vm) = await ShowAsync();
        var offenders = new List<string>();

        void Scan(string where)
        {
            Pump(30);
            foreach (var block in All<TextBlock>(window))
            {
                if (block.Text is { Length: > 0 } text && IsMicroLabel(text))
                {
                    offenders.Add($"{where}: {text}");
                }
            }
        }

        foreach (var device in vm.Devices.ToList())
        {
            vm.SelectDeviceCommand.Execute(device);
            foreach (var section in Enum.GetValues<AppSection>().Where(s => s != AppSection.Settings))
            {
                vm.NavigateCommand.Execute(section);
                Scan($"{device.Name}/{section}");
            }
        }

        vm.NavigateCommand.Execute(AppSection.Settings);
        Scan("Settings");

        Assert.Empty(offenders.Distinct());
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task BatteryFigures_ShowDashForUnreportedComponents_AndNumbersForReportedOnes()
    {
        var (window, vm) = await ShowAsync();
        var pro = vm.Devices.Single(d => d.Name == "Simulated Buds Pro");
        var headset = vm.Devices.Single(d => d.Name == "Simulated Headset");

        Assert.Equal(["Overall", "Left bud", "Right bud", "Case"], pro.Figures.Select(f => f.Label));
        Assert.All(pro.Figures, f => Assert.Matches(@"^\d+$", f.Value));
        Assert.All(pro.Figures, f => Assert.Equal("%", f.Unit));

        Assert.Matches(@"^\d+$", headset.Figures[0].Value);
        Assert.All(headset.Figures.Skip(1), f =>
        {
            Assert.Equal("–", f.Value);
            Assert.Equal("", f.Unit);
        });
        Assert.False(string.IsNullOrWhiteSpace(headset.BatteryReasonText));

        vm.SelectDeviceCommand.Execute(headset);
        Pump();
        Assert.DoesNotContain(All<TextBlock>(window).Where(t => t.IsEffectivelyVisible), t => t.Text == "Left bud");
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task BatteryPanel_ShowsOneFigureAndAReasonForSingleLevelDevices_AndFourForBuds()
    {
        var (window, vm) = await ShowAsync();
        var pro = vm.Devices.Single(d => d.Name == "Simulated Buds Pro");
        var headset = vm.Devices.Single(d => d.Name == "Simulated Headset");

        Assert.Equal(["Overall"], headset.ShownFigures.Select(f => f.Label));
        Assert.Equal("Left, right and case aren't reported for this device.", headset.BatteryReasonText);
        Assert.Equal(["Overall", "Left bud", "Right bud", "Case"], pro.ShownFigures.Select(f => f.Label));

        vm.SelectDeviceCommand.Execute(headset);
        Pump();
        var visible = All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Overall", visible);
        Assert.DoesNotContain("Right bud", visible);
        Assert.DoesNotContain("Case", visible);

        vm.SelectDeviceCommand.Execute(pro);
        Pump();
        Assert.Contains("Right bud", All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task Diagnostics_KeepsDetailsBehindDisclosures_ThatToggleFromTheKeyboard()
    {
        var (window, vm) = await ShowAsync();
        vm.SelectDeviceCommand.Execute(vm.Devices.Single(d => d.Name == "Simulated Headset"));
        vm.NavigateCommand.Execute(AppSection.Info);
        Pump();

        var toggles = All<ToggleButton>(window).Where(t => t.Classes.Contains("disclosure")).ToList();
        var names = toggles.Select(AutomationProperties.GetName).ToList();
        Assert.Equal(["Battery · 3 features", "Noise control · 7 features", "Connections · 4 features", "Technical details", "Event log"], names);
        Assert.All(toggles, t => Assert.False(t.IsChecked));

        IEnumerable<string?> Visible() => All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text);
        Assert.DoesNotContain("Wind reduction", Visible());
        Assert.DoesNotContain("Device ID", Visible());
        Assert.DoesNotContain("Identity", Visible());
        Assert.Contains("Overall battery", Visible());

        var noise = toggles[1];
        noise.Focus();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Pump();
        Assert.True(noise.IsChecked);
        Assert.Contains("Wind reduction", Visible());

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();
        Assert.False(noise.IsChecked);
        Assert.DoesNotContain("Wind reduction", Visible());

        void PressSpace(ToggleButton toggle)
        {
            toggle.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Pump();
        }

        PressSpace(toggles[3]);
        Assert.True(toggles[3].IsChecked);
        Assert.Contains("Device ID", Visible());

        var eventsBefore = Visible().Count();
        PressSpace(toggles[4]);
        Assert.True(toggles[4].IsChecked);
        Assert.True(Visible().Count() > eventsBefore, "opening the event log shows its entries");
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task ControlsHaveAccessibleNames()
    {
        var (window, vm) = await ShowAsync(800);
        var pro = vm.Devices.Single(d => d.Name == "Simulated Buds Pro");
        vm.SelectDeviceCommand.Execute(pro);
        Pump();

        Assert.True(vm.IsSidebarCompact);
        var rows = All<Button>(window).Select(AutomationProperties.GetName).ToList();
        foreach (var device in vm.Devices)
        {
            var row = Assert.Single(rows, n => n is not null && n.StartsWith(device.Name + ", ", StringComparison.Ordinal));
            Assert.EndsWith($", {device.ConnectionText}", row);
        }

        Assert.Matches(@"^Simulated Buds Pro, \d+%, Connected$", rows.Single(n => n is not null && n.StartsWith("Simulated Buds Pro, ")));
        foreach (var name in new[] { "Settings", "Refresh devices", vm.AlertCountText, "Overview", "Sound", "Links", "Details", "Diagnostics" })
        {
            Assert.Contains(name, rows);
        }

        Assert.Matches(@"^Battery \d+ percent$", AutomationProperties.GetName(Named(window, pro.BatteryAccessibleText)));
        Assert.Equal("Simulated Buds Pro earbuds", AutomationProperties.GetName(Named(window, "Simulated Buds Pro earbuds")));

        vm.NavigateCommand.Execute(AppSection.Settings);
        Pump();
        Assert.IsType<ToggleSwitch>(Named(window, "Reduce motion"));
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task ReduceMotion_RemovesTheFadeAndTheValueTransitions()
    {
        var (window, vm) = await ShowAsync(systemReduceMotion: true);
        var host = window.FindControl<TransitioningContentControl>("PageHost")!;
        var ring = All<BatteryRing>(window).First();

        Assert.Contains("reduce-motion", window.Classes);
        Assert.Equal(TimeSpan.Zero, Assert.IsType<CrossFade>(host.PageTransition).Duration);
        Assert.True(ring.Transitions is null or { Count: 0 });
        vm.NavigateCommand.Execute(AppSection.Sound);
        Pump();

        vm.Settings.ReduceMotion = false;
        Pump();

        Assert.DoesNotContain("reduce-motion", window.Classes);
        Assert.True(Assert.IsType<CrossFade>(host.PageTransition).Duration > TimeSpan.Zero);
        Assert.NotEmpty(ring.Transitions!);
        await CloseAsync(window, vm);
    }

    [AvaloniaFact]
    public async Task AnimationsStayOn_WhenTheSystemDoesNotAskToReduceThem()
    {
        var (window, vm) = await ShowAsync(systemReduceMotion: false);
        var host = window.FindControl<TransitioningContentControl>("PageHost")!;

        Assert.DoesNotContain("reduce-motion", window.Classes);
        Assert.True(Assert.IsType<CrossFade>(host.PageTransition).Duration > TimeSpan.Zero);
        await CloseAsync(window, vm);
    }
}
