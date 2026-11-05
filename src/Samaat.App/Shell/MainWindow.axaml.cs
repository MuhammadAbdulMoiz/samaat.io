using System.ComponentModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Samaat.App.Features.Settings;
using Samaat.Core.Settings;

namespace Samaat.App.Shell;

public partial class MainWindow : Window
{
    /// <summary>Below this window width the sidebar collapses to an icon rail.</summary>
    public const double CompactWidth = 900;

    /// <summary>The window's minimum width; Glance is built to work in a smaller window.</summary>
    public const double StandardMinWidth = 640;

    public const double GlanceMinWidth = 400;


    private SettingsViewModel? _settings;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateCompact();
        Opened += (_, _) => UpdateCompact();
        DataContextChanged += (_, _) => Attach();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void UpdateCompact()
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.SetWindowNarrow(Bounds.Width < CompactWidth);
        }
    }

    private void Attach()
    {
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsChanged;
        }

        _settings = ViewModel?.Settings;
        if (_settings is not null)
        {
            _settings.PropertyChanged += OnSettingsChanged;
        }

        UpdateCompact();
        ApplyMotion();
        ApplyLayout();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ReduceMotion))
        {
            ApplyMotion();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.Dashboard))
        {
            ApplyLayout();
            // Let the bindings show the new layout first, then rescue keyboard focus if it was left on a hidden control.
            Dispatcher.UIThread.Post(KeepFocus, DispatcherPriority.Loaded);
        }
    }

    // The window class turns off value glides, press feedback and the busy-bar sweep in the styles (Gauges.axaml).
    private void ApplyMotion() => Classes.Set("reduce-motion", _settings?.ReduceMotion == true);

    private void ApplyLayout() =>
        MinWidth = _settings?.Dashboard == DashboardView.Glance ? GlanceMinWidth : StandardMinWidth;

    /// <summary>After a layout switch, focus moves to the new layout when the focused control was hidden by it.</summary>
    private void KeepFocus()
    {
        if (FocusManager?.GetFocusedElement() is Visual { IsEffectivelyVisible: true })
        {
            return;
        }

        Control? root = _settings?.Dashboard switch
        {
            DashboardView.Focus => FocusLayout,
            DashboardView.Glance => GlanceLayout,
            _ => DeviceLayout,
        };
        var target = root?.GetVisualDescendants().OfType<InputElement>()
            .FirstOrDefault(e => e.Focusable && e.IsEffectivelyVisible && e.IsEffectivelyEnabled && e is not ScrollViewer);
        // Unspecified: no focus ring after a mouse switch; Tab still continues from here.
        (target ?? (InputElement)LayoutButton).Focus(NavigationMethod.Unspecified);
    }
}
