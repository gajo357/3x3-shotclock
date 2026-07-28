using ThreeByThree.Centar.Scoreboard.Domain;

namespace ThreeByThree.Centar.Scoreboard.Domain.Tests;

[TestClass]
public sealed class ClockDisplayFormatterTests
{
    [TestMethod]
    [DataRow(600_000, "10:00")]
    [DataRow(60_001, "01:01")]
    [DataRow(59_900, "00:59.9")]
    [DataRow(1, "00:00.1")]
    [DataRow(0, "00:00.0")]
    [DataRow(-100, "00:00.0")]
    public void FormatGameClock_BoundaryRemainingTime_ReturnsCountdownDisplay(
        int remainingMilliseconds,
        string expected)
    {
        var result = ClockDisplayFormatter.FormatGameClock(
            TimeSpan.FromMilliseconds(remainingMilliseconds),
            TimeSpan.FromMinutes(1));

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow(12_000, "12")]
    [DataRow(5_000, "05")]
    [DataRow(4_901, "5.0")]
    [DataRow(4_900, "4.9")]
    [DataRow(1, "0.1")]
    [DataRow(0, "0.0")]
    [DataRow(-100, "0.0")]
    public void FormatShotClock_BoundaryRemainingTime_ReturnsCountdownDisplay(
        int remainingMilliseconds,
        string expected)
    {
        var result = ClockDisplayFormatter.FormatShotClock(
            TimeSpan.FromMilliseconds(remainingMilliseconds),
            TimeSpan.FromSeconds(5));

        Assert.AreEqual(expected, result);
    }
}
