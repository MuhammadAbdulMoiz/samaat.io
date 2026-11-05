namespace Samaat.Core.Commands;

/// <summary>Runs a device command with a confirmation timeout and turns errors into results.</summary>
public static class CommandRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs <paramref name="command"/> and waits up to <paramref name="timeout"/> for the device to confirm.
    /// On timeout the token the command received is cancelled, so a driver stops waiting for a reply that the UI has
    /// already given up on (and doesn't hold its channel against the next command). Only cancellation by the caller
    /// is rethrown; everything else becomes a <see cref="CommandResult"/>.
    /// </summary>
    public static async Task<CommandResult> RunAsync(
        Func<CancellationToken, Task<CommandResult>> command,
        TimeSpan timeout,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        time ??= TimeProvider.System;
        using var deadline = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            // WaitAsync still bounds the wait for a command that ignores its token.
            return await command(linked.Token).WaitAsync(timeout, time, cancellationToken);
        }
        catch (TimeoutException)
        {
            await deadline.CancelAsync();
            return new CommandResult(CommandStatus.TimedOut);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new CommandResult(CommandStatus.TimedOut);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CommandResult(CommandStatus.Failed, ex.Message);
        }
    }
}
