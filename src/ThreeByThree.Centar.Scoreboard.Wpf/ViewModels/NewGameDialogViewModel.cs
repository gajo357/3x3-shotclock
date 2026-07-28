using CommunityToolkit.Mvvm.ComponentModel;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class NewGameDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string tournamentName = "3x3 Centar";

    [ObservableProperty]
    private string scheduledGameId = string.Empty;

    [ObservableProperty]
    private string courtName = "Main Court";

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private DateTime? scheduledDate;

    [ObservableProperty]
    private string scheduledTime = string.Empty;

    [ObservableProperty]
    private string homeName = "HOME";

    [ObservableProperty]
    private string awayName = "AWAY";

    [ObservableProperty]
    private string homeColorHex = "#FFFFFF";

    [ObservableProperty]
    private string awayColorHex = "#FF5252";

    [ObservableProperty]
    private int gameMinutes = 10;

    [ObservableProperty]
    private int shotClockSeconds = 12;

    [ObservableProperty]
    private TeamSide coinTossWinner = TeamSide.Home;

    [ObservableProperty]
    private CoinTossChoice coinTossSelection = CoinTossChoice.OpeningPossession;

    public CreateGameCommand BuildCommand()
    {
        var rules = MatchRules.Fiba3x3 with
        {
            RegularDuration = TimeSpan.FromMinutes(Math.Clamp(GameMinutes, 1, 99)),
            ShotClockDuration = TimeSpan.FromSeconds(Math.Clamp(ShotClockSeconds, 1, 99)),
            ShotClockTenthsThreshold = TimeSpan.FromSeconds(
                Math.Min(5, Math.Clamp(ShotClockSeconds, 1, 99))),
        };
        var scheduledStart = ParseScheduledStart();
        var metadata = new MatchMetadata
        {
            TournamentName = TournamentName,
            ScheduledGameId = ScheduledGameId,
            CourtName = CourtName,
            Category = Category,
            ScheduledStart = scheduledStart,
            CoinTossWinner = CoinTossWinner,
            CoinTossSelection = CoinTossSelection,
        };

        return new CreateGameCommand(
            metadata,
            rules,
            HomeName,
            AwayName,
            HomeColorHex,
            AwayColorHex);
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
