using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Samaat.Mobile.ViewModels;

namespace Samaat.Mobile.Views;

public partial class MainView : UserControl
{
    private TopLevel? _topLevel;

    public MainView() => InitializeComponent();

    // Android's back gesture goes back from a device page or tab before it leaves the app.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.BackRequested += OnBackRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.BackRequested -= OnBackRequested;
            _topLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.GoBack())
        {
            e.Handled = true;
        }
    }
}
