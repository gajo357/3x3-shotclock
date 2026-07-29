namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record MatchMetadata
{
    public Guid? TournamentId { get; init; }

    public string TournamentName { get; init; } = "3x3 Centar";

    public Guid? HomeTeamId { get; init; }

    public Guid? AwayTeamId { get; init; }

    public string ScheduledGameId { get; init; } = string.Empty;

    public string CourtName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public GameType GameType { get; init; }

    public string Group { get; init; } = string.Empty;

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

    public string GetGameTypeLabel()
    {
        var label = GameType switch
        {
            GameType.Group when !string.IsNullOrWhiteSpace(Group) =>
                $"GROUP {Group.Trim().ToUpperInvariant()}",
            GameType.Group => "GROUP",
            GameType.Qualifier => "QUALIFIER",
            GameType.Quarterfinal => "QUARTERFINAL",
            GameType.Semifinal => "SEMIFINAL",
            GameType.Final => "FINAL",
            _ => Category.Trim().ToUpperInvariant(),
        };

        return label;
    }

    private static TeamSide Opposite(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}
