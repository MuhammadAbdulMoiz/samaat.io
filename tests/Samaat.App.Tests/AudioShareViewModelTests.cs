using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Samaat.App.Features.Settings;
using Samaat.App.Features.Sound;
using Samaat.Core.Audio;
using Samaat.Core.Battery;
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

    public List<string> Started { get; } = [];

    public int StopCalls { get; private set; }

    public event EventHandler? Changed;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public Task StartAsync(string targetId, CancellationToken ct = default)
    {
        if (ThrowOnStart)
        {
            throw new InvalidOperationException("Boom");
        }

        Started.Add(targetId);
        TargetId = targetId;
        State = FailWith is null ? AudioShareState.Sharing : AudioShareState.Stopped;
        StatusText = FailWith;
        Raise();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCalls++;
        State = AudioShareState.Off;
        Raise();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

public sealed class AudioShareViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private (AudioShareViewModel Vm, SettingsViewModel Settings, SettingsStore Store) Create(FakeAudioShare share, AppSettings? saved = null)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        if (saved is not null)
        {
            store.Save(saved);
        }

        var settings = new SettingsViewModel(store, new LowBatteryDetector(), new FakeStartup());
        return (new AudioShareViewModel(share, settings), settings, store);
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void Targets_ExcludeDefault()
    {
        var (vm, _, _) = Create(new FakeAudioShare());
        Assert.Equal(["tv", "hp"], vm.Targets.Select(t => t.Id));
        Assert.True(vm.HasTargets);
        Assert.Contains("Speakers", vm.HelpText);
    }

    [AvaloniaFact]
    public void Toggle_On_StartsWithSelectedTarget()
    {
        var share = new FakeAudioShare();
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        Assert.Equal("tv", share.TargetId);
        Assert.True(vm.IsSharing);
        Assert.Equal(0, share.StopCalls);
    }

    [AvaloniaFact]
    public void Start_Failure_ShowsStatus_AndTurnsToggleOff()
    {
        var share = new FakeAudioShare { FailWith = "Device busy" };
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        Assert.False(vm.IsSharing);
        Assert.Equal("Device busy", vm.Status);
        Assert.Equal(0, share.StopCalls);
    }

    [AvaloniaFact]
    public void Share_StoppedByDevice_TurnsToggleOff()
    {
        var share = new FakeAudioShare();
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        share.State = AudioShareState.Stopped;
        share.StatusText = "TV was unplugged";
        share.Raise();
        Settle();
        Assert.False(vm.IsSharing);
        Assert.Equal("TV was unplugged", vm.Status);
        Assert.Equal(0, share.StopCalls);
    }

    [AvaloniaFact]
    public void Delay_Change_PushedAndSaved()
    {
        var share = new FakeAudioShare();
        var (vm, _, store) = Create(share);
        vm.DelayMs = 120;
        Assert.Equal(120, share.DelayMs);
        Assert.Equal(120, store.Load().ShareDelayMs);
    }

    [AvaloniaFact]
    public void SelectedTarget_Saved_AndRestored()
    {
        var (vm, _, store) = Create(new FakeAudioShare());
        vm.SelectedTarget = vm.Targets[1];
        Assert.Equal("hp", store.Load().ShareTargetId);
        var (again, _, _) = Create(new FakeAudioShare());
        Assert.Equal("hp", again.SelectedTarget?.Id);
    }

    [AvaloniaFact]
    public void SavedTarget_Missing_FallsBackToFirst()
    {
        var (vm, _, _) = Create(new FakeAudioShare(), new AppSettings { ShareTargetId = "gone", ShareDelayMs = 50 });
        Assert.Equal("tv", vm.SelectedTarget?.Id);
        Assert.Equal(50, vm.DelayMs);
    }

    [AvaloniaFact]
    public void SelectedTarget_Removed_Reselects()
    {
        var share = new FakeAudioShare();
        var (vm, _, _) = Create(share, new AppSettings { ShareTargetId = "hp" });
        Assert.Equal("hp", vm.SelectedTarget?.Id);
        share.Outputs = [new("spk", "Speakers", true), new("tv", "TV", false)];
        share.Raise();
        Settle();
        Assert.Equal("tv", vm.SelectedTarget?.Id);
    }

    [AvaloniaFact]
    public void Unsupported_ShowsWindowsOnlyText()
    {
        var (vm, _, _) = Create(new FakeAudioShare { IsSupported = false, Outputs = [] });
        Assert.False(vm.IsSupported);
        Assert.Equal("Sharing audio needs Windows.", vm.UnavailableText);
    }

    [AvaloniaFact]
    public void Toggle_On_WithoutTarget_DoesNotStop()
    {
        var share = new FakeAudioShare { Outputs = [new("spk", "Speakers", true)] };
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        Assert.False(vm.IsSharing);
        Assert.Equal(0, share.StopCalls);
    }

    [AvaloniaFact]
    public void ChangeTarget_WhileSharing_RestartsOnNewTarget()
    {
        var share = new FakeAudioShare();
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        vm.SelectedTarget = vm.Targets[1];
        Settle();
        Assert.Equal(["tv", "hp"], share.Started);
        Assert.True(vm.IsSharing);
    }

    [AvaloniaFact]
    public void Start_Throws_ShowsMessage_AndTurnsToggleOff()
    {
        var share = new FakeAudioShare { ThrowOnStart = true };
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        Assert.False(vm.IsSharing);
        Assert.Equal("Boom", vm.Status);
    }

    [AvaloniaFact]
    public async Task Dispose_StopsSharing()
    {
        var share = new FakeAudioShare();
        var (vm, _, _) = Create(share);
        vm.IsSharing = true;
        Settle();
        await vm.DisposeAsync();
        Assert.Equal(AudioShareState.Off, share.State);
    }
}
