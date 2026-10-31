using Microsoft.Extensions.Time.Testing;
using Samaat.Core.Commands;

namespace Samaat.Core.Tests;

public class CommandRunnerTests
{
    [Fact]
    public async Task PassesThroughDeviceResult()
    {
        var result = await CommandRunner.RunAsync(_ => Task.FromResult(CommandResult.Rejected("Busy")), TimeSpan.FromSeconds(1));

        Assert.Equal(CommandStatus.Rejected, result.Status);
        Assert.Equal("Busy", result.Message);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ConfirmedResultSucceeds()
    {
        var result = await CommandRunner.RunAsync(_ => Task.FromResult(CommandResult.Confirmed), TimeSpan.FromSeconds(1));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task NoConfirmationWithinTimeout_IsTimedOut()
    {
        var time = new FakeTimeProvider();
        var never = new TaskCompletionSource<CommandResult>();

        var run = CommandRunner.RunAsync(_ => never.Task, TimeSpan.FromSeconds(2), time);
        time.Advance(TimeSpan.FromSeconds(3));

        var result = await run;
        Assert.Equal(CommandStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task Exception_IsReportedAsFailed()
    {
        var result = await CommandRunner.RunAsync(_ => throw new InvalidOperationException("Channel closed"), TimeSpan.FromSeconds(1));

        Assert.Equal(CommandStatus.Failed, result.Status);
        Assert.Equal("Channel closed", result.Message);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CommandRunner.RunAsync(ct => Task.FromCanceled<CommandResult>(ct), TimeSpan.FromSeconds(1), cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData(CommandStatus.Confirmed, null)]
    [InlineData(CommandStatus.TimedOut, "The device didn't confirm the change in time.")]
    [InlineData(CommandStatus.Unsupported, "This device doesn't support that.")]
    public void UserMessage_ExplainsFailures(CommandStatus status, string? expected) =>
        Assert.Equal(expected, new CommandResult(status).UserMessage);
}
