using System.Runtime.InteropServices;

namespace Samaat.Platform.Windows.Tests;

/// <summary>The one-shot KSPROPERTY Windows Settings sends to a Bluetooth audio filter to connect or disconnect it.</summary>
public sealed class KsPropertyTests
{
    [Theory]
    [InlineData(BluetoothAudioLink.OneShotReconnect)]
    [InlineData(BluetoothAudioLink.OneShotDisconnect)]
    public void Request_IsAGetOnTheBtAudioSet(uint property)
    {
        var request = KsProperty.BtAudio(property);

        Assert.Equal(new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D"), request.Set);
        Assert.Equal(property, request.Id);
        Assert.Equal(KsProperty.TypeGet, request.Flags);
    }

    [Fact]
    public void OneShots_AreReconnectZeroAndDisconnectOne()
    {
        Assert.Equal(0u, BluetoothAudioLink.OneShotReconnect);
        Assert.Equal(1u, BluetoothAudioLink.OneShotDisconnect);
    }

    [Fact]
    public void Request_HasKspropertyLayout_GuidThenTwoUlongs()
    {
        Assert.Equal(24, Marshal.SizeOf<KsProperty.Request>());
        Assert.Equal(16, Marshal.OffsetOf<KsProperty.Request>(nameof(KsProperty.Request.Id)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<KsProperty.Request>(nameof(KsProperty.Request.Flags)).ToInt32());
    }

    [Fact]
    public void Ioctl_IsCtlCodeOfFileDeviceKs_MethodNeither()
    {
        const uint fileDeviceKs = 0x2F, methodNeither = 3, fileAnyAccess = 0, function = 0;

        Assert.Equal((fileDeviceKs << 16) | (fileAnyAccess << 14) | (function << 2) | methodNeither, KsProperty.Ioctl);
    }

    [Theory]
    [InlineData(5, unchecked((int)0x80070005))] // ERROR_ACCESS_DENIED
    [InlineData(2, unchecked((int)0x80070002))] // ERROR_FILE_NOT_FOUND
    [InlineData(0, unchecked((int)0x80004005))] // no error code: E_FAIL rather than success
    [InlineData(-1, unchecked((int)0x80004005))]
    public void Win32Errors_BecomeFailureHResults(int error, int expected) =>
        Assert.Equal(expected, KsProperty.HResultFromWin32(error));
}
