using Avalonia.Controls;
using Avalonia.Interactivity;
using Samaat.App.Features.Overview;
using Samaat.Core.Diagnostics;

namespace Samaat.App.Features.Info;

public partial class InfoPage : UserControl
{
    public InfoPage()
    {
        InitializeComponent();
    }

    private void OnCopyReport(object? sender, RoutedEventArgs e) => _ = SamaatLog.Guard("Diagnostics", "Copying the report", async () =>
    {
        if (DataContext is DeviceViewModel { Info: { } info } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(info.BuildReport());
            CopyLabel.Text = "Copied";
        }
    });
}
