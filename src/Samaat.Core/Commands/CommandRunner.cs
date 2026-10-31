namespace Samaat.Core.Commands;

/// <summary>Runs a device command with a confirmation timeout and turns errors into results.</summary>
public static class CommandRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public static async Task<CommandResult> RunAsync(
        Func<CancellationToken, Task<CommandResult>> command,
        TimeSpan timeout,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await command(cancellationToken).WaitAsync(timeout, time ?? TimeProvider.System, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new CommandResult(CommandStatus.TimedOut);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CommandResult(CommandStatus.Failed, ex.Message);
        }
    }
}
