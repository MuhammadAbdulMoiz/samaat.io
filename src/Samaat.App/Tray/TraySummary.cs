namespace Samaat.App.Tray;

public sealed record TrayDevice(string Name, string SummaryText, bool Connected, bool HasLink = false);

public static class TraySummary
{
    /// <summary>Windows cuts notification-area tooltips at 127 characters.</summary>
    public const int MaxLength = 127;

    public static string Text(IEnumerable<TrayDevice> devices)
    {
        var parts = devices.Select(d => $"{d.Name} {d.SummaryText}").ToList();
        var text = "Samaat — " + (parts.Count == 0 ? "no devices" : string.Join(" · ", parts));
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }

    public static string MenuLine(TrayDevice device) =>
        $"{device.Name} · {device.SummaryText} · {(device.Connected ? "Connected" : "Disconnected")}";

    /// <summary>The one connect-or-disconnect menu item for a device; null when the device has no link feature.</summary>
    public static string? LinkItem(TrayDevice device) =>
        !device.HasLink ? null : device.Connected ? $"Disconnect {device.Name}" : $"Connect {device.Name}";

    /// <summary>The tray's way into pairing; only offered when the phone handoff ships (<see cref="AppOptions.PhoneHandoff"/>).</summary>
    public const string PairPhoneItem = "Pair a phone…";

    /// <summary>The app-level menu items below the devices, in order.</summary>
    public static IReadOnlyList<string> AppItems(bool phoneHandoff) =>
        phoneHandoff ? ["Open Samaat", PairPhoneItem, "Refresh devices", "Quit"] : ["Open Samaat", "Refresh devices", "Quit"];
}
