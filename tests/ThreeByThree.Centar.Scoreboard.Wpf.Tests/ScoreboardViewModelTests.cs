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
}
