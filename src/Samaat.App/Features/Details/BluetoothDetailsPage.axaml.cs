using Avalonia.Controls;
using Avalonia.Interactivity;
using Samaat.Core.Diagnostics;

namespace Samaat.App.Features.Details;

public partial class BluetoothDetailsPage : UserControl
{
    public BluetoothDetailsPage()
    {
        InitializeComponent();
    }

    private void OnCopyRow(object? sender, RoutedEventArgs e) => _ = SamaatLog.Guard("Bluetooth details", "Copying a value", async () =>
    {
        if (sender is Button { DataContext: DetailsRow row } button && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(row.Value);
            ToolTip.SetTip(button, "Copied");
        }
    });
}
