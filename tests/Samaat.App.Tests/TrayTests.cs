using Samaat.App.Tray;

namespace Samaat.App.Tests;

public class TrayTests
{
    [Fact]
    public void Summary_NoDevices() => Assert.Equal("Samaat — no devices", TraySummary.Text([]));

    [Fact]
    public void Summary_OneDevice() =>
        Assert.Equal("Samaat — Quantum 90%", TraySummary.Text([new("Quantum", "90%", true)]));

    [Fact]
    public void Summary_SeveralDevicesAreJoined() =>
        Assert.Equal("Samaat — Quantum 90% · Buds Lite 17%",
            TraySummary.Text([new("Quantum", "90%", true), new("Buds Lite", "17%", true)]));

    [Fact]
    public void Summary_TruncatesToTooltipLimit()
    {
        var many = Enumerable.Range(0, 30).Select(i => new TrayDevice($"Headphones number {i}", "50%", true)).ToList();

        var text = TraySummary.Text(many);

        Assert.Equal(127, text.Length);
        Assert.EndsWith("…", text);
        Assert.StartsWith("Samaat — Headphones number 0 50%", text);
    }

    [Fact]
    public void Summary_ExactlyAtLimitIsNotTruncated()
    {
        var name = new string('x', 127 - "Samaat — ".Length - " 5%".Length);

        var text = TraySummary.Text([new(name, "5%", true)]);

        Assert.Equal(127, text.Length);
        Assert.DoesNotContain("…", text);
    }

    [Theory]
    [InlineData(true, "Quantum · 90% · Connected")]
    [InlineData(false, "Quantum · 90% · Disconnected")]
    public void MenuLine_ShowsConnection(bool connected, string expected) =>
        Assert.Equal(expected, TraySummary.MenuLine(new("Quantum", "90%", connected)));

    [Theory]
    [InlineData(false, false, CloseAction.Exit)]
    [InlineData(false, true, CloseAction.Hide)]
    [InlineData(true, true, CloseAction.Exit)]
    [InlineData(true, false, CloseAction.Exit)]
    public void CloseBehavior_Decides(bool quitting, bool closeToTray, CloseAction expected) =>
        Assert.Equal(expected, CloseBehavior.Decide(quitting, closeToTray));
}
