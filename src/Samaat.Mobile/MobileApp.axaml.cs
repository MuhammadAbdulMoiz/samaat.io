using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Samaat.Mobile.Runtime;
using Samaat.Mobile.Simulation;
using Samaat.Mobile.ViewModels;
using Samaat.Mobile.Views;

namespace Samaat.Mobile;

public partial class MobileApp : Application
{
    /// <summary>Set by the platform head before Avalonia starts; defaults to the simulator.</summary>
    public static Func<MobileRuntime> RuntimeFactory { get; set; } = () => SimulatedMobile.Create();

    /// <summary>The shell's view model once the UI is up (the Android activity forwards Back to it).</summary>
    public static MainViewModel? Shell { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = CreateMainView();
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A phone-sized window, for trying the UI on a computer.
            desktop.MainWindow = new Window { Width = 412, Height = 892, Title = "Samaat (phone)", Content = CreateMainView() };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainView CreateMainView()
    {
        var viewModel = new MainViewModel(RuntimeFactory());
        Shell = viewModel;
        _ = viewModel.StartAsync();
        return new MainView { DataContext = viewModel };
    }
}
