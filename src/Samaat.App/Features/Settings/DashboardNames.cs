using Samaat.Core.Settings;

namespace Samaat.App.Features.Settings;

/// <summary>The words the layout and device-filter switchers use, kept in one place.</summary>
public static class DashboardNames
{
    public static string Name(DashboardView view) => view switch
    {
        DashboardView.Focus => "Focus",
        DashboardView.Glance => "Glance",
        _ => "Studio",
    };

    public static string Description(DashboardView view) => view switch
    {
        DashboardView.Focus => "One device, big and simple",
        DashboardView.Glance => "Every device's battery, in a small window",
        _ => "Every control, one device at a time",
    };

    /// <summary>The heading over the device list.</summary>
    public static string ListTitle(DeviceFilter filter) => filter switch
    {
        DeviceFilter.Recent => "Recent devices",
        DeviceFilter.Paired => "Paired devices",
        _ => "Connected devices",
    };

    /// <summary>The filter's name in its menu and on its button.</summary>
    public static string FilterName(DeviceFilter filter) => filter switch
    {
        DeviceFilter.Recent => "Recent",
        DeviceFilter.Paired => "All paired",
        _ => "Connected",
    };

    /// <summary>What the list says when the filter leaves it empty.</summary>
    public static string EmptyListText(DeviceFilter filter) => filter switch
    {
        DeviceFilter.Recent => "Nothing connected in the last 7 days.",
        _ => "No earbuds are connected right now.",
    };
}
