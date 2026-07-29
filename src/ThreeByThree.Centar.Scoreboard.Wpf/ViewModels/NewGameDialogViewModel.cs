using CommunityToolkit.Mvvm.ComponentModel;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class NewGameDialogViewModel : ObservableObject
{
    public NewGameDialogViewModel(IReadOnlyList<Tournament> tournaments)
    {
        ArgumentNullException.ThrowIfNull(tournaments);
        Tournaments = tournaments;
        Groups = GameGroups.All;
        SelectedTournament = Tournaments.Count > 0 ? Tournaments[0] : null;
    }

    public IReadOnlyList<Tournament> Tournaments { get; }

    public IReadOnlyList<GameTypeOption> GameTypes { get; } =
    [
        new(GameType.Group, "GROUP"),
        new(GameType.Qualifier, "QUALIFIER"),
        new(GameType.Quarterfinal, "QUARTERFINAL"),
        new(GameType.Semifinal, "SEMIFINAL"),
        new(GameType.Final, "FINAL"),
    ];

    public IReadOnlyList<string> Groups { get; }

    [ObservableProperty]
    private Tournament? selectedTournament;

    [ObservableProperty]
    private TournamentTeam? selectedHomeTeam;

    [ObservableProperty]
    private TournamentTeam? selectedAwayTeam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGroupGame))]
    private GameTypeOption selectedGameType =
        new(GameType.Group, "GROUP");

    [ObservableProperty]
    private string selectedGroup = "A";

    public bool IsGroupGame => SelectedGameType.Type == GameType.Group;

    [ObservableProperty]
    private string scheduledGameId = string.Empty;

    [ObservableProperty]
    private string courtName = "Main Court";

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private DateTime? scheduledDate = DateTime.Today;

    [ObservableProperty]
    private string scheduledTime = string.Empty;

    [ObservableProperty]
    private int gameMinutes = 10;

    [ObservableProperty]
    private int shotClockSeconds = 12;

    [ObservableProperty]
    private TeamSide coinTossWinner = TeamSide.Home;

    [ObservableProperty]
    private CoinTossChoice coinTossSelection = CoinTossChoice.OpeningPossession;

    public bool TryBuildCommand(
        out CreateGameCommand? command,
        out string validationMessage)
    {
        command = null;
        if (SelectedTournament is null)
        {
            validationMessage = "Select a tournament.";
            return false;
        }

        if (SelectedHomeTeam is null || SelectedAwayTeam is null)
        {
            validationMessage = "Select both teams.";
            return false;
        }

        if (SelectedHomeTeam.Id == SelectedAwayTeam.Id)
        {
            validationMessage = "Home and away teams must be different.";
            return false;
        }

        if (SelectedGameType.Type == GameType.Group &&
            !GameGroups.IsValid(SelectedGroup))
        {
            validationMessage = "Select a group from 1–20 or A–Z.";
            return false;
        }

        command = BuildCommand();
        validationMessage = string.Empty;
        return true;
    }

    partial void OnSelectedTournamentChanged(Tournament? value)
    {
        SelectedHomeTeam = value is { Teams.Count: > 0 } ? value.Teams[0] : null;
        SelectedAwayTeam = value is { Teams.Count: > 1 } ? value.Teams[1] : null;
    }

    private CreateGameCommand BuildCommand()
    {
        var tournament = SelectedTournament!;
        var home = SelectedHomeTeam!;
        var away = SelectedAwayTeam!;
        var rules = MatchRules.Fiba3x3 with
        {
            RegularDuration = TimeSpan.FromMinutes(Math.Clamp(GameMinutes, 1, 99)),
            ShotClockDuration = TimeSpan.FromSeconds(Math.Clamp(ShotClockSeconds, 1, 99)),
            ShotClockTenthsThreshold = TimeSpan.FromSeconds(
                Math.Min(5, Math.Clamp(ShotClockSeconds, 1, 99))),
        };
        var metadata = new MatchMetadata
        {
            TournamentId = tournament.Id,
            TournamentName = tournament.Name,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            ScheduledGameId = ScheduledGameId,
            CourtName = CourtName,
            Category = Category,
            GameType = SelectedGameType.Type,
            Group = SelectedGameType.Type == GameType.Group ? SelectedGroup : string.Empty,
            ScheduledStart = ParseScheduledStart(),
            CoinTossWinner = CoinTossWinner,
            CoinTossSelection = CoinTossSelection,
        };

        return new CreateGameCommand(
            metadata,
            rules,
            home.Name,
            away.Name,
            home.ColorHex,
            away.ColorHex);
    }

    private DateTimeOffset? ParseScheduledStart()
    {
        if (!ScheduledDate.HasValue)
        {
            return null;
        }

        var time = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(ScheduledTime) &&
            !TimeSpan.TryParse(
                ScheduledTime,
                System.Globalization.CultureInfo.CurrentCulture,
                out time))
        {
            time = TimeSpan.Zero;
        }

        var local = DateTime.SpecifyKind(
            ScheduledDate.Value.Date + time,
            DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}

public sealed record GameTypeOption(GameType Type, string Label);
