using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Samaat.App.Shell;

namespace Samaat.App;

public partial class App : Application
{
    public static AppOptions Options { get; set; } = new(Simulate: false, IncludeNonAudio: false);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(Options);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += async (_, _) => await viewModel.DisposeAsync();
            _ = viewModel.RefreshDevicesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
