using Avalonia.Headless.XUnit;
using Samaat.App.Features.Overview;
using Samaat.Core.Battery;
using Samaat.Core.Capabilities;
using Samaat.Core.Devices;
using Samaat.Core.Profiles;
using Samaat.Simulator;

namespace Samaat.App.Tests;

public class ArtworkTests
{
    private static DeviceViewModel Create(string name, string? model = null, SimulatedBatteryLayout layout = SimulatedBatteryLayout.CombinedOnly) =>
        new(new SimulatedEarbudDevice(new DeviceIdentity("id", name, Model: model), layout), new(), TimeProvider.System, (_, _) => { });

    [AvaloniaTheory]
    [InlineData("Quantum", "Zero Lifestyle Quantum Zbuds")] // single Windows level, but the model says buds
    [InlineData("Galaxy Buds2 Pro", null)]
    [InlineData("AirPods Pro", null)]
    public void EarbudsShowBudArtwork_EvenWithOneBatteryLevel(string name, string? model)
    {
        var device = Create(name, model);

        Assert.True(device.HasBuds);
        Assert.Equal($"{name} earbuds", device.ArtworkLabel);
    }

    [AvaloniaTheory]
    [InlineData("ParadoX-E", "Audionic", "Audionic ParadoX-E", "Audionic ParadoX-E")] // maker already in the model
    [InlineData("Quantum", "Zero Lifestyle", "Quantum Zbuds", "Zero Lifestyle · Quantum Zbuds")]
    [InlineData("Microsoft Modern Wireless Headset", "Microsoft", "Microsoft Modern Wireless Headset", "")] // all repeat the title
    [InlineData("SHP-9500", "HP", "SHP-9500", "HP")] // "HP" inside "SHP" is not the maker's name
    public void SubtitleDoesNotRepeatItself(string name, string manufacturer, string model, string expected)
    {
        var device = new DeviceViewModel(
            new SimulatedEarbudDevice(new DeviceIdentity("id", name, manufacturer, model), SimulatedBatteryLayout.CombinedOnly),
            new(), TimeProvider.System, (_, _) => { });

        Assert.Equal(expected, device.Subtitle);
    }

    private sealed class BudsBattery(params BatteryReading[] readings) : IBatteryFeature
    {
        public IReadOnlySet<BatteryComponent> Components { get; } = readings.Select(r => r.Component).ToHashSet();
        public bool ComponentsAreExhaustive => true;
        public CapabilitySource Source => CapabilitySource.RuntimeQuery;
        public Verification Verification => Verification.Unverified;
        public BatteryState Current { get; private set; } = BatteryState.Empty.With(readings);
        public event EventHandler<BatteryState>? Changed;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Publish(params BatteryReading[] next)
        {
            Current = BatteryState.Empty.With(next);
            Changed?.Invoke(this, Current);
        }
    }

    private sealed class Device(IBatteryFeature battery) : IEarbudDevice
    {
        public DeviceIdentity Identity { get; } = new("id", "ParadoX-E");
        public ConnectionState Connection => ConnectionState.Connected;
        public IReadOnlyCollection<IFeature> Features { get; } = [battery];
        public event EventHandler? ConnectionChanged { add { } remove { } }
    }

    // Any real PNG will do; the shipped photo is always there.
    private static readonly string Photo = Path.Combine(RepoRoot(), "devices", "audionic", "paradox-e.png");

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root!.FullName, "Samaat.slnx")))
        {
            root = root.Parent;
        }

        return root.FullName;
    }

    private static DeviceViewModel CreateWithParts(IBatteryFeature battery, bool partImages = true) =>
        new(new Device(battery), new(), TimeProvider.System, (_, _) => { },
            new DeviceProfile("audionic.paradox-e", "Audionic ParadoX-E", new ProfileMatch(null, null, [], null), "jieli-rcsp", null, [], null,
                Image: Photo, PartImages: partImages ? new ProfilePartImages(Photo, Photo, Photo) : null));

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [AvaloniaFact]
    public void PartImages_WithPerBudBattery_ShowEachPieceWithItsOwnLevel()
    {
        var device = CreateWithParts(new BudsBattery(
            new BatteryReading(BatteryComponent.Left, 80, Now, ChargingState.Charging),
            new BatteryReading(BatteryComponent.Right, null, Now),
            new BatteryReading(BatteryComponent.Case, 40, Now, ChargingState.NotCharging)));

        Assert.True(device.ShowArtworkParts);
        Assert.Equal(["Left bud", "Right bud", "Case"], device.ArtworkParts.Select(p => p.Figure.Label));
        var (left, right, @case) = (device.ArtworkParts[0].Figure, device.ArtworkParts[1].Figure, device.ArtworkParts[2].Figure);
        Assert.Equal(("80", true, "Left bud, 80 percent, charging"), (left.Value, left.IsCharging, left.AccessibleText));
        Assert.Equal(("–", false, "Right bud, no reading"), (right.Value, right.HasLevel, right.AccessibleText));
        Assert.Equal(("40", false, "Case, 40 percent"), (@case.Value, @case.IsCharging, @case.AccessibleText));
    }

    [AvaloniaFact]
    public void PartImages_AppearOnceTheBudsReportALevel()
    {
        var battery = new BudsBattery(
            new BatteryReading(BatteryComponent.Left, null, Now), new BatteryReading(BatteryComponent.Right, null, Now),
            new BatteryReading(BatteryComponent.Case, null, Now));
        var device = CreateWithParts(battery);
        Assert.Equal("Case, no reading", device.ArtworkParts[2].Figure.AccessibleText);

        battery.Publish(new BatteryReading(BatteryComponent.Left, 50, Now), new BatteryReading(BatteryComponent.Right, 60, Now, ChargingState.Charging),
            new BatteryReading(BatteryComponent.Case, 30, Now, ChargingState.Charging));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(device.ShowArtworkParts);
        Assert.Equal("Right bud, 60 percent, charging", device.ArtworkParts[1].Figure.AccessibleText);
        Assert.True(device.ArtworkParts[2].Figure.IsCharging);
    }

    [AvaloniaFact]
    public void FullyChargedPiece_SaysSo()
    {
        var device = CreateWithParts(new BudsBattery(
            new BatteryReading(BatteryComponent.Left, 100, Now, ChargingState.Full), new BatteryReading(BatteryComponent.Right, 90, Now)));

        Assert.Equal("Left bud, 100 percent, fully charged", device.ArtworkParts[0].Figure.AccessibleText);
    }

    [AvaloniaFact]
    public void WithoutPartImages_FallsBackToTheSinglePhoto()
    {
        var device = CreateWithParts(new BudsBattery(new BatteryReading(BatteryComponent.Left, 80, Now)), partImages: false);

        Assert.False(device.ShowArtworkParts);
        Assert.Empty(device.ArtworkParts);
        Assert.True(device.HasArtworkImage);
    }

    [AvaloniaFact]
    public void PartImages_WithOnlyACombinedLevel_FallBackToTheSinglePhoto()
    {
        var device = CreateWithParts(new BudsBattery(new BatteryReading(BatteryComponent.Combined, 60, Now)));

        Assert.False(device.ShowArtworkParts);
        Assert.True(device.HasArtworkImage);
    }

    [AvaloniaFact]
    public void HeadsetShowsHeadsetArtwork()
    {
        var device = Create("Microsoft Modern Wireless Headset");

        Assert.False(device.HasBuds);
        Assert.Equal("Microsoft Modern Wireless Headset headset", device.ArtworkLabel);
    }
}
