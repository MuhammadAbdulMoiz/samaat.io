using Samaat.Core.Audio;

namespace Samaat.Core.Tests;

public class AudioShareTests
{
    private static readonly AudioOutput Speakers = new("spk", "Speakers", true);
    private static readonly AudioOutput Buds = new("buds", "Buds", false);
    private static readonly AudioOutput Tv = new("tv", "TV", false);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for condition");
            await Task.Delay(10);
        }
    }

    private static FakeAudioBackend Backend(params AudioOutput[] outputs) => new() { Outputs = [.. outputs] };

    private static async Task<AudioShare> Sharing(FakeAudioBackend backend, string target = "buds")
    {
        var share = new AudioShare(backend);
        await share.StartAsync(target);
        Assert.Equal(AudioShareState.Sharing, share.State);
        return share;
    }

    private static void MakeDefault(FakeAudioBackend backend, string id) =>
        backend.Outputs = backend.Outputs.Select(o => o with { IsDefault = o.Id == id }).ToList();

    [Fact]
    public async Task NoAudioShare_IsUnsupported_AndEmpty()
    {
        IAudioShare share = new NoAudioShare();

        Assert.False(share.IsSupported);
        Assert.Empty(share.Outputs);
        await Assert.ThrowsAsync<NotSupportedException>(() => share.StartAsync("any"));
        await share.DisposeAsync();
    }

    [Fact]
    public async Task Start_OpensFromDefaultToTarget()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);

        Assert.Equal(("spk", "buds", 0), Assert.Single(backend.Opens));
        Assert.Equal("buds", share.TargetId);
        Assert.Null(share.StatusText);
    }

    [Fact]
    public async Task Start_RejectsDefaultAsTarget_WithoutOpening()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = new AudioShare(backend);

        await share.StartAsync("spk");

        Assert.Equal(AudioShareState.Stopped, share.State);
        Assert.NotNull(share.StatusText);
        Assert.Empty(backend.Opens);
    }

    [Fact]
    public async Task Start_MissingTarget_Stopped()
    {
        var backend = Backend(Speakers);
        await using var share = new AudioShare(backend);

        await share.StartAsync("nope");

        Assert.Equal(AudioShareState.Stopped, share.State);
        Assert.Empty(backend.Opens);
    }

    [Fact]
    public async Task Start_NoDefault_Stopped()
    {
        var backend = Backend(Buds);
        await using var share = new AudioShare(backend);

        await share.StartAsync("buds");

        Assert.Equal(AudioShareState.Stopped, share.State);
        Assert.Equal("Couldn't start: no default output", share.StatusText);
    }

    [Fact]
    public async Task Start_OpenThrows_StoppedWithStatus()
    {
        var backend = Backend(Speakers, Buds);
        backend.OpenThrows = new InvalidOperationException("boom");
        await using var share = new AudioShare(backend);

        await share.StartAsync("buds");

        Assert.Equal(AudioShareState.Stopped, share.State);
        Assert.Equal("Couldn't start: boom", share.StatusText);
    }

    [Fact]
    public async Task TargetRemoved_Stops_WithDisconnectedStatus()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);

        backend.Outputs = [Speakers];
        backend.RaiseOutputsChanged();

        await WaitUntil(() => share.State == AudioShareState.Stopped);
        Assert.Equal("Stopped: Buds disconnected", share.StatusText);
        Assert.Equal(0, backend.OpenPipes);
    }

    [Fact]
    public async Task TargetRemoved_StatusUsesCachedName()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);

        // The new list doesn't contain the target at all, so the name must come from the cache.
        backend.Outputs = [Speakers, Tv];
        backend.RaiseOutputsChanged();

        await WaitUntil(() => share.State == AudioShareState.Stopped);
        Assert.Contains("Buds", share.StatusText);
    }

    [Fact]
    public async Task TargetBecomesDefault_Stops()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);

        MakeDefault(backend, "buds");
        backend.RaiseOutputsChanged();

        await WaitUntil(() => share.State == AudioShareState.Stopped);
        Assert.Equal("Stopped: Buds became the default output", share.StatusText);
        Assert.Equal(0, backend.OpenPipes);
    }

    [Fact]
    public async Task DefaultChanged_ReopensFromNewDefault_StaysSharing()
    {
        var backend = Backend(Speakers, Buds, Tv);
        await using var share = await Sharing(backend);

        MakeDefault(backend, "tv");
        backend.RaiseOutputsChanged();

        await WaitUntil(() => backend.Opens.Count == 2);
        Assert.Equal(("tv", "buds", 0), backend.Opens[1]);
        await WaitUntil(() => backend.OpenPipes == 1);
        Assert.Equal(AudioShareState.Sharing, share.State);
    }

    [Fact]
    public async Task StalePipeStopped_AfterRestart_IsIgnored()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);
        var first = backend.Pipes[0];

        await share.StartAsync("buds");
        first.RaiseStopped(new Exception("late"));
        await Task.Delay(100);

        Assert.Equal(AudioShareState.Sharing, share.State);
        Assert.Null(share.StatusText);
    }

    [Fact]
    public async Task CurrentPipeStopped_StopsWithMessage()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);

        backend.Pipes[0].RaiseStopped(new Exception("device lost"));

        await WaitUntil(() => share.State == AudioShareState.Stopped);
        Assert.Equal("Stopped: device lost", share.StatusText);
    }

    [Fact]
    public async Task CurrentPipeStopped_NullException_StoppedDot()
    {
        var backend = Backend(Speakers, Buds);
        await using var share = await Sharing(backend);

        backend.Pipes[0].RaiseStopped();

        await WaitUntil(() => share.State == AudioShareState.Stopped);
        Assert.Equal("Stopped.", share.StatusText);
    }

    [Fact]
    public async Task StoppedRaisedByDispose_OnStop_StaysOff()
    {
        var backend = Backend(Speakers, Buds);
        backend.RaiseStoppedOnDispose = true;
        await using var share = await Sharing(backend);

        await share.StopAsync();
        await Task.Delay(100);

        Assert.Equal(AudioShareState.Off, share.State);
        Assert.Null(share.StatusText);
        Assert.Equal("buds", share.TargetId);
    }

    [Fact]
    public async Task StoppedRaisedByDispose_OnReopen_StaysSharing()
    {
        var backend = Backend(Speakers, Buds, Tv);
        backend.RaiseStoppedOnDispose = true;
        await using var share = await Sharing(backend);

        MakeDefault(backend, "tv");
        backend.RaiseOutputsChanged();

        await WaitUntil(() => backend.Opens.Count == 2);
        await Task.Delay(100);
        Assert.Equal(AudioShareState.Sharing, share.State);
        Assert.Equal(1, backend.OpenPipes);
    }

    [Fact]
    public async Task StartWhileSharing_ReplacesPipe_OneOpen()
    {
        var backend = Backend(Speakers, Buds, Tv);
        await using var share = await Sharing(backend);

        await share.StartAsync("tv");

        Assert.Equal(AudioShareState.Sharing, share.State);
        Assert.Equal("tv", share.TargetId);
        Assert.Equal(1, backend.OpenPipes);
        Assert.Equal(2, backend.Opens.Count);
    }

    [Fact]
    public async Task ConcurrentStartStop_EndsConsistent()
    {
        var backend = Backend(Speakers, Buds, Tv);
        await using var share = new AudioShare(backend);

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() => i % 2 == 0
            ? share.StartAsync(i % 4 == 0 ? "buds" : "tv")
            : share.StopAsync())).ToArray();
        await Task.WhenAll(tasks);

        Assert.True(
            (share.State == AudioShareState.Sharing && backend.OpenPipes == 1)
            || (share.State == AudioShareState.Off && backend.OpenPipes == 0),
            $"{share.State} with {backend.OpenPipes} open pipes");
    }

    [Fact]
    public async Task Delay_PushedToPipe_AndKeptAcrossReopen()
    {
        var backend = Backend(Speakers, Buds, Tv);
        await using var share = await Sharing(backend);

        share.DelayMs = 120;
        Assert.Equal(120, backend.Pipes[0].DelayMs);

        MakeDefault(backend, "tv");
        backend.RaiseOutputsChanged();

        await WaitUntil(() => backend.Opens.Count == 2);
        Assert.Equal(120, backend.Opens[1].DelayMs);
    }

    [Fact]
    public async Task OutputsChanged_RefreshesOutputs_WhileOff()
    {
        var backend = Backend(Speakers);
        await using var share = new AudioShare(backend);
        var changed = 0;
        share.Changed += (_, _) => Interlocked.Increment(ref changed);

        backend.Outputs = [Speakers, Buds];
        backend.RaiseOutputsChanged();

        await WaitUntil(() => share.Outputs.Count == 2);
        await WaitUntil(() => Volatile.Read(ref changed) > 0);
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndDisposesBackend()
    {
        var backend = Backend(Speakers, Buds);
        var share = await Sharing(backend);

        await share.DisposeAsync();
        await share.DisposeAsync();
        await share.StartAsync("buds");

        Assert.True(backend.Disposed);
        Assert.Equal(0, backend.OpenPipes);
        Assert.Single(backend.Opens);
    }
}
