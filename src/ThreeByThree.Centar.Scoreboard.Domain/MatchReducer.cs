using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain;

public static class MatchReducer
{
    public static MatchState Replay(IEnumerable<MatchEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var orderedEvents = events.OrderBy(matchEvent => matchEvent.Sequence).ToArray();
        var revertedEventIds = orderedEvents
            .OfType<EventRevertedEvent>()
            .Select(matchEvent => matchEvent.TargetEventId)
            .ToHashSet();

        var state = MatchState.Empty;

        foreach (var matchEvent in orderedEvents)
        {
            if (matchEvent is EventRevertedEvent || revertedEventIds.Contains(matchEvent.EventId))
            {
                continue;
            }

            state = Apply(state, matchEvent);
        }

        return state with
        {
            LastEventSequence = orderedEvents.LastOrDefault()?.Sequence ?? 0,
        };
    }

    public static MatchState Apply(MatchState state, MatchEvent matchEvent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(matchEvent);

        var updated = matchEvent switch
        {
            GameCreatedEvent created => ApplyGameCreated(created),
            ScoreAdjustedEvent score => ApplyScoreAdjusted(state, score),
            FoulAdjustedEvent foul => ApplyFoulAdjusted(state, foul),
            TeamNameChangedEvent name => ApplyTeamNameChanged(state, name),
            TeamColorChangedEvent color => ApplyTeamColorChanged(state, color),
            TeamsSwappedEvent => ApplyTeamsSwapped(state),
            ClockChangedEvent clock => ApplyClockChanged(state, clock),
            ClockExpiredEvent expired => ApplyClockExpired(state, expired),
            OvertimeStartedEvent overtime => ApplyOvertimeStarted(state, overtime),
            PendingDecisionClearedEvent => state with { PendingDecision = PendingDecision.None },
            GameEndedEvent => state with
            {
                Stage = MatchStage.Final,
                PendingDecision = PendingDecision.None,
                GameClock = state.GameClock with { IsRunning = false },
                ShotClock = state.ShotClock with { IsRunning = false },
            },
            BuzzerTriggeredEvent or EventRevertedEvent => state,
            _ => throw new InvalidOperationException(
                $"Unsupported match event type: {matchEvent.GetType().Name}."),
        };

        return updated with { LastEventSequence = matchEvent.Sequence };
    }

    private static MatchState ApplyGameCreated(GameCreatedEvent matchEvent) =>
        new()
        {
            GameId = matchEvent.GameId,
            Metadata = matchEvent.MatchMetadata,
            Rules = matchEvent.Rules,
            Home = new TeamState
            {
                Name = matchEvent.HomeName,
                ColorHex = matchEvent.HomeColorHex,
            },
            Away = new TeamState
            {
                Name = matchEvent.AwayName,
                ColorHex = matchEvent.AwayColorHex,
            },
            GameClock = new ClockState { Remaining = matchEvent.Rules.RegularDuration },
            ShotClock = new ClockState { Remaining = matchEvent.Rules.ShotClockDuration },
            Stage = MatchStage.Regular,
            StartingPossession = matchEvent.MatchMetadata.GetOpeningPossession(),
            LastEventSequence = matchEvent.Sequence,
        };

    private static MatchState ApplyScoreAdjusted(MatchState state, ScoreAdjustedEvent matchEvent)
    {
        var currentTeam = state.GetTeam(matchEvent.Team);
        var overtimePoints = state.Stage == MatchStage.Overtime
            ? Math.Max(0, currentTeam.OvertimePoints + matchEvent.Delta)
            : currentTeam.OvertimePoints;
        var updatedTeam = currentTeam with
        {
            Score = matchEvent.After,
            OvertimePoints = overtimePoints,
        };

        var updated = SetTeam(state, matchEvent.Team, updatedTeam);
        return updated with { PendingDecision = GetWinningDecision(updated) };
    }

    private static MatchState ApplyFoulAdjusted(MatchState state, FoulAdjustedEvent matchEvent)
    {
        var updatedTeam = state.GetTeam(matchEvent.Team) with { Fouls = matchEvent.After };
        return SetTeam(state, matchEvent.Team, updatedTeam);
    }

    private static MatchState ApplyTeamNameChanged(
        MatchState state,
        TeamNameChangedEvent matchEvent)
    {
        var updatedTeam = state.GetTeam(matchEvent.Team) with { Name = matchEvent.After };
        return SetTeam(state, matchEvent.Team, updatedTeam);
    }

    private static MatchState ApplyTeamColorChanged(
        MatchState state,
        TeamColorChangedEvent matchEvent)
    {
        var updatedTeam = state.GetTeam(matchEvent.Team) with { ColorHex = matchEvent.After };
        return SetTeam(state, matchEvent.Team, updatedTeam);
    }

    private static MatchState ApplyTeamsSwapped(MatchState state) =>
        state with
        {
            Home = state.Away,
            Away = state.Home,
            Metadata = state.Metadata with
            {
                CoinTossWinner = Opposite(state.Metadata.CoinTossWinner),
            },
            StartingPossession = Opposite(state.StartingPossession),
        };

    private static MatchState ApplyClockChanged(MatchState state, ClockChangedEvent matchEvent)
    {
        var updatedClock = state.GetClock(matchEvent.Clock) with
        {
            Remaining = matchEvent.After,
            IsRunning = matchEvent.IsRunning,
            HasExpired = false,
        };

        var updated = SetClock(state, matchEvent.Clock, updatedClock);
        return matchEvent.Clock == ClockKind.Game && matchEvent.IsRunning
            ? updated with { HasStarted = true }
            : updated;
    }

    private static MatchState ApplyClockExpired(MatchState state, ClockExpiredEvent matchEvent)
    {
        var expiredClock = state.GetClock(matchEvent.Clock) with
        {
            Remaining = TimeSpan.Zero,
            IsRunning = false,
            HasExpired = true,
        };
        var updated = SetClock(state, matchEvent.Clock, expiredClock);

        if (matchEvent.Clock == ClockKind.Shot)
        {
            return updated with
            {
                GameClock = updated.GameClock with { IsRunning = false },
            };
        }

        var decision = updated.Home.Score == updated.Away.Score
            ? PendingDecision.StartOvertime
            : PendingDecision.ConfirmFinalScore;

        return updated with
        {
            HasStarted = true,
            PendingDecision = decision,
            ShotClock = updated.ShotClock with { IsRunning = false },
        };
    }

    private static MatchState ApplyOvertimeStarted(
        MatchState state,
        OvertimeStartedEvent matchEvent) =>
        state with
        {
            Stage = MatchStage.Overtime,
            PendingDecision = PendingDecision.None,
            Home = state.Home with { OvertimePoints = 0 },
            Away = state.Away with { OvertimePoints = 0 },
            GameClock = state.GameClock with { IsRunning = false },
            ShotClock = new ClockState { Remaining = matchEvent.ShotClockDuration },
            StartingPossession = matchEvent.StartingPossession,
        };

    private static PendingDecision GetWinningDecision(MatchState state)
    {
        var hasWinner = state.Stage switch
        {
            MatchStage.Regular =>
                state.Home.Score >= state.Rules.WinningScore ||
                state.Away.Score >= state.Rules.WinningScore,
            // Overtime is finalized immediately by the scoring command; the
            // regular-time winning-score decision never applies here.
            MatchStage.Overtime => false,
            _ => false,
        };

        if (hasWinner)
        {
            return PendingDecision.ConfirmWinningScore;
        }

        return state.PendingDecision == PendingDecision.ConfirmWinningScore
            ? PendingDecision.None
            : state.PendingDecision;
    }

    private static MatchState SetTeam(MatchState state, TeamSide side, TeamState team) =>
        side == TeamSide.Home
            ? state with { Home = team }
            : state with { Away = team };

    private static MatchState SetClock(MatchState state, ClockKind kind, ClockState clock) =>
        kind == ClockKind.Game
            ? state with { GameClock = clock }
            : state with { ShotClock = clock };

    private static TeamSide Opposite(TeamSide side) =>
        side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
}
