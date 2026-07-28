namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record MatchMetadata
{
    public string TournamentName { get; init; } = "3x3 Centar";

    public string ScheduledGameId { get; init; } = string.Empty;

    public string CourtName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public DateTimeOffset? ScheduledStart { get; init; }

    public TeamSide CoinTossWinner { get; init; } = TeamSide.Home;

    public CoinTossChoice CoinTossSelection { get; init; } =
        CoinTossChoice.OpeningPossession;

    public TeamSide GetOpeningPossession() =>
        CoinTossSelection == CoinTossChoice.OpeningPossession
            ? CoinTossWinner
            : Opposite(CoinTossWinner);

    public TeamSide GetOvertimePossession() =>
        CoinTossSelection == CoinTossChoice.OvertimePossession
            ? CoinTossWinner
            : Opposite(CoinTossWinner);

    private static TeamSide Opposite(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}
