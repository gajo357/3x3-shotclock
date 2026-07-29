using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Tests;

[TestClass]
public sealed class NewGameDialogViewModelTests
{
    [TestMethod]
    public void GameTypes_ContainsOnlySupportedSelectableTypes()
    {
        var viewModel = new NewGameDialogViewModel([]);
        GameType[] expectedTypes =
        [
            GameType.Group,
            GameType.Qualifier,
            GameType.Quarterfinal,
            GameType.Semifinal,
            GameType.Final,
        ];
        string[] expectedLabels =
        [
            "GROUP",
            "QUALIFIER",
            "QUARTERFINAL",
            "SEMIFINAL",
            "FINAL",
        ];

        CollectionAssert.AreEqual(
            expectedTypes,
            viewModel.GameTypes.Select(option => option.Type).ToArray());
        CollectionAssert.AreEqual(
            expectedLabels,
            viewModel.GameTypes.Select(option => option.Label).ToArray());
        Assert.DoesNotContain(
            GameType.Unspecified,
            viewModel.GameTypes.Select(option => option.Type));
    }

    [TestMethod]
    public void TryBuildCommand_SelectedTournamentTeams_UsesSelectedTeamsAndClassification()
    {
        var tournament = CreateTournament(
            "City Cup",
            ("Centar", "#ABCDEF"),
            ("Rivals", "#123456"));
        var viewModel = new NewGameDialogViewModel([tournament])
        {
            SelectedGroup = "Z",
            ScheduledGameId = "G-42",
            CourtName = "Center Court",
            Category = "Under 18",
        };

        var accepted = viewModel.TryBuildCommand(
            out var command,
            out var validationMessage);

        Assert.IsTrue(accepted);
        Assert.IsNotNull(command);
        Assert.AreEqual(string.Empty, validationMessage);
        Assert.AreEqual(tournament.Id, command.Metadata.TournamentId);
        Assert.AreEqual("City Cup", command.Metadata.TournamentName);
        Assert.AreEqual(tournament.Teams[0].Id, command.Metadata.HomeTeamId);
        Assert.AreEqual(tournament.Teams[1].Id, command.Metadata.AwayTeamId);
        Assert.AreEqual(GameType.Group, command.Metadata.GameType);
        Assert.AreEqual("Z", command.Metadata.Group);
        Assert.AreEqual("G-42", command.Metadata.ScheduledGameId);
        Assert.AreEqual("Center Court", command.Metadata.CourtName);
        Assert.AreEqual("Under 18", command.Metadata.Category);
        Assert.AreEqual("Centar", command.HomeName);
        Assert.AreEqual("#ABCDEF", command.HomeColorHex);
        Assert.AreEqual("Rivals", command.AwayName);
        Assert.AreEqual("#123456", command.AwayColorHex);
    }

    [TestMethod]
    public void TryBuildCommand_NonGroupWithStaleGroup_OmitsGroup()
    {
        var tournament = CreateTournament(
            "Finals",
            ("Home", "#FFFFFF"),
            ("Away", "#000000"));
        var viewModel = new NewGameDialogViewModel([tournament])
        {
            SelectedGameType = new GameTypeOption(GameType.Final, "FINAL"),
            SelectedGroup = "A",
        };

        var accepted = viewModel.TryBuildCommand(
            out var command,
            out var validationMessage);

        Assert.IsTrue(accepted);
        Assert.IsNotNull(command);
        Assert.AreEqual(string.Empty, validationMessage);
        Assert.AreEqual(GameType.Final, command.Metadata.GameType);
        Assert.AreEqual(string.Empty, command.Metadata.Group);
        Assert.AreEqual("FINAL", command.Metadata.GetGameTypeLabel());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("21")]
    [DataRow("AA")]
    [DataRow("a")]
    [DataRow(" A")]
    public void TryBuildCommand_InvalidGroup_IsRejected(string group)
    {
        var tournament = CreateTournament(
            "Group Stage",
            ("Home", "#FFFFFF"),
            ("Away", "#000000"));
        var viewModel = new NewGameDialogViewModel([tournament])
        {
            SelectedGroup = group,
        };

        var accepted = viewModel.TryBuildCommand(
            out var command,
            out var validationMessage);

        Assert.IsFalse(accepted);
        Assert.IsNull(command);
        Assert.AreEqual(
            "Select a group from 1–20 or A–Z.",
            validationMessage);
    }

    [TestMethod]
    public void TryBuildCommand_SameTeamOnBothSides_IsRejectedWithoutCommand()
    {
        var tournament = CreateTournament(
            "Qualifier",
            ("Only Team", "#FFFFFF"),
            ("Other Team", "#000000"));
        var viewModel = new NewGameDialogViewModel([tournament])
        {
            SelectedAwayTeam = tournament.Teams[0],
        };

        var accepted = viewModel.TryBuildCommand(
            out var command,
            out var validationMessage);

        Assert.IsFalse(accepted);
        Assert.IsNull(command);
        Assert.AreEqual(
            "Home and away teams must be different.",
            validationMessage);
    }

    [TestMethod]
    public void SelectedTournament_Changed_SelectsItsFirstTwoTeams()
    {
        var first = CreateTournament(
            "First",
            ("First Home", "#111111"),
            ("First Away", "#222222"));
        var second = CreateTournament(
            "Second",
            ("Second Home", "#333333"),
            ("Second Away", "#444444"),
            ("Reserve", "#555555"));
        var viewModel = new NewGameDialogViewModel([first, second]);

        viewModel.SelectedTournament = second;

        var selectedHome = viewModel.SelectedHomeTeam;
        var selectedAway = viewModel.SelectedAwayTeam;
        Assert.IsNotNull(selectedHome);
        Assert.IsNotNull(selectedAway);
        Assert.AreSame(second.Teams[0], viewModel.SelectedHomeTeam);
        Assert.AreSame(second.Teams[1], viewModel.SelectedAwayTeam);
        Assert.AreEqual("Second Home", selectedHome.Name);
        Assert.AreEqual("Second Away", selectedAway.Name);
    }

    [TestMethod]
    public void TryBuildCommand_TournamentWithoutTwoTeams_IsRejected()
    {
        var tournament = CreateTournament(
            "Incomplete",
            ("Only Team", "#FFFFFF"));
        var viewModel = new NewGameDialogViewModel([tournament]);

        var accepted = viewModel.TryBuildCommand(
            out var command,
            out var validationMessage);

        Assert.IsFalse(accepted);
        Assert.IsNull(command);
        Assert.AreEqual("Select both teams.", validationMessage);
    }

    private static Tournament CreateTournament(
        string name,
        params (string Name, string ColorHex)[] teams) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Teams =
            [
                .. teams.Select(team => new TournamentTeam
                {
                    Id = Guid.NewGuid(),
                    Name = team.Name,
                    ColorHex = team.ColorHex,
                }),
            ],
        };
}
