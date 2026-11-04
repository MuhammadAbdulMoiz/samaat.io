using Avalonia.Headless.XUnit;
using Samaat.App.Features.Overview;
using Samaat.App.Features.Settings;
using Samaat.Core.Battery;
using Samaat.Core.Profiles;
using Samaat.Core.Settings;
using Samaat.Simulator;
using Samaat.Core.Devices;

namespace Samaat.App.Tests;

public sealed class LookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static readonly string Photo = Path.Combine(AppContext.BaseDirectory, "devices", "audionic", "paradox-e.png");

    private static DeviceProfile Profile(string id, string name, string? image = null, bool parts = false) =>
        new(id, name, new ProfileMatch(null, null, [], null), null, null, [], null,
            Image: image, PartImages: parts ? new ProfilePartImages(Photo, Photo, Photo) : null);

    private static readonly DeviceProfile Matched = Profile("a", "Alpha Buds", Photo);
    private static readonly DeviceProfile Other = Profile("b", "Beta Buds", Photo);
    private static readonly DeviceProfile OtherWithParts = Profile("c", "Gamma Buds", Photo, parts: true);
    private static readonly DeviceProfile NoPhoto = Profile("d", "Delta");
    private static readonly IReadOnlyList<DeviceProfile> All = [Matched, Other, OtherWithParts, NoPhoto];

    [Fact]
    public void ResolveLook_Auto_WithPhoto_UsesPhoto()
    {
        var look = DeviceViewModel.ResolveLook(null, Matched, All, reportsBuds: true, looksLikeEarbuds: true);
        Assert.Equal((LookKind.Photo, Matched), (look.Kind, look.Profile));
    }

    [Fact]
    public void ResolveLook_Auto_NoPhoto_BudsReported_UnknownPieces() =>
        Assert.Equal(LookKind.UnknownPieces, DeviceViewModel.ResolveLook(null, NoPhoto, All, true, true).Kind);

    [Fact]
    public void ResolveLook_Auto_NoPhoto_Combined_UnknownSingle()
    {
        Assert.Equal(LookKind.UnknownSingle, DeviceViewModel.ResolveLook(null, null, All, false, false).Kind);
        Assert.Equal(LookKind.UnknownSingle, DeviceViewModel.ResolveLook("auto", null, All, false, true).Kind);
    }

    [Fact]
    public void ResolveLook_ChosenProfile_UsesThatPhoto()
    {
        var look = DeviceViewModel.ResolveLook("b", Matched, All, reportsBuds: true, looksLikeEarbuds: true);
        Assert.Equal((LookKind.Photo, Other), (look.Kind, look.Profile));
    }

    [Fact]
    public void ResolveLook_ChosenProfileWithParts_AndBuds_UsesParts()
    {
        Assert.Equal(LookKind.PartsPhoto, DeviceViewModel.ResolveLook("c", null, All, true, true).Kind);
        Assert.Equal(LookKind.Photo, DeviceViewModel.ResolveLook("c", null, All, false, true).Kind); // combined battery: the single photo
    }

    [Fact]
    public void ResolveLook_UnknownChoices_PickTheUnknownLooks()
    {
        Assert.Equal(LookKind.UnknownSingle, DeviceViewModel.ResolveLook("unknown.single", Matched, All, true, true).Kind);
        Assert.Equal(LookKind.UnknownPieces, DeviceViewModel.ResolveLook("unknown.pieces", Matched, All, false, false).Kind);
    }

    [Fact]
    public void ResolveLook_UnknownProfileId_FallsBackToAuto()
    {
        var look = DeviceViewModel.ResolveLook("removed", Matched, All, true, true);
        Assert.Equal((LookKind.Photo, Matched), (look.Kind, look.Profile));
        Assert.Equal(LookKind.UnknownSingle, DeviceViewModel.ResolveLook("removed", null, All, false, false).Kind);
    }

    [Fact]
    public void ResolveLook_ChosenProfileWithoutPhoto_FallsBackToAuto() =>
        Assert.Equal(LookKind.UnknownSingle, DeviceViewModel.ResolveLook("d", null, All, false, false).Kind);

    [Fact]
    public void ResolveLook_PartsWithoutFullPhoto_AreNotUsed()
    {
        var partsOnly = Profile("p", "Parts only", image: null, parts: true);
        Assert.Equal(LookKind.UnknownPieces, DeviceViewModel.ResolveLook(null, partsOnly, [partsOnly], true, true).Kind);
    }

    private SettingsViewModel Settings() =>
        new(new SettingsStore(Path.Combine(_dir, "settings.json")), new LowBatteryDetector(), new FakeStartup());

    private static DeviceViewModel Create(SettingsViewModel? settings, DeviceProfile? matched = null,
        SimulatedBatteryLayout layout = SimulatedBatteryLayout.CombinedOnly) =>
        new(new SimulatedEarbudDevice(new DeviceIdentity("id", "Some Device"), layout), new(), TimeProvider.System, (_, _) => { },
            matched, All, settings);

    [AvaloniaFact]
    public void ChangeLook_SavesChoice_AndUpdatesArtwork()
    {
        var settings = Settings();
        var device = Create(settings);
        Assert.True(device.ShowUnknownSingle);
        Assert.False(device.HasArtworkImage);

        device.ChangeLookCommand.Execute("b");

        Assert.True(device.HasArtworkImage);
        Assert.False(device.ShowUnknownSingle);
        Assert.Equal("b", settings.GetArtworkChoice("id"));
        Assert.Equal("b", new SettingsStore(Path.Combine(_dir, "settings.json")).Load().ArtworkChoices["id"]);

        device.ChangeLookCommand.Execute("unknown.pieces");
        Assert.True(device.ShowUnknownPieces);
        Assert.False(device.HasArtworkImage);

        device.ChangeLookCommand.Execute("auto");
        Assert.Null(settings.GetArtworkChoice("id"));
        Assert.True(device.ShowUnknownSingle);
    }

    [AvaloniaFact]
    public void SavedChoice_IsUsedWhenTheDeviceAppears()
    {
        var settings = Settings();
        settings.SetArtworkChoice("id", "c");

        var device = Create(settings, layout: SimulatedBatteryLayout.BudsAndCase);

        Assert.True(device.ShowArtworkParts);
        Assert.Equal(3, device.ArtworkParts.Count);
    }

    [AvaloniaFact]
    public void LookOptions_ListAutoUnknownsAndPhotoProfiles_WithCurrentChecked()
    {
        var device = Create(Settings(), Matched);

        Assert.Equal(["Automatic", "Unknown · single", "Unknown · separate pieces", "Alpha Buds", "Beta Buds", "Gamma Buds"],
            device.LookOptions.Select(o => o.Label)); // "Delta" has no photo
        Assert.Equal(["auto"], device.LookOptions.Where(o => o.IsSelected).Select(o => o.Key));

        device.ChangeLookCommand.Execute("b");
        Assert.Equal(["b"], device.LookOptions.Where(o => o.IsSelected).Select(o => o.Key));
    }

    [AvaloniaFact]
    public void ArtworkChoices_RoundTripAndNullSafe()
    {
        var settings = Settings();
        settings.SetArtworkChoice("x", "unknown.single");
        Assert.Equal("unknown.single", new SettingsStore(Path.Combine(_dir, "settings.json")).Load().ArtworkChoices["x"]);
        settings.SetArtworkChoice("x", null);
        Assert.Empty(new SettingsStore(Path.Combine(_dir, "settings.json")).Load().ArtworkChoices);
        Assert.Empty((new AppSettings { ArtworkChoices = null! }).Normalize().ArtworkChoices);
    }
}
