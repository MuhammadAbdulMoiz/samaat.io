namespace Samaat.App.Tray;

public sealed record TrayDevice(string Name, string SummaryText, bool Connected);

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
}
