using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeByThree.Centar.Scoreboard.Application.Tournaments;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class TournamentManagerViewModel(
    ITournamentStore store) : ObservableObject
{
    public ObservableCollection<Tournament> Tournaments { get; } = [];

    [ObservableProperty]
    private Tournament? selectedTournament;

    [ObservableProperty]
    private TournamentTeam? selectedTeam;

    [ObservableProperty]
    private string newTournamentName = string.Empty;

    [ObservableProperty]
    private string newTeamName = string.Empty;

    [ObservableProperty]
    private string newTeamColorHex = "#FFFFFF";

    [ObservableProperty]
    private string newTeamImagePath = string.Empty;

    [ObservableProperty]
    private string newPlayerName = string.Empty;

    [ObservableProperty]
    private string newPlayerImagePath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Create a tournament, then add its teams and players.";

    [ObservableProperty]
    private bool isBusy;

    public async Task LoadAsync()
    {
        await RunAsync(
            async () =>
            {
                var tournaments = await store.ListAsync();
                Tournaments.Clear();
                foreach (var tournament in tournaments)
                {
                    Tournaments.Add(tournament);
                }

                SelectedTournament = Tournaments.Count > 0 ? Tournaments[0] : null;
                StatusMessage = tournaments.Count == 0
                    ? "No tournaments yet. Create the first one."
                    : $"{tournaments.Count} tournament(s) loaded.";
            });
    }

    [RelayCommand]
    private async Task CreateTournament()
    {
        var name = NewTournamentName.Trim();
        if (name.Length is < 1 or > 80)
        {
            StatusMessage = "Tournament names must contain between 1 and 80 characters.";
            return;
        }

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await RunAsync(
            async () =>
            {
                await store.SaveAsync(tournament);
                Tournaments.Insert(0, tournament);
                SelectedTournament = tournament;
                NewTournamentName = string.Empty;
                StatusMessage = $"Tournament “{tournament.Name}” created.";
            });
    }

    [RelayCommand]
    private async Task AddTeam()
    {
        var tournament = SelectedTournament;
        if (tournament is null)
        {
            StatusMessage = "Select or create a tournament first.";
            return;
        }

        var name = NewTeamName.Trim();
        if (name.Length is < 1 or > 32)
        {
            StatusMessage = "Team names must contain between 1 and 32 characters.";
            return;
        }

        var teamId = Guid.NewGuid();
        await RunAsync(
            async () =>
            {
                var imagePath = await ImportImageIfSelected(
                    tournament.Id,
                    teamId,
                    NewTeamImagePath);
                var team = new TournamentTeam
                {
                    Id = teamId,
                    Name = name,
                    ColorHex = NormalizeColor(NewTeamColorHex),
                    ImagePath = imagePath,
                };
                var updated = tournament with
                {
                    Teams = [.. tournament.Teams, team],
                };
                await store.SaveAsync(updated);
                ReplaceTournament(updated, team.Id);
                NewTeamName = string.Empty;
                NewTeamColorHex = "#FFFFFF";
                NewTeamImagePath = string.Empty;
                StatusMessage = $"Team “{team.Name}” added.";
            });
    }

    [RelayCommand]
    private async Task AddPlayer()
    {
        var tournament = SelectedTournament;
        var team = SelectedTeam;
        if (tournament is null || team is null)
        {
            StatusMessage = "Select a team before adding a player.";
            return;
        }

        var name = NewPlayerName.Trim();
        if (name.Length is < 1 or > 64)
        {
            StatusMessage = "Player names must contain between 1 and 64 characters.";
            return;
        }

        var playerId = Guid.NewGuid();
        await RunAsync(
            async () =>
            {
                var imagePath = await ImportImageIfSelected(
                    tournament.Id,
                    playerId,
                    NewPlayerImagePath);
                var player = new TournamentPlayer
                {
                    Id = playerId,
                    Name = name,
                    ImagePath = imagePath,
                };
                var updatedTeam = team with
                {
                    Players = [.. team.Players, player],
                };
                var updatedTournament = tournament with
                {
                    Teams = tournament.Teams
                        .Select(candidate =>
                            candidate.Id == team.Id ? updatedTeam : candidate)
                        .ToArray(),
                };
                await store.SaveAsync(updatedTournament);
                ReplaceTournament(updatedTournament, updatedTeam.Id);
                NewPlayerName = string.Empty;
                NewPlayerImagePath = string.Empty;
                StatusMessage = $"Player “{player.Name}” added to {team.Name}.";
            });
    }

    partial void OnSelectedTournamentChanged(Tournament? value) =>
        SelectedTeam = value is { Teams.Count: > 0 } ? value.Teams[0] : null;

    private async Task<string?> ImportImageIfSelected(
        Guid tournamentId,
        Guid subjectId,
        string sourcePath)
    {
        return string.IsNullOrWhiteSpace(sourcePath)
            ? null
            : await store.ImportImageAsync(tournamentId, subjectId, sourcePath);
    }

    private void ReplaceTournament(Tournament updated, Guid selectedTeamId)
    {
        var index = Tournaments
            .Select((tournament, position) => (tournament, position))
            .First(item => item.tournament.Id == updated.Id)
            .position;
        Tournaments[index] = updated;
        SelectedTournament = updated;
        SelectedTeam = updated.Teams.First(team => team.Id == selectedTeamId);
    }

    private static string NormalizeColor(string value)
    {
        var color = value.Trim().ToUpperInvariant();
        if (color.Length != 7 ||
            color[0] != '#' ||
            color.Skip(1).Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "Team colors must use six-digit hexadecimal notation, for example #FF5252.");
        }

        return color;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
