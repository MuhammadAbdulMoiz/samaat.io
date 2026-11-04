using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Settings;
using Samaat.App.Features.Sound;
using Samaat.Core.Audio;
using Samaat.Core.Battery;
using Samaat.Core.Platform;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

internal sealed class FakeAudioShare : IAudioShare
{
    public bool IsSupported { get; set; } = true;

    public IReadOnlyList<AudioOutput> Outputs { get; set; } =
    [
        new("spk", "Speakers", true),
        new("tv", "TV", false),
        new("hp", "Headphones", false),
    ];

    public AudioShareState State { get; set; }

    public string? TargetId { get; set; }

    public string? StatusText { get; set; }

    public int DelayMs { get; set; }

    public string? FailWith { get; set; }

    public bool ThrowOnStart { get; set; }

    public Func<string, Task>? StartGate { get; set; }

    public Task? StopGate { get; set; }

    public List<string> Started { get; } = [];

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public event EventHandler? Changed;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public async Task StartAsync(string targetId, CancellationToken ct = default)
    {
        if (StartGate is not null)
        {
            await StartGate(targetId);
        }

        if (ThrowOnStart)
        {
            throw new InvalidOperationException("Boom");
        }

        Started.Add(targetId);
        TargetId = targetId;
        State = FailWith is null ? AudioShareState.Sharing : AudioShareState.Stopped;
        StatusText = FailWith;
        Raise();
    }

    public async Task StopAsync()
    {
        StopCalls++;
        if (StopGate is not null)
        {
            await StopGate;
        }

        State = AudioShareState.Off;
        Raise();
    }

    public bool ThrowOnDispose { get; set; }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        if (ThrowOnDispose)
        {
            throw new InvalidOperationException("Dispose boom");
        }

        return new(StopAsync());
    }
}

internal sealed class FakeSwitcher : IDefaultOutputSwitcher
{
    public bool IsSupported { get; set; } = true;

    public Exception? FailWith { get; set; }

    public List<string> Calls { get; } = [];

    public void SetDefault(string outputId)
    {
        Calls.Add(outputId);
        if (FailWith is not null)
        {
            throw FailWith;
        }
    }
}

public sealed class AudioShareViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<FakeAudioShare> _created = [];

    private FakeAudioShare Lister => _created[0];

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static AudioOutput[] Outs(params string[] ids) =>
        ids.Select((id, i) => new AudioOutput(id, id.ToUpperInvariant(), i == 0)).ToArray();

    // The first share the factory makes is the lister; the rest belong to rows, in toggle order.
    private (AudioShareViewModel Vm, FakeSwitcher Switcher, SettingsStore Store) Create(
        IReadOnlyList<AudioOutput>? outputs = null, bool supported = true, Action<FakeAudioShare>? rowShare = null)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = new SettingsViewModel(store, new LowBatteryDetector(), new FakeStartup());
        var switcher = new FakeSwitcher();
        var vm = new AudioShareViewModel(() =>
        {
            var share = new FakeAudioShare { IsSupported = supported };
            if (_created.Count == 0)
            {
                share.Outputs = outputs ?? Outs("spk", "tv", "hp");
            }
            else
            {
                rowShare?.Invoke(share);
            }

            _created.Add(share);
            return share;
        }, switcher, settings);
        return (vm, switcher, store);
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void Rows_ExcludeDefault()
    {
        var (vm, _, _) = Create();
        Assert.Equal(["tv", "hp"], vm.Rows.Select(r => r.Id));
        Assert.Equal("SPK", vm.DefaultName);
        Assert.Contains("Bluetooth", vm.HelpText);
        Assert.Single(_created);
    }

    [AvaloniaFact]
    public void Toggle_StartsThatRowsShare()
    {
        var (vm, _, _) = Create();
        vm.Rows[1].IsSharing = true;
        Settle();
        Assert.Equal(2, _created.Count);
        Assert.Equal("hp", _created[1].TargetId);
        Assert.True(vm.Rows[1].IsSharing);
        Assert.Equal(1, vm.SharingCount);
    }

    [AvaloniaFact]
    public void ThreeSharing_DisablesOtherToggles_ReenablesWhenOneStops()
    {
        var (vm, _, _) = Create(Outs("spk", "a", "b", "c", "d"));
        for (var i = 0; i < 3; i++)
        {
            vm.Rows[i].IsSharing = true;
        }

        Settle();
        Assert.Equal(3, vm.SharingCount);
        Assert.False(vm.Rows[3].CanToggle);
        Assert.True(vm.Rows[0].CanToggle);
        vm.Rows[0].IsSharing = false;
        Settle();
        Assert.Equal(2, vm.SharingCount);
        Assert.True(vm.Rows[3].CanToggle);
    }

    [AvaloniaFact]
    public async Task FourQuickToggles_OnlyThreeShareFactoriesCreated()
    {
        var gate = new TaskCompletionSource();
        var (vm, _, _) = Create(Outs("spk", "a", "b", "c", "d"), rowShare: s => s.StartGate = _ => gate.Task);
        foreach (var row in vm.Rows)
        {
            row.IsSharing = true;
        }

        Assert.Equal(4, _created.Count);
        Assert.False(vm.Rows[3].IsSharing);
        gate.SetResult();
        await Task.Delay(50);
        Settle();
        Assert.Equal(3, vm.SharingCount);
    }

    [AvaloniaFact]
    public void StoppedRow_DisposesShare_RestartCreatesNew()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        var first = _created[1];
        first.State = AudioShareState.Stopped;
        first.StatusText = "TV was unplugged";
        first.Raise();
        Settle();
        Assert.Equal(1, first.DisposeCalls);
        Assert.False(vm.Rows[0].IsSharing);
        Assert.Equal("TV was unplugged", vm.Rows[0].Status);
        Assert.Equal(0, vm.SharingCount);
        vm.Rows[0].IsSharing = true;
        Settle();
        Assert.Equal(3, _created.Count);
        Assert.Equal(1, vm.SharingCount);
    }

    [AvaloniaFact]
    public async Task LateStoppedFromOldShare_AfterRestart_KeepsNewShareAndSlot()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        var old = _created[1];
        var gate = new TaskCompletionSource();
        old.StopGate = gate.Task;
        vm.Rows[0].IsSharing = false;
        // The old share is still attached while its stop is pending, so this Changed reaches the row.
        old.State = AudioShareState.Stopped;
        old.Raise();
        Settle();
        vm.Rows[0].IsSharing = true;
        gate.SetResult();
        await Task.Delay(100);
        Settle();
        Assert.True(vm.Rows[0].IsSharing);
        Assert.Equal(1, vm.SharingCount);
        Assert.Equal(3, _created.Count);
        Assert.Equal(0, _created[2].DisposeCalls);
        Assert.Equal(["tv"], _created[2].Started);
    }

    [AvaloniaFact]
    public async Task ToggleOffThenOn_DuringPendingStop_EndsSharing()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        var gate = new TaskCompletionSource();
        _created[1].StopGate = gate.Task;
        vm.Rows[0].IsSharing = false;
        vm.Rows[0].IsSharing = true;
        gate.SetResult();
        await Task.Delay(100);
        Settle();
        Assert.True(vm.Rows[0].IsSharing);
        Assert.Equal(1, vm.SharingCount);
        Assert.Equal(3, _created.Count);
        Assert.Equal(["tv"], _created[2].Started);
        Assert.Equal(1, _created[1].DisposeCalls);
    }

    [AvaloniaFact]
    public void SwitchHere_CallsSwitcher()
    {
        var (vm, switcher, _) = Create();
        Assert.True(vm.CanSwitch);
        vm.Rows[1].SwitchHereCommand.Execute(null);
        Assert.Equal(["hp"], switcher.Calls);
    }

    [AvaloniaFact]
    public void SwitchHere_Failure_ShowsStatus()
    {
        var (vm, switcher, _) = Create();
        switcher.FailWith = new InvalidOperationException("nope");
        vm.Rows[0].SwitchHereCommand.Execute(null);
        Assert.Equal("Couldn't switch: nope", vm.Status);
    }

    [AvaloniaFact]
    public void SharedRow_BecomesDefault_RowRemoved_ShareDisposed()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        Lister.Outputs = [new("spk", "SPK", false), new("tv", "TV", true), new("hp", "HP", false)];
        Lister.Raise();
        Settle();
        Assert.Equal(["spk", "hp"], vm.Rows.Select(r => r.Id));
        Assert.Equal("TV", vm.DefaultName);
        Assert.Equal(1, _created[1].DisposeCalls);
        Assert.Equal(0, vm.SharingCount);
        Assert.Null(vm.Status);
    }

    [AvaloniaFact]
    public void SharedDevice_Removed_ShowsDisconnectedStatus()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        Lister.Outputs = Outs("spk", "hp");
        Lister.Raise();
        Settle();
        Assert.Equal(["hp"], vm.Rows.Select(r => r.Id));
        Assert.Equal("Stopped sharing to TV: disconnected", vm.Status);
        Assert.Equal(1, _created[1].DisposeCalls);
        Assert.Equal(0, vm.SharingCount);
    }

    [AvaloniaFact]
    public void RowStoppedBeforeRemoval_StillShowsDisconnectedStatus()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        _created[1].State = AudioShareState.Stopped;
        _created[1].StatusText = "TV was unplugged";
        _created[1].Raise();
        Settle();
        Assert.False(vm.Rows[0].IsSharing);
        Lister.Outputs = Outs("spk", "hp");
        Lister.Raise();
        Settle();
        Assert.Equal("Stopped sharing to TV: disconnected", vm.Status);
    }

    [AvaloniaFact]
    public async Task Dispose_AwaitsRemovedRowDisposal()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        var gate = new TaskCompletionSource();
        _created[1].StopGate = gate.Task;
        Lister.Outputs = Outs("spk", "hp");
        Lister.Raise();
        Settle();
        var dispose = vm.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(dispose.IsCompleted);
        gate.SetResult();
        await dispose;
        Assert.Equal(1, _created[1].DisposeCalls);
        Assert.Equal(AudioShareState.Off, _created[1].State);
    }

    [AvaloniaFact]
    public void ImmediateStopAfterStart_ThenRemoval_ShowsDisconnectedStatus()
    {
        var (vm, _, _) = Create(rowShare: s => s.FailWith = "Device gone");
        vm.Rows[0].IsSharing = true;
        Settle();
        Assert.False(vm.Rows[0].IsSharing);
        Lister.Outputs = Outs("spk", "hp");
        Lister.Raise();
        Settle();
        Assert.Equal("Stopped sharing to TV: disconnected", vm.Status);
    }

    [AvaloniaFact]
    public async Task ParentDispose_AwaitsInFlightRowTeardown()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        var gate = new TaskCompletionSource();
        _created[1].StopGate = gate.Task;
        _created[1].State = AudioShareState.Stopped;
        _created[1].Raise();
        Settle();
        var dispose = vm.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(dispose.IsCompleted);
        gate.SetResult();
        await dispose;
        Assert.Equal(AudioShareState.Off, _created[1].State);
    }

    [AvaloniaFact]
    public void TearDown_DisposeThrowsSynchronously_ShowsStatus()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        _created[1].ThrowOnDispose = true;
        _created[1].State = AudioShareState.Stopped;
        _created[1].Raise();
        Settle();
        Assert.Equal("Dispose boom", vm.Rows[0].Status);
        Assert.False(vm.Rows[0].IsSharing);
    }

    [AvaloniaFact]
    public void Rows_SurviveRefresh()
    {
        var (vm, _, _) = Create();
        var row = vm.Rows[0];
        Lister.Outputs = Outs("spk", "tv", "hp", "new");
        Lister.Raise();
        Settle();
        Assert.Same(row, vm.Rows[0]);
        Assert.Equal(3, vm.Rows.Count);
    }

    [AvaloniaFact]
    public void Delay_PerRow_SavedAndPushed()
    {
        var (vm, _, store) = Create();
        vm.Rows[0].IsSharing = true;
        Settle();
        vm.Rows[0].DelayMs = 120;
        Assert.Equal(120, _created[1].DelayMs);
        Assert.Equal(120, store.Load().ShareDelays["tv"]);
        Assert.Equal("Delay · 120 ms", vm.Rows[0].DelayText);
        Assert.Equal(0, vm.Rows[1].DelayMs);
    }

    [AvaloniaFact]
    public async Task RapidToggles_LastIntentWins()
    {
        var gate = new TaskCompletionSource();
        var (vm, _, _) = Create(rowShare: s => s.StartGate = _ => gate.Task);
        var row = vm.Rows[0];
        row.IsSharing = true;
        row.IsSharing = false;
        row.IsSharing = true;
        row.IsSharing = false;
        gate.SetResult();
        await Task.Delay(100);
        Settle();
        Assert.False(row.IsSharing);
        Assert.Equal(0, vm.SharingCount);
        Assert.Equal(1, _created[1].DisposeCalls);
        Assert.Empty(_created[1].Started.Skip(1));
    }

    [AvaloniaFact]
    public async Task Dispose_DisposesAllSharesAndLister()
    {
        var (vm, _, _) = Create();
        vm.Rows[0].IsSharing = true;
        vm.Rows[1].IsSharing = true;
        Settle();
        await vm.DisposeAsync();
        Assert.Equal(3, _created.Count);
        Assert.All(_created, s => Assert.Equal(1, s.DisposeCalls));
    }

    [AvaloniaFact]
    public void Unsupported_ShowsWindowsOnlyText()
    {
        var (vm, _, _) = Create([], supported: false);
        Assert.False(vm.IsSupported);
        Assert.Equal("Sharing audio needs Windows.", vm.UnavailableText);
    }
}
