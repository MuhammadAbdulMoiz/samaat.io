using Samaat.Core.Commands;

namespace Samaat.Core.Tests;

public class LatestValueSenderTests
{
    [Fact]
    public async Task SendsImmediatelyWhenIdle()
    {
        var sent = new List<int>();
        var sender = new LatestValueSender<int>((v, _) => { sent.Add(v); return Task.FromResult(CommandResult.Confirmed); });

        var result = await sender.SubmitAsync(3);

        Assert.Equal([3], sent);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ValuesSubmittedWhileBusy_CoalesceToTheLatest()
    {
        var sent = new List<int>();
        var gate = new TaskCompletionSource<CommandResult>();
        var sender = new LatestValueSender<int>((v, _) =>
        {
            sent.Add(v);
            return sent.Count == 1 ? gate.Task : Task.FromResult(CommandResult.Confirmed);
        });

        var first = sender.SubmitAsync(1);
        var second = sender.SubmitAsync(2);
        var third = sender.SubmitAsync(3);
        gate.SetResult(CommandResult.Confirmed);

        await Task.WhenAll(first, second, third);
        Assert.Equal([1, 3], sent);
    }

    [Fact]
    public async Task SupersededSubmissions_ReportTheLatestResult()
    {
        var gate = new TaskCompletionSource<CommandResult>();
        var calls = 0;
        var sender = new LatestValueSender<int>((v, _) =>
            ++calls == 1 ? gate.Task : Task.FromResult(CommandResult.Rejected($"no {v}")));

        _ = sender.SubmitAsync(1);
        var superseded = sender.SubmitAsync(2);
        var latest = sender.SubmitAsync(3);
        gate.SetResult(CommandResult.Confirmed);

        Assert.Equal("no 3", (await latest).Message);
        Assert.Equal("no 3", (await superseded).Message);
    }
}
