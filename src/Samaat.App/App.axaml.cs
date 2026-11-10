using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Samaat.App.Shell;
using Samaat.App.Tray;
using Samaat.Core.Diagnostics;

namespace Samaat.App;

public partial class App : Application
{
    public static AppOptions Options { get; set; } = new(Simulate: false, IncludeNonAudio: false);

    /// <summary>Set once the window exists; the single-instance guard calls it (from any thread) on a second launch.</summary>
    public static Action? ShowMainWindow { get; private set; }

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
                    SamaatLog.Warn("App", "Loading the toast icon failed", e);
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
                    SamaatLog.Warn("App", "Loading the window icon failed", e);
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
                SamaatLog.Warn("App", "The tray icon is unavailable", e);
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

            // Run once from both events: whichever Avalonia raises on this exit path.
            var cleaned = 0;
            void Cleanup()
            {
                if (Interlocked.Exchange(ref cleaned, 1) == 1)
                {
                    return;
                }

                tray?.Dispose();
                viewModel.Shutdown(TimeSpan.FromSeconds(3));
            }

            desktop.Exit += (_, _) => Cleanup();
            desktop.ShutdownRequested += (_, _) => Cleanup();

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                SamaatLog.Error("App", "Unhandled UI exception", e.Exception);
                e.Handled = true;
                try
                {
                    viewModel.ShowMessage("Something went wrong. Details are in the log (Diagnostics, Copy report).");
                }
                catch (Exception toastError)
                {
                    SamaatLog.Warn("App", "Showing the error message failed", toastError);
                }
            };

            // Raised by a second launch: bring the running window forward.
            ShowMainWindow = () => Dispatcher.UIThread.Post(() =>
            {
                if (tray is not null)
                {
                    tray.Open();
                    return;
                }

                window.Show();
                window.Activate();
            });

            // Starting hidden only makes sense when the tray can bring the window back.
            if (!(Options.Minimized && tray is not null))
            {
                desktop.MainWindow = window;
            }

            _ = SamaatLog.Guard("App", "Refreshing devices", () => viewModel.RefreshDevicesAsync());
            if (Options.TestNotification)
            {
                notifier.Show("Samaat", "Test notification");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
