using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.Services;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Tests;

[TestClass]
public sealed class ScoreboardViewModelTests
{
    [TestMethod]
    public void Constructor_CreatedGroupGame_ProjectsGameTypeLabelAndTeams()
    {
        using var session = new MatchSession(
            new MatchEngine(),
            TimeProvider.System);
        var created = session.Execute(
            new CreateGameCommand(
                new MatchMetadata
                {
                    TournamentName = "City Cup",
                    GameType = GameType.Group,
                    Group = "20",
                },
                MatchRules.Fiba3x3,
                "Centar",
                "Rivals",
                "#FFFFFF",
                "#123456"));
        using var ticker = new MatchPresentationTicker();
        using var viewModel = new ScoreboardViewModel(session, ticker);

        Assert.IsTrue(created.IsAccepted);
        Assert.AreEqual("GROUP 20", viewModel.Category);
        Assert.AreEqual("CENTAR", viewModel.HomeName);
        Assert.AreEqual("RIVALS", viewModel.AwayName);
        Assert.AreEqual("0", viewModel.HomeScore);
        Assert.AreEqual("0", viewModel.AwayScore);
    }

    [TestMethod]
    public void Constructor_OvertimeWithRunningShotClock_ShowsOtAndLiveShotClock()
    {
        using var session = new MatchSession(
            new MatchEngine(),
            TimeProvider.System);
        var created = session.Execute(
            new CreateGameCommand(
                new MatchMetadata(),
                MatchRules.Fiba3x3,
                "Centar",
                "Rivals",
                "#FFFFFF",
                "#123456"));
        var regulationStarted = session.Execute(
            new SetLinkedClocksRunningCommand(true));
        var regulationExpired = session.Execute(
            new ExpireClockCommand(ClockKind.Game));
        var overtimeStarted = session.Execute(new StartOvertimeCommand());
        var shotClockStarted = session.Execute(
            new SetLinkedClocksRunningCommand(true));
        using var ticker = new MatchPresentationTicker();
        using var viewModel = new ScoreboardViewModel(session, ticker);

        Assert.IsTrue(created.IsAccepted);
        Assert.IsTrue(regulationStarted.IsAccepted);
        Assert.IsTrue(regulationExpired.IsAccepted);
        Assert.IsTrue(overtimeStarted.IsAccepted);
        Assert.IsTrue(shotClockStarted.IsAccepted);
        Assert.AreEqual(MatchStage.Overtime, session.Snapshot.Stage);
        Assert.IsFalse(session.Snapshot.GameClock.IsRunning);
        Assert.IsTrue(session.Snapshot.ShotClock.IsRunning);
        Assert.AreEqual("OT", viewModel.GameClock);
        Assert.AreEqual("12", viewModel.ShotClock);
        Assert.AreEqual("OVERTIME", viewModel.StatusBanner);
    }
}
