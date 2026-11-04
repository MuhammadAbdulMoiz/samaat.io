using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Controls;
using Samaat.App.Features.Settings;

namespace Samaat.App.Shell;

public partial class MainWindow : Window
{
    /// <summary>Below this window width the sidebar collapses to an icon rail.</summary>
    public const double CompactWidth = 900;

    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(180);

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
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ReduceMotion))
        {
            ApplyMotion();
        }
    }

    // The window class turns off value transitions in the styles; the page fade lives on the control.
    private void ApplyMotion()
    {
        var reduce = _settings?.ReduceMotion == true;
        Classes.Set("reduce-motion", reduce);
        PageHost.PageTransition = new CrossFade(reduce ? TimeSpan.Zero : FadeDuration);
    }
}
