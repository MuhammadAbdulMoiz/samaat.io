using Samaat.Core.Capabilities;
using Samaat.Core.Commands;
using Samaat.Core.Devices;

namespace Samaat.Core.Connections;

public enum HostKind
{
    Unknown,
    Phone,
    Computer,
    Tablet,
}

public enum HostLinkState
{
    /// <summary>Connected and currently the audio source.</summary>
    Active,

    /// <summary>Connected on standby (multipoint).</summary>
    Connected,

    /// <summary>Remembered by the earbuds but not connected.</summary>
    Paired,

    /// <summary>Remembered but out of range or switched off.</summary>
    Unavailable,
}

/// <summary>One host (phone, laptop, ...) the earbuds know about.</summary>
public sealed record HostLink(string Id, string Name, HostKind Kind, HostLinkState State, bool IsThisHost)
{
    public bool IsConnected => State is HostLinkState.Active or HostLinkState.Connected;
}

public sealed record MultipointOptions(int MaxConnections, bool CanSwitch, bool CanDisconnect, bool CanToggleMultipoint);

public sealed record MultipointState(bool MultipointEnabled, IReadOnlyList<HostLink> Hosts)
{
    public HostLink? ActiveHost => Hosts.FirstOrDefault(h => h.State == HostLinkState.Active);

    public int ConnectedCount => Hosts.Count(h => h.IsConnected);
}

/// <summary>
/// Earbud-side multipoint, available only when a vendor protocol exposes it. A phone or PC can't
/// see or switch other hosts' connections on its own (docs/PLAN.md §8).
/// </summary>
public interface IMultipointFeature : IFeature
{
    MultipointOptions Options { get; }

    CapabilitySource Source { get; }

    Verification Verification { get; }

    MultipointState Current { get; }

    event EventHandler<MultipointState>? Changed;

    /// <summary>Makes a host the active audio source, connecting it first if needed.</summary>
    Task<CommandResult> SwitchToAsync(string hostId, CancellationToken cancellationToken = default);

    Task<CommandResult> DisconnectAsync(string hostId, CancellationToken cancellationToken = default);

    Task<CommandResult> SetMultipointEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

public static class ConnectionCapabilities
{
    public static IEnumerable<Capability> For(IEarbudDevice device)
    {
        var feature = device.GetFeature<IMultipointFeature>();
        var options = feature?.Options;

        Capability Make(FeatureId id, bool supported, CapabilityAccess access) => CapabilityFactory.Create(
            id,
            hasSource: feature is not null,
            supported,
            exhaustive: feature is not null,
            device.Connection,
            feature?.Source ?? CapabilitySource.Profile,
            feature?.Verification ?? Verification.Unverified,
            access);

        yield return Make(FeatureId.ConnectedHosts, feature is not null, CapabilityAccess.ReadOnly);
        yield return Make(FeatureId.SwitchHost, options?.CanSwitch == true, CapabilityAccess.ReadWrite);
        yield return Make(FeatureId.DisconnectHost, options?.CanDisconnect == true, CapabilityAccess.ReadWrite);
        yield return Make(FeatureId.MultipointToggle, options?.CanToggleMultipoint == true, CapabilityAccess.ReadWrite);
    }
}

/// <summary>
/// Reference model of how multipoint earbuds behave. The simulator uses it; real adapters report
/// the device's own state instead. Each operation returns the next state and the command result.
/// </summary>
public static class MultipointRules
{
    public static (MultipointState State, CommandResult Result) SwitchTo(MultipointState state, MultipointOptions options, string hostId)
    {
        if (!options.CanSwitch)
        {
            return (state, CommandResult.Unsupported("This device can't switch hosts from here."));
        }

        var target = state.Hosts.FirstOrDefault(h => h.Id == hostId);
        if (target is null)
        {
            return (state, CommandResult.Rejected("That device isn't known to your earbuds."));
        }

        if (target.State == HostLinkState.Active)
        {
            return (state, CommandResult.Confirmed);
        }

        if (target.State == HostLinkState.Unavailable)
        {
            return (state, CommandResult.Rejected($"{target.Name} is out of range or switched off."));
        }

        var limit = state.MultipointEnabled ? Math.Max(1, options.MaxConnections) : 1;
        var hosts = state.Hosts.ToList();

        // The old active host stays connected on standby when multipoint allows it.
        Replace(hosts, h => h.State == HostLinkState.Active,
            h => h with { State = limit > 1 ? HostLinkState.Connected : HostLinkState.Paired });

        if (target.State != HostLinkState.Connected)
        {
            // Make room: drop standby hosts (not this one) until the target fits.
            while (hosts.Count(h => h.IsConnected) >= limit)
            {
                var drop = hosts.FindLast(h => h.State == HostLinkState.Connected && !h.IsThisHost)
                    ?? hosts.FindLast(h => h.State == HostLinkState.Connected);
                if (drop is null)
                {
                    break;
                }

                hosts[hosts.IndexOf(drop)] = drop with { State = HostLinkState.Paired };
            }
        }

        Replace(hosts, h => h.Id == hostId, h => h with { State = HostLinkState.Active });
        return (state with { Hosts = hosts }, CommandResult.Confirmed);
    }

    public static (MultipointState State, CommandResult Result) Disconnect(MultipointState state, MultipointOptions options, string hostId)
    {
        if (!options.CanDisconnect)
        {
            return (state, CommandResult.Unsupported("This device can't disconnect hosts from here."));
        }

        var target = state.Hosts.FirstOrDefault(h => h.Id == hostId);
        if (target is null || !target.IsConnected)
        {
            return (state, CommandResult.Rejected("That device isn't connected."));
        }

        if (target.IsThisHost)
        {
            return (state, CommandResult.Rejected("To disconnect this computer, use your system's Bluetooth settings."));
        }

        var hosts = state.Hosts.ToList();
        Replace(hosts, h => h.Id == hostId, h => h with { State = HostLinkState.Paired });
        if (target.State == HostLinkState.Active)
        {
            Replace(hosts, h => h.State == HostLinkState.Connected, h => h with { State = HostLinkState.Active }, firstOnly: true);
        }

        return (state with { Hosts = hosts }, CommandResult.Confirmed);
    }

    public static (MultipointState State, CommandResult Result) SetMultipointEnabled(MultipointState state, MultipointOptions options, bool enabled)
    {
        if (!options.CanToggleMultipoint)
        {
            return (state, CommandResult.Unsupported("Multipoint can't be turned on or off from here."));
        }

        var hosts = enabled
            ? state.Hosts.ToList()
            : state.Hosts.Select(h => h.State == HostLinkState.Connected ? h with { State = HostLinkState.Paired } : h).ToList();
        return (new MultipointState(enabled, hosts), CommandResult.Confirmed);
    }

    private static void Replace(List<HostLink> hosts, Func<HostLink, bool> match, Func<HostLink, HostLink> change, bool firstOnly = false)
    {
        for (var i = 0; i < hosts.Count; i++)
        {
            if (match(hosts[i]))
            {
                hosts[i] = change(hosts[i]);
                if (firstOnly)
                {
                    return;
                }
            }
        }
    }
}
