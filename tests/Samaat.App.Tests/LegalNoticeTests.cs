using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Samaat.App.Features.Settings;
using Samaat.App.Shell;
using Samaat.Core.Battery;
using Samaat.Core.Settings;

namespace Samaat.App.Tests;

/// <summary>
/// GPL-3.0 §0 and §5(d): an interactive program must show "Appropriate Legal Notices" — that it is free software
/// under the GPL, that there is no warranty, and where to read the licence. Samaat shows them in Settings → About.
/// </summary>
public sealed class LegalNoticeTests
{
    private static string TempStore() =>
        Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"), "settings.json");

    [AvaloniaFact]
    public void SettingsViewModel_ExposesTheLicenceNotice_AndItsLinks()
    {
        var vm = new SettingsViewModel(new SettingsStore(TempStore()), new LowBatteryDetector(), new FakeStartup());

        Assert.Equal("Free software under the GNU GPL v3 or later. No warranty.", vm.LicenceNotice);
        Assert.Equal(new Uri("https://www.gnu.org/licenses/gpl-3.0.html"), vm.LicenceUri);
        Assert.Equal(new Uri("https://github.com/MuhammadAbdulMoiz/samaat.io"), vm.SourceUri);
    }

    [AvaloniaFact]
    public async Task SettingsAbout_ShowsTheLicenceNotice_WithLinksToTheLicenceAndTheSource()
    {
        var vm = new MainWindowViewModel(new AppOptions(Simulate: true, IncludeNonAudio: false), settings: new SettingsStore(TempStore()));
        var window = new MainWindow { DataContext = vm, Width = 1180 };
        window.Show();
        vm.NavigateCommand.Execute(AppSection.Settings);
        for (var i = 0; i < 60; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        var links = window.GetVisualDescendants().OfType<HyperlinkButton>().Select(b => b.NavigateUri).ToList();

        Assert.Contains("Free software under the GNU GPL v3 or later. No warranty.", texts);
        Assert.Contains(new Uri("https://www.gnu.org/licenses/gpl-3.0.html"), links);
        Assert.Contains(new Uri("https://github.com/MuhammadAbdulMoiz/samaat.io"), links);

        window.Close();
        await vm.DisposeAsync();
    }
}
