using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Persistence;

public sealed record SavedGameInfo(
    Guid GameId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset SavedAtUtc,
    MatchStage Stage,
    string TournamentName,
    string HomeName,
    int HomeScore,
    string AwayName,
    int AwayScore,
    string FilePath)
{
    public bool IsFinished => Stage == MatchStage.Final;
}
