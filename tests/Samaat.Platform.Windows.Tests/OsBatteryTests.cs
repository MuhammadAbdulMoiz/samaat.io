namespace Samaat.Platform.Windows.Tests;

/// <summary>DEVPKEY_Bluetooth_Battery as Windows reports it, mapped to a percentage.</summary>
public sealed class OsBatteryTests
{
    [Fact]
    public void Property_IsDevpkeyBluetoothBattery() =>
        Assert.Equal("{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", OsBatteryFeature.BatteryProperty);

    [Theory]
    [InlineData((byte)80, 80)]
    [InlineData((byte)0, 0)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    public void Percent_IsConvertedAndClamped(object reported, int expected)
    {
        var properties = new Dictionary<string, object> { [OsBatteryFeature.BatteryProperty] = reported };

        Assert.Equal(expected, OsBatteryFeature.ReadPercent(properties));
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData(long.MaxValue)]
    [InlineData(1e30)]
    public void Percent_IsUnknown_WhenValueIsUnreadable(object reported)
    {
        var properties = new Dictionary<string, object> { [OsBatteryFeature.BatteryProperty] = reported };

        Assert.Null(OsBatteryFeature.ReadPercent(properties));
    }

    [Fact]
    public void Percent_IsUnknown_WhenValueIsNotConvertible() =>
        Assert.Null(OsBatteryFeature.ReadPercent(new Dictionary<string, object> { [OsBatteryFeature.BatteryProperty] = new object() }));

    [Fact]
    public void Percent_IsUnknown_WhenWindowsHasNone()
    {
        Assert.Null(OsBatteryFeature.ReadPercent(new Dictionary<string, object>()));
        Assert.Null(OsBatteryFeature.ReadPercent(new Dictionary<string, object> { [OsBatteryFeature.BatteryProperty] = null! }));
    }
}
