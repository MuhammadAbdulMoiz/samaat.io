namespace Samaat.Core.Bluetooth;

public static class BluetoothVendors
{
    // shortcut: a short list of Bluetooth SIG company IDs seen on audio devices; extend as hardware turns up.
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x0006] = "Microsoft",
        [0x000A] = "Qualcomm (CSR)",
        [0x004C] = "Apple",
        [0x005D] = "Realtek",
        [0x0067] = "Jabra (GN)",
        [0x0075] = "Samsung",
        [0x009E] = "Bose",
        [0x00D7] = "Qualcomm",
        [0x00E0] = "Google",
        [0x012D] = "Sony",
        [0x05D6] = "Jieli (Zhuhai Jieli Technology)",
    };

    public static string? Name(int vendorId) => Names.GetValueOrDefault(vendorId);
}
