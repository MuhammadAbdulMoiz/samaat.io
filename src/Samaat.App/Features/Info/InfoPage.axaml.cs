using Avalonia.Controls;
using Avalonia.Interactivity;
using Samaat.App.Features.Overview;

namespace Samaat.App.Features.Info;

public partial class InfoPage : UserControl
{
    public InfoPage()
    {
        InitializeComponent();
    }

    private async void OnCopyReport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceViewModel { Info: { } info } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(info.BuildReport());
            CopyLabel.Text = "Copied";
        }
    }
}
