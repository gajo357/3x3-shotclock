using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Tests;

[TestClass]
public sealed class MatchStateTests
{
    [TestMethod]
    [DataRow(MatchStage.Setup, false, false, MatchStatus.Setup)]
    [DataRow(MatchStage.Regular, false, false, MatchStatus.Ready)]
    [DataRow(MatchStage.Regular, false, true, MatchStatus.Live)]
    [DataRow(MatchStage.Regular, true, false, MatchStatus.Paused)]
    [DataRow(MatchStage.Overtime, true, false, MatchStatus.Overtime)]
    [DataRow(MatchStage.Final, true, false, MatchStatus.Final)]
    public void Status_StateCombination_ReturnsExpectedStatus(
        MatchStage stage,
        bool hasStarted,
        bool gameClockRunning,
        MatchStatus expected)
    {
        var state = new MatchState
        {
            GameId = Guid.NewGuid(),
            Stage = stage,
            HasStarted = hasStarted,
            GameClock = new ClockState
            {
                Remaining = TimeSpan.FromMinutes(5),
                IsRunning = gameClockRunning,
            },
        };

        Assert.AreEqual(expected, state.Status);
    }

    [TestMethod]
    public void Status_ShotClockRunning_ReturnsLive()
    {
        var state = new MatchState
        {
            GameId = Guid.NewGuid(),
            Stage = MatchStage.Regular,
            ShotClock = new ClockState
            {
                Remaining = TimeSpan.FromSeconds(8),
                IsRunning = true,
            },
        };

        Assert.AreEqual(MatchStatus.Live, state.Status);
    }

    [TestMethod]
    public void PenaltyProperties_FoulsAtDifferentThresholds_ProjectEachTeamIndependently()
    {
        var state = new MatchState
        {
            Rules = MatchRules.Fiba3x3,
            Home = new TeamState { Name = "Home", Fouls = 7 },
            Away = new TeamState { Name = "Away", Fouls = 10 },
        };

        Assert.AreEqual(PenaltyState.Penalty, state.HomePenalty);
        Assert.AreEqual(PenaltyState.DoublePenalty, state.AwayPenalty);
        Assert.AreSame(state.Home, state.GetTeam(TeamSide.Home));
        Assert.AreSame(state.Away, state.GetTeam(TeamSide.Away));
    }

    [TestMethod]
    public void GetClock_EachKind_ReturnsCorrespondingClock()
    {
        var gameClock = new ClockState { Remaining = TimeSpan.FromMinutes(3) };
        var shotClock = new ClockState { Remaining = TimeSpan.FromSeconds(4) };
        var state = new MatchState
        {
            GameClock = gameClock,
            ShotClock = shotClock,
        };

        Assert.AreSame(gameClock, state.GetClock(ClockKind.Game));
        Assert.AreSame(shotClock, state.GetClock(ClockKind.Shot));
    }
}
