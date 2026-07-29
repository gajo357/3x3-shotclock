using ThreeByThree.Centar.Scoreboard.Application.Tournaments;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Tests;

[TestClass]
public sealed class TournamentManagerViewModelTests
{
    [TestMethod]
    public async Task CreateTournamentCommand_ValidName_PersistsAndSelectsEmptyTournament()
    {
        var store = new RecordingTournamentStore();
        var viewModel = new TournamentManagerViewModel(store)
        {
            NewTournamentName = "  Summer Cup  ",
        };

        await viewModel.CreateTournamentCommand.ExecuteAsync(null);

        var saved = Assert.ContainsSingle(store.SavedTournaments);
        Assert.AreNotEqual(Guid.Empty, saved.Id);
        Assert.AreEqual("Summer Cup", saved.Name);
        Assert.AreNotEqual(default, saved.CreatedAtUtc);
        Assert.IsNotNull(saved.Teams);
        Assert.IsEmpty(saved.Teams);
        Assert.AreSame(saved, viewModel.SelectedTournament);
        Assert.AreSame(saved, Assert.ContainsSingle(viewModel.Tournaments));
        Assert.AreEqual(string.Empty, viewModel.NewTournamentName);
        Assert.AreEqual("Tournament “Summer Cup” created.", viewModel.StatusMessage);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsEmpty(store.ImageImports);
    }

    [TestMethod]
    public async Task AddTeamAndPlayers_MixedOptionalImages_PersistsCorrectOwnership()
    {
        var store = new RecordingTournamentStore();
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = "Roster Cup",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var viewModel = new TournamentManagerViewModel(store)
        {
            SelectedTournament = tournament,
            NewTeamName = "No Image",
            NewTeamColorHex = "#112233",
        };
        viewModel.Tournaments.Add(tournament);

        await viewModel.AddTeamCommand.ExecuteAsync(null);
        viewModel.NewTeamName = "With Image";
        viewModel.NewTeamColorHex = " #abcdef ";
        viewModel.NewTeamImagePath = @"C:\imports\team.PNG";
        await viewModel.AddTeamCommand.ExecuteAsync(null);
        viewModel.NewPlayerName = "No Portrait";
        await viewModel.AddPlayerCommand.ExecuteAsync(null);
        viewModel.NewPlayerName = "With Portrait";
        viewModel.NewPlayerImagePath = @"C:\imports\player.jpg";
        await viewModel.AddPlayerCommand.ExecuteAsync(null);

        var finalTournament = viewModel.SelectedTournament;
        Assert.IsNotNull(finalTournament);
        Assert.HasCount(2, finalTournament.Teams);
        var firstTeam = finalTournament.Teams[0];
        var secondTeam = finalTournament.Teams[1];
        Assert.AreEqual("No Image", firstTeam.Name);
        Assert.AreEqual("#112233", firstTeam.ColorHex);
        Assert.IsNull(firstTeam.ImagePath);
        Assert.IsEmpty(firstTeam.Players);
        Assert.AreEqual("With Image", secondTeam.Name);
        Assert.AreEqual("#ABCDEF", secondTeam.ColorHex);
        Assert.AreEqual(
            $"imported/{secondTeam.Id:N}.png",
            secondTeam.ImagePath);
        Assert.HasCount(2, secondTeam.Players);
        Assert.AreEqual("No Portrait", secondTeam.Players[0].Name);
        Assert.IsNull(secondTeam.Players[0].ImagePath);
        Assert.AreEqual("With Portrait", secondTeam.Players[1].Name);
        Assert.AreEqual(
            $"imported/{secondTeam.Players[1].Id:N}.png",
            secondTeam.Players[1].ImagePath);
        Assert.HasCount(2, store.ImageImports);
        Assert.AreEqual(
            (tournament.Id, secondTeam.Id, @"C:\imports\team.PNG"),
            store.ImageImports[0]);
        Assert.AreEqual(
            (tournament.Id, secondTeam.Players[1].Id, @"C:\imports\player.jpg"),
            store.ImageImports[1]);
        Assert.HasCount(4, store.SavedTournaments);
        Assert.AreSame(finalTournament, store.SavedTournaments[^1]);
        Assert.AreSame(secondTeam, viewModel.SelectedTeam);
        Assert.IsFalse(viewModel.IsBusy);
    }

    private sealed class RecordingTournamentStore : ITournamentStore
    {
        public string TournamentsDirectory => "tournaments";

        public List<Tournament> SavedTournaments { get; } = [];

        public List<(Guid TournamentId, Guid SubjectId, string SourcePath)> ImageImports
        {
            get;
        } = [];

        public Task<IReadOnlyList<Tournament>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Tournament>>(
                [.. SavedTournaments]);
        }

        public Task SaveAsync(
            Tournament tournament,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedTournaments.Add(tournament);
            return Task.CompletedTask;
        }

        public Task<string> ImportImageAsync(
            Guid tournamentId,
            Guid subjectId,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageImports.Add((tournamentId, subjectId, sourcePath));
            return Task.FromResult($"imported/{subjectId:N}.png");
        }
    }
}
