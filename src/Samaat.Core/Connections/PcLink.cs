using System.Runtime.InteropServices;
using Samaat.Core.Commands;
using Samaat.Core.Devices;

namespace Samaat.Core.Connections;

/// <summary>Connect or disconnect the earbuds from this computer, like the buttons in the system's Bluetooth settings.</summary>
public interface IPcLinkFeature : IFeature
{
    Task<CommandResult> ConnectAsync(CancellationToken ct = default);

    Task<CommandResult> DisconnectAsync(CancellationToken ct = default);
}

/// <summary>One thing to send a request to. <paramref name="Send"/> returns an HRESULT-style code (negative is a failure).</summary>
public sealed record PcLinkTarget(string FilterId, Func<int> Send);

public sealed record PcLinkSendResult(bool Accepted, int Attempted, int? FirstFailure);

public static class PcLink
{
    private const int EFail = unchecked((int)0x80004005);

    /// <summary>Confirmed when the device reaches <paramref name="wanted"/>, TimedOut otherwise. Cancellation throws, like <see cref="CommandRunner"/>.</summary>
    public static async Task<CommandResult> AwaitStateAsync(
        IEarbudDevice device, ConnectionState wanted, TimeSpan timeout, TimeProvider time, CancellationToken ct = default)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            if (device.Connection == wanted)
            {
                reached.TrySetResult();
            }
        }

        void OnChanged(object? sender, EventArgs e) => Check();

        // Subscribe before checking, so a change between the two can't be missed.
        device.ConnectionChanged += OnChanged;
        try
        {
            Check();
            await reached.Task.WaitAsync(timeout, time, ct);
            return CommandResult.Confirmed;
        }
        catch (TimeoutException)
        {
            return new CommandResult(CommandStatus.TimedOut);
        }
        finally
        {
            device.ConnectionChanged -= OnChanged;
        }
    }

    /// <summary>Sends to every distinct filter (deduped by id). Accepted when at least one returned success.</summary>
    public static PcLinkSendResult SendToAll(IEnumerable<PcLinkTarget> targets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var attempted = 0;
        var accepted = false;
        int? firstFailure = null;
        foreach (var target in targets)
        {
            if (!seen.Add(target.FilterId))
            {
                continue;
            }

            attempted++;
            int code;
            try
            {
                code = target.Send();
            }
            catch (Exception ex)
            {
                code = Marshal.GetHRForException(ex);
                if (code >= 0)
                {
                    code = EFail; // a managed exception with no failure HRESULT still failed
                }
            }

            if (code >= 0)
            {
                accepted = true;
            }
            else
            {
                firstFailure ??= code;
            }
        }

        return new PcLinkSendResult(accepted, attempted, firstFailure);
    }
}
