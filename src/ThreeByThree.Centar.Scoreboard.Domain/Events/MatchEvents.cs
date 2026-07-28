using System.Text.Json.Serialization;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Events;

public sealed record EventMetadata(
    Guid EventId,
    long Sequence,
    DateTimeOffset RecordedAtUtc,
    long SessionElapsedMilliseconds,
    CommandSource Source);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GameCreatedEvent), "gameCreated")]
[JsonDerivedType(typeof(ScoreAdjustedEvent), "scoreAdjusted")]
[JsonDerivedType(typeof(FoulAdjustedEvent), "foulAdjusted")]
[JsonDerivedType(typeof(TeamNameChangedEvent), "teamNameChanged")]
[JsonDerivedType(typeof(TeamColorChangedEvent), "teamColorChanged")]
[JsonDerivedType(typeof(TeamsSwappedEvent), "teamsSwapped")]
[JsonDerivedType(typeof(ClockChangedEvent), "clockChanged")]
[JsonDerivedType(typeof(ClockExpiredEvent), "clockExpired")]
[JsonDerivedType(typeof(BuzzerTriggeredEvent), "buzzerTriggered")]
[JsonDerivedType(typeof(OvertimeStartedEvent), "overtimeStarted")]
[JsonDerivedType(typeof(PendingDecisionClearedEvent), "pendingDecisionCleared")]
[JsonDerivedType(typeof(EventRevertedEvent), "eventReverted")]
[JsonDerivedType(typeof(GameEndedEvent), "gameEnded")]
public abstract record MatchEvent(EventMetadata Metadata)
{
    public Guid EventId => Metadata.EventId;

    public long Sequence => Metadata.Sequence;
}

public sealed record GameCreatedEvent(
    EventMetadata Metadata,
    Guid GameId,
    MatchMetadata MatchMetadata,
    MatchRules Rules,
    string HomeName,
    string AwayName,
    string HomeColorHex,
    string AwayColorHex)
    : MatchEvent(Metadata);

public sealed record ScoreAdjustedEvent(
    EventMetadata Metadata,
    TeamSide Team,
    int Delta,
    int Before,
    int After)
    : MatchEvent(Metadata);

public sealed record FoulAdjustedEvent(
    EventMetadata Metadata,
    TeamSide Team,
    int Delta,
    int Before,
    int After)
    : MatchEvent(Metadata);

public sealed record TeamNameChangedEvent(
    EventMetadata Metadata,
    TeamSide Team,
    string Before,
    string After)
    : MatchEvent(Metadata);

public sealed record TeamColorChangedEvent(
    EventMetadata Metadata,
    TeamSide Team,
    string Before,
    string After)
    : MatchEvent(Metadata);

public sealed record TeamsSwappedEvent(EventMetadata Metadata)
    : MatchEvent(Metadata);

public sealed record ClockChangedEvent(
    EventMetadata Metadata,
    ClockKind Clock,
    ClockOperation Operation,
    TimeSpan Before,
    TimeSpan After,
    bool WasRunning,
    bool IsRunning)
    : MatchEvent(Metadata);

public sealed record ClockExpiredEvent(
    EventMetadata Metadata,
    ClockKind Clock,
    TimeSpan Before)
    : MatchEvent(Metadata);

public sealed record BuzzerTriggeredEvent(
    EventMetadata Metadata,
    BuzzerKind Buzzer)
    : MatchEvent(Metadata);

public sealed record OvertimeStartedEvent(
    EventMetadata Metadata,
    TimeSpan ShotClockDuration,
    TeamSide StartingPossession = TeamSide.Home)
    : MatchEvent(Metadata);

public sealed record PendingDecisionClearedEvent(
    EventMetadata Metadata,
    PendingDecision PreviousDecision)
    : MatchEvent(Metadata);

public sealed record EventRevertedEvent(
    EventMetadata Metadata,
    Guid TargetEventId)
    : MatchEvent(Metadata);

public sealed record GameEndedEvent(
    EventMetadata Metadata,
    int HomeScore,
    int AwayScore)
    : MatchEvent(Metadata);
