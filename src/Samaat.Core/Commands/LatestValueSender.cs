namespace Samaat.Core.Commands;

/// <summary>
/// Sends values one at a time, and while a send is in flight keeps only the newest value.
/// Dragging a slider from 10 to 0 sends two commands rather than eleven. Every submission
/// completes with the result of the send that carried its value or a newer one.
/// </summary>
public sealed class LatestValueSender<T>(Func<T, CancellationToken, Task<CommandResult>> send)
{
    private readonly Lock _gate = new();
    private TaskCompletionSource<CommandResult> _batch = NewBatch();
    private bool _running;
    private bool _hasPending;
    private T _pending = default!;

    public Task<CommandResult> SubmitAsync(T value)
    {
        Task<CommandResult> task;
        lock (_gate)
        {
            _pending = value;
            _hasPending = true;
            task = _batch.Task;
            if (_running)
            {
                return task;
            }

            _running = true;
        }

        _ = PumpAsync();
        return task;
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            T value;
            TaskCompletionSource<CommandResult> batch;
            lock (_gate)
            {
                if (!_hasPending)
                {
                    _running = false;
                    return;
                }

                value = _pending;
                _hasPending = false;
                batch = _batch;
                _batch = NewBatch();
            }

            CommandResult result;
            try
            {
                result = await send(value, CancellationToken.None);
            }
            catch (Exception ex)
            {
                result = new CommandResult(CommandStatus.Failed, ex.Message);
            }

            batch.TrySetResult(result);
        }
    }

    private static TaskCompletionSource<CommandResult> NewBatch() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
