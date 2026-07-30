using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Tests.Overlay;

[TestClass]
public sealed class ScoreboardOverlayStateTests
{
    [TestMethod]
    [DataRow(MatchStage.Regular, "00:12.4", DisplayName = "Regular clock uses tenths")]
    [DataRow(MatchStage.Overtime, "OT", DisplayName = "Overtime uses OT label")]
    public void FromSnapshot_CombinedMatchState_MapsEveryOverlayField(
        MatchStage stage,
        string expectedGameClock)
    {
        var snapshot = MatchState.Empty with
        {
            Stage = stage,
            Home = new TeamState
            {
                Name = "Leotar",
                Score = 14,
                Fouls = 7,
            },
            Away = new TeamState
            {
                Name = "Trebinje",
                Score = 12,
                Fouls = 10,
            },
            GameClock = new ClockState
            {
                Remaining = TimeSpan.FromSeconds(12.31),
                IsRunning = true,
            },
            ShotClock = new ClockState
            {
                Remaining = TimeSpan.FromSeconds(4.11),
                IsRunning = true,
            },
        };

        var result = ScoreboardOverlayState.FromSnapshot(snapshot);

        Assert.AreEqual("Leotar", result.HomeTeam);
        Assert.AreEqual("Trebinje", result.AwayTeam);
        Assert.AreEqual(14, result.HomeScore);
        Assert.AreEqual(12, result.AwayScore);
        Assert.AreEqual(7, result.HomeFouls);
        Assert.AreEqual(10, result.AwayFouls);
        Assert.AreEqual(expectedGameClock, result.GameClock);
        Assert.AreEqual("4.2", result.ShotClock);
        Assert.IsTrue(result.GameClockRunning);
        Assert.IsTrue(result.ShotClockRunning);
    }
}
