using Samaat.Core.Battery;
using Samaat.Protocols.Standard;
using Samaat.Transport;

namespace Samaat.Protocols.Tests;

public class BatteryServiceParsingTests
{
    [Fact]
    public void Uuids_ExpandOntoBluetoothBase()
    {
        Assert.Equal(Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb"), GattUuids.BatteryService);
        Assert.Equal(Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb"), GattUuids.BatteryLevel);
    }

    [Theory]
    [InlineData(new byte[] { 0 }, 0)]
    [InlineData(new byte[] { 57 }, 57)]
    [InlineData(new byte[] { 100 }, 100)]
    public void BatteryLevel_ParsesValidByte(byte[] value, int expected)
    {
        Assert.True(BatteryLevelParser.TryParse(value, out var percent));
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 101 })]
    [InlineData(new byte[] { 0xFF })]
    public void BatteryLevel_RejectsInvalid(byte[] value) => Assert.False(BatteryLevelParser.TryParse(value, out _));

    [Fact]
    public void PresentationFormat_ParsesLittleEndianFields()
    {
        // format=uint8 (0x04), exponent=0, unit=percentage (0x27AD), namespace=SIG, description=left (0x010D)
        var format = PresentationFormat.Parse([0x04, 0x00, 0xAD, 0x27, 0x01, 0x0D, 0x01])!;

        Assert.Equal(0x04, format.Format);
        Assert.Equal(0x27AD, format.Unit);
        Assert.Equal(PresentationFormat.SigNamespace, format.Namespace);
        Assert.Equal(BatteryComponent.Left, format.Position);
    }

    [Fact]
    public void PresentationFormat_UnknownDescription_HasNoPosition() =>
        Assert.Null(PresentationFormat.Parse([0x04, 0x00, 0xAD, 0x27, 0x01, 0x00, 0x00])!.Position);

    [Fact]
    public void PresentationFormat_TooShort_IsNull() => Assert.Null(PresentationFormat.Parse([0x04, 0x00]));
}

public class GattBatteryFeatureTests
{
    private static byte[] Format(ushort description) => [0x04, 0x00, 0xAD, 0x27, 0x01, (byte)description, (byte)(description >> 8)];

    [Fact]
    public async Task NoBatteryService_ReturnsNull()
    {
        Assert.Null(await GattBatteryFeature.CreateAsync(new FakeGattClient()));
    }

    [Fact]
    public async Task SingleUnlabelledLevel_IsCombined()
    {
        var client = new FakeGattClient(new FakeService(new FakeCharacteristic(value: [72])));

        var feature = (await GattBatteryFeature.CreateAsync(client))!;

        Assert.Equal([BatteryComponent.Combined], feature.Components);
        Assert.False(feature.ComponentsAreExhaustive);
        Assert.Equal(72, feature.Current.Get(BatteryComponent.Combined)!.Percent);
    }

    [Fact]
    public async Task LabelledLevels_MapToBuds()
    {
        var client = new FakeGattClient(
            new FakeService(new FakeCharacteristic(value: [90], presentationFormat: Format(PresentationFormat.DescriptionLeft))),
            new FakeService(new FakeCharacteristic(value: [85], presentationFormat: Format(PresentationFormat.DescriptionRight))));

        var feature = (await GattBatteryFeature.CreateAsync(client))!;

        Assert.Equal(90, feature.Current.Get(BatteryComponent.Left)!.Percent);
        Assert.Equal(85, feature.Current.Get(BatteryComponent.Right)!.Percent);
        Assert.Null(feature.Current.Get(BatteryComponent.Combined));
    }

    [Fact]
    public async Task Notifications_UpdateStateAndRaiseChanged()
    {
        var characteristic = new FakeCharacteristic(value: [50]);
        var feature = (await GattBatteryFeature.CreateAsync(new FakeGattClient(new FakeService(characteristic))))!;
        BatteryState? raised = null;
        feature.Changed += (_, state) => raised = state;

        characteristic.Notify([49]);

        Assert.Equal(49, feature.Current.Get(BatteryComponent.Combined)!.Percent);
        Assert.Same(feature.Current, raised);
    }

    [Fact]
    public async Task InvalidNotification_IsIgnored()
    {
        var characteristic = new FakeCharacteristic(value: [50]);
        var feature = (await GattBatteryFeature.CreateAsync(new FakeGattClient(new FakeService(characteristic))))!;

        characteristic.Notify([0xFF]);

        Assert.Equal(50, feature.Current.Get(BatteryComponent.Combined)!.Percent);
    }

    [Fact]
    public async Task Dispose_Unsubscribes()
    {
        var characteristic = new FakeCharacteristic(value: [50]);
        var feature = (await GattBatteryFeature.CreateAsync(new FakeGattClient(new FakeService(characteristic))))!;

        await feature.DisposeAsync();

        Assert.False(characteristic.IsSubscribed);
    }
}

internal sealed class FakeGattClient(params FakeService[] services) : IGattClient
{
    public Task<IReadOnlyList<IGattService>> GetServicesAsync(Guid serviceUuid, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IGattService>>(services.Where(s => s.Uuid == serviceUuid).ToList());
}

internal sealed class FakeService(params FakeCharacteristic[] characteristics) : IGattService
{
    public Guid Uuid => GattUuids.BatteryService;

    public Task<IReadOnlyList<IGattCharacteristic>> GetCharacteristicsAsync(Guid characteristicUuid, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IGattCharacteristic>>(characteristics.Where(c => c.Uuid == characteristicUuid).ToList());
}

internal sealed class FakeCharacteristic(byte[] value, byte[]? presentationFormat = null) : IGattCharacteristic
{
    private Action<byte[]>? _subscriber;

    public Guid Uuid => GattUuids.BatteryLevel;

    public bool CanRead => true;

    public bool CanNotify => true;

    public bool IsSubscribed => _subscriber is not null;

    public Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(value);

    public Task<byte[]?> ReadDescriptorAsync(Guid descriptorUuid, CancellationToken cancellationToken = default) =>
        Task.FromResult(descriptorUuid == GattUuids.CharacteristicPresentationFormat ? presentationFormat : null);

    public Task<IAsyncDisposable> SubscribeAsync(Action<byte[]> onValue, CancellationToken cancellationToken = default)
    {
        _subscriber = onValue;
        return Task.FromResult<IAsyncDisposable>(new Unsubscriber(this));
    }

    public void Notify(byte[] newValue) => _subscriber?.Invoke(newValue);

    private sealed class Unsubscriber(FakeCharacteristic owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._subscriber = null;
            return ValueTask.CompletedTask;
        }
    }
}
