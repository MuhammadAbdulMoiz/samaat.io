namespace Samaat.Core.Settings;

/// <summary>The dashboard layout the window shows. Studio is the original layout and the default.</summary>
public enum DashboardView
{
    /// <summary>Sidebar, device rail and tabs: every control, one device at a time.</summary>
    Studio,

    /// <summary>One device, big and simple, with a few quick controls.</summary>
    Focus,

    /// <summary>Every listed device's battery at once, in a small window.</summary>
    Glance,
}

/// <summary>Which devices the sidebar and the Glance layout list.</summary>
public enum DeviceFilter
{
    /// <summary>Only devices connected right now.</summary>
    Connected,

    /// <summary>Connected now or within <see cref="DeviceFilters.RecentWindow"/>.</summary>
    Recent,

    /// <summary>Every paired device.</summary>
    Paired,
}

public static class DeviceFilters
{
    /// <summary>How long a device counts as recent after it was last connected.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);

    public static bool Includes(DeviceFilter filter, bool connected, DateTimeOffset? lastConnected, DateTimeOffset now) => filter switch
    {
        DeviceFilter.Paired => true,
        DeviceFilter.Recent => connected || (lastConnected is { } last && now - last <= RecentWindow),
        _ => connected,
    };
}
