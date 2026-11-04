using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Samaat.App.Features.Details;

public partial class BluetoothDetailsPage : UserControl
{
    public BluetoothDetailsPage()
    {
        InitializeComponent();
    }

    private async void OnCopyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DetailsRow row } button && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(row.Value);
            ToolTip.SetTip(button, "Copied");
        }
    }
}
