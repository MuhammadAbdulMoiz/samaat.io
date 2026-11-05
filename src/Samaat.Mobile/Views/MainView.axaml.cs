using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Samaat.Mobile.ViewModels;

namespace Samaat.Mobile.Views;

public partial class MainView : UserControl
{
    private static readonly Thickness NavBarPadding = new(8, 6);

    private TopLevel? _topLevel;
    private IInsetsManager? _insets;

    public MainView() => InitializeComponent();

    // Android's back gesture goes back from a device page or tab before it leaves the app.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.BackRequested += OnBackRequested;

            // Android 15+ draws apps edge to edge: the ink background runs under the system bars and the content keeps
            // clear of them, the navigation bar's surface reaching down under the gesture bar.
            _insets = _topLevel.InsetsManager;
            if (_insets is not null)
            {
                _insets.DisplayEdgeToEdgePreference = true;
                _insets.SafeAreaChanged += OnSafeAreaChanged;
                ApplySafeArea(_insets.SafeAreaPadding);
            }
        }
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplySafeArea(e.SafeAreaPadding);

    private void ApplySafeArea(Thickness safe)
    {
        Root.Margin = new Thickness(safe.Left, safe.Top, safe.Right, 0);
        NavBar.Padding = new Thickness(NavBarPadding.Left, NavBarPadding.Top, NavBarPadding.Right, NavBarPadding.Bottom + safe.Bottom);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_insets is not null)
        {
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
            _insets = null;
        }

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
