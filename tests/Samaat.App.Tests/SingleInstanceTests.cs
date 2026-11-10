namespace Samaat.App.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void SecondLaunch_IsRefusedAndWakesTheFirst()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // named events are Windows-only here; elsewhere TryAcquire never blocks a launch
        }

        var name = @"Local\Samaat.Test." + Guid.NewGuid().ToString("N");
        using var woke = new ManualResetEventSlim();
        var first = SingleInstance.TryAcquire(name, woke.Set);
        Assert.NotNull(first);

        Assert.Null(SingleInstance.TryAcquire(name, () => { }));
        Assert.True(woke.Wait(TimeSpan.FromSeconds(2)));

        first.Dispose();
        using var again = SingleInstance.TryAcquire(name, () => { });
        Assert.NotNull(again);
    }

    [Fact]
    public void FirstLaunch_Succeeds()
    {
        using var first = SingleInstance.TryAcquire(@"Local\Samaat.Test." + Guid.NewGuid().ToString("N"), () => { });

        Assert.NotNull(first);
    }
}
