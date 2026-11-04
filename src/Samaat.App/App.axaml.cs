using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Samaat.App.Shell;
using Samaat.App.Tray;

namespace Samaat.App;

public partial class App : Application
{
    public static AppOptions Options { get; set; } = new(Simulate: false, IncludeNonAudio: false);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The tray keeps the app alive when the window is hidden; quitting is always explicit.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            byte[]? iconPng = null;
            byte[]? IconPng()
            {
                try
                {
                    return iconPng ??= AppIcon.Png();
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"Samaat icon loading failed: {e.Message}");
                    return null;
                }
            }

            WindowIcon? LoadIcon()
            {
                try
                {
                    return AppIcon.Window();
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"Samaat icon loading failed: {e.Message}");
                    return null;
                }
            }

            var notifier = Options.CreateNotifier(IconPng);
            var viewModel = new MainWindowViewModel(Options, notifier: notifier);
            var window = new MainWindow { DataContext = viewModel };
            var icon = LoadIcon();
            window.Icon = icon;

            TrayController? tray = null;
            try
            {
                if (icon is not null)
                {
                    tray = new TrayController(this, desktop, window, viewModel, icon);
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Samaat tray icon unavailable: {e}");
            }

            window.Closing += (_, e) =>
            {
                var quitting = tray?.Quitting == true || e.CloseReason != WindowCloseReason.WindowClosing;
                // Without a tray there is nothing to restore the window from, so closing always exits.
                var action = tray is null ? CloseAction.Exit : CloseBehavior.Decide(quitting, viewModel.Settings.CloseToTray);
                if (action == CloseAction.Hide)
                {
                    e.Cancel = true;
                    window.Hide();
                }
                else if (!quitting)
                {
                    desktop.Shutdown();
                }
            };

            desktop.ShutdownRequested += async (_, _) =>
            {
                tray?.Dispose();
                await viewModel.DisposeAsync();
            };

            // Starting hidden only makes sense when the tray can bring the window back.
            if (!(Options.Minimized && tray is not null))
            {
                desktop.MainWindow = window;
            }

            _ = viewModel.RefreshDevicesAsync();
            if (Options.TestNotification)
            {
                notifier.Show("Samaat", "Test notification");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
