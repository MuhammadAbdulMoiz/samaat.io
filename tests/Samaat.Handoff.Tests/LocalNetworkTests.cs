using System.Net;

namespace Samaat.Handoff.Tests;

public sealed class LocalNetworkTests
{
    private static IReadOnlyList<(IPAddress Address, IPAddress Mask)> Nic(string address, string mask) =>
        [(IPAddress.Parse(address), IPAddress.Parse(mask))];

    [Theory]
    [InlineData("192.168.1.77", "192.168.1.20", "255.255.255.0", true)]
    [InlineData("192.168.2.77", "192.168.1.20", "255.255.255.0", false)]
    [InlineData("8.8.8.8", "192.168.1.20", "255.255.255.0", false)]
    [InlineData("10.1.200.9", "10.1.3.4", "255.255.0.0", true)]
    [InlineData("10.2.0.1", "10.1.3.4", "255.255.0.0", false)]
    [InlineData("10.0.0.2", "10.0.0.1", "255.255.255.252", true)]
    [InlineData("10.0.0.4", "10.0.0.1", "255.255.255.252", false)]
    [InlineData("8.8.8.8", "192.168.1.20", "0.0.0.0", false)]
    [InlineData("::ffff:192.168.1.77", "192.168.1.20", "255.255.255.0", true)]
    [InlineData("::ffff:8.8.8.8", "192.168.1.20", "255.255.255.0", false)]
    [InlineData("2001:4860:4860::8888", "192.168.1.20", "255.255.255.0", false)]
    public void IsOnLink_ComparesTheSubnetOfEachInterface(string remote, string address, string mask, bool expected) =>
        Assert.Equal(expected, LocalNetwork.IsOnLink(IPAddress.Parse(remote), Nic(address, mask)));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void IsOnLink_AcceptsLoopback_EvenWithNoInterfaces(string remote) =>
        Assert.True(LocalNetwork.IsOnLink(IPAddress.Parse(remote), []));

    [Fact]
    public void IsOnLink_IgnoresIpv6InterfaceEntries() =>
        Assert.False(LocalNetwork.IsOnLink(IPAddress.Parse("192.168.1.5"), [(IPAddress.Parse("fe80::1"), IPAddress.Parse("ffff:ffff:ffff:ffff::"))]));

    [Fact]
    public void IsLocal_AcceptsLoopback() => Assert.True(LocalNetwork.IsLocal(IPAddress.Loopback));
}
