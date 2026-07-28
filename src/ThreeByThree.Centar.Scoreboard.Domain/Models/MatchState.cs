namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record MatchState
{
    public static MatchState Empty { get; } = new();

    public Guid GameId { get; init; }

    public MatchMetadata Metadata { get; init; } = new();

    public MatchRules Rules { get; init; } = MatchRules.Fiba3x3;

    public TeamState Home { get; init; } = new() { Name = "HOME" };

    public TeamState Away { get; init; } = new() { Name = "AWAY", ColorHex = "#FF5252" };

    public ClockState GameClock { get; init; } = new()
    {
        Remaining = TimeSpan.FromMinutes(10),
    };

    public ClockState ShotClock { get; init; } = new()
    {
        Remaining = TimeSpan.FromSeconds(12),
    };

    public MatchStage Stage { get; init; } = MatchStage.Setup;

    public PendingDecision PendingDecision { get; init; }

    public bool HasStarted { get; init; }

    public TeamSide StartingPossession { get; init; } = TeamSide.Home;

    public long LastEventSequence { get; init; }

    public bool IsCreated => GameId != Guid.Empty;

    public MatchStatus Status => Stage switch
    {
        MatchStage.Setup => MatchStatus.Setup,
        MatchStage.Final => MatchStatus.Final,
        MatchStage.Overtime => MatchStatus.Overtime,
        _ when GameClock.IsRunning || ShotClock.IsRunning => MatchStatus.Live,
        _ when HasStarted => MatchStatus.Paused,
        _ => MatchStatus.Ready,
    };

    public PenaltyState HomePenalty => Rules.GetPenaltyState(Home.Fouls);

    public PenaltyState AwayPenalty => Rules.GetPenaltyState(Away.Fouls);

    public TeamState GetTeam(TeamSide side) => side == TeamSide.Home ? Home : Away;

    public ClockState GetClock(ClockKind kind) => kind == ClockKind.Game ? GameClock : ShotClock;
}
