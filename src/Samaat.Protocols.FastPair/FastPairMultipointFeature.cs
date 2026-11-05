using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Connections;
using Samaat.Core.Devices;

namespace Samaat.Protocols.FastPair;

/// <summary>
/// Read-only multipoint from Google Fast Pair SASS: whether multipoint is on and which host is active.
/// It sends only the two get requests (<see cref="FastPairMessages.SassGetCapability"/>,
/// <see cref="FastPairMessages.SassGetConnectionStatus"/>) over the battery feature's channel and never
/// acknowledges anything. Created by <see cref="FastPairDriver"/>; it doesn't own the channel.
/// </summary>
public sealed class FastPairMultipointFeature : IMultipointFeature
{
    private const string OtherName = "Other device";

    private readonly FastPairBatteryFeature _stream;
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<SassCapability?> _capability = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _enabled;
    private bool _thisActive = true;
    private bool _otherKnown;
    private string _otherName = OtherName;

    private FastPairMultipointFeature(FastPairBatteryFeature stream) => _stream = stream;

    // Switching or toggling needs an account-key MAC this PC doesn't have (docs/protocols/fast-pair-message-stream.md).
    public MultipointOptions Options { get; } = new(MaxConnections: 2, CanSwitch: false, CanDisconnect: false, CanToggleMultipoint: false);

    public CapabilitySource Source => CapabilitySource.RuntimeQuery;

    public Verification Verification => Verification.Unverified;

    public MultipointState Current
    {
        get
        {
            lock (_gate)
            {
                return Snapshot();
            }
        }
    }

    public event EventHandler<MultipointState>? Changed;

    public Task<CommandResult> SwitchToAsync(string hostId, CancellationToken cancellationToken = default) => Unsupported();

    public Task<CommandResult> DisconnectAsync(string hostId, CancellationToken cancellationToken = default) => Unsupported();

    public Task<CommandResult> SetMultipointEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => Unsupported();

    /// <summary>Subscribes to <paramref name="stream"/> and asks for the capability; a failed send is kept, not thrown.</summary>
    internal static FastPairMultipointFeature Start(FastPairBatteryFeature stream)
    {
        var feature = new FastPairMultipointFeature(stream);
        stream.MessageReceived += feature.OnMessage;
        stream.ChannelAttached += feature.OnAttached;
        _ = feature.SendQuietly(FastPairMessages.SassGetCapability);
        return feature;
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the capability. Returns this when the buds report SASS
    /// with multipoint; otherwise stops listening and returns null. Caller cancellation propagates.
    /// </summary>
    internal async Task<FastPairMultipointFeature?> ReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expiry.CancelAfter(timeout);
        SassCapability? capability = null;
        try
        {
            capability = await _capability.Task.WaitAsync(expiry.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // No reply in time means SASS is unsupported.
        }

        if (capability is { Version: not 0 } found && (found.MultipointToggleable || found.MultipointOn))
        {
            return this;
        }

        _stream.MessageReceived -= OnMessage;
        _stream.ChannelAttached -= OnAttached;
        return null;
    }

    private static Task<CommandResult> Unsupported() =>
        Task.FromResult(CommandResult.Unsupported("These earbuds only let your phone switch audio."));

    // Caller holds _gate.
    private MultipointState Snapshot()
    {
        List<HostLink> hosts = [new("this", "This computer", HostKind.Computer, _thisActive ? HostLinkState.Active : HostLinkState.Connected, true)];
        if (_otherKnown)
        {
            hosts.Add(new("other", _otherName, HostKind.Unknown, _thisActive ? HostLinkState.Connected : HostLinkState.Active, false));
        }

        return new MultipointState(_enabled, hosts);
    }

    private void OnMessage(StreamMessage message)
    {
        var askStatus = false;
        MultipointState? changed = null;
        lock (_gate)
        {
            var before = (_enabled, _thisActive, _otherKnown, _otherName);
            if (FastPairMessages.TrySassCapability(message, out var capability))
            {
                _capability.TrySetResult(capability);
                if (capability.Version != 0)
                {
                    _enabled = capability.MultipointOn;
                    askStatus = _enabled;
                }
            }
            else if (FastPairMessages.TrySassConnectionStatus(message, out var active))
            {
                SetActive(thisActive: active == SassActiveDevice.ThisSeeker);
            }
            else if (FastPairMessages.TrySassSwitchEvent(message, out var switchEvent))
            {
                SetActive(switchEvent.ToThisDevice);
                if (!switchEvent.ToThisDevice && switchEvent.Name.Length > 0)
                {
                    _otherName = switchEvent.Name;
                }
            }

            if (before != (_enabled, _thisActive, _otherKnown, _otherName))
            {
                changed = Snapshot();
            }
        }

        if (askStatus)
        {
            _ = SendQuietly(FastPairMessages.SassGetConnectionStatus);
        }

        if (changed is not null)
        {
            Changed?.Invoke(this, changed);
        }
    }

    // Caller holds _gate. The other host only becomes known once it is the active one.
    private void SetActive(bool thisActive)
    {
        _thisActive = thisActive;
        _otherKnown |= !thisActive;
    }

    private void OnAttached() => _ = SendQuietly(FastPairMessages.SassGetCapability);

    private async Task SendQuietly(byte[] message)
    {
        try
        {
            await _stream.SendAsync(message, CancellationToken.None);
        }
        catch (Exception)
        {
            // A failed capability request means no multipoint; a failed refresh keeps the last state.
            _capability.TrySetResult(null);
        }
    }
}
