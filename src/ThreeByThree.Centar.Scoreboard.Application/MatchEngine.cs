using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application;

public sealed class MatchEngine
{
    private static readonly TimeSpan MaximumGameClock =
        TimeSpan.FromMinutes(100) - TimeSpan.FromMilliseconds(100);

    public CommandResult Execute(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        MatchCommand command,
        DateTimeOffset recordedAtUtc,
        TimeSpan sessionElapsed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(command);

        var nextSequence = history.Count == 0
            ? 1
            : history.Max(matchEvent => matchEvent.Sequence) + 1;
        var elapsedMilliseconds = Math.Max(0, (long)sessionElapsed.TotalMilliseconds);

        EventMetadata CreateMetadata() =>
            new(
                Guid.NewGuid(),
                nextSequence++,
                recordedAtUtc,
                elapsedMilliseconds,
                command.Source);

        return command switch
        {
            CreateGameCommand create => CreateGame(
                state,
                history,
                create,
                CreateMetadata),
            AdjustScoreCommand score => AdjustScore(
                state,
                history,
                score,
                CreateMetadata),
            AdjustFoulCommand foul => AdjustFoul(
                state,
                history,
                foul,
                CreateMetadata),
            ChangeTeamNameCommand name => ChangeTeamName(
                state,
                history,
                name,
                CreateMetadata),
            ChangeTeamColorCommand color => ChangeTeamColor(
                state,
                history,
                color,
                CreateMetadata),
            SwapTeamsCommand => SwapTeams(
                state,
                history,
                CreateMetadata),
            SetLinkedClocksRunningCommand clocks => SetLinkedClocksRunning(
                state,
                history,
                clocks,
                CreateMetadata),
            SetClockRunningCommand clock => SetClockRunning(
                state,
                history,
                clock,
                CreateMetadata),
            AdjustClockCommand clock => AdjustClock(
                state,
                history,
                clock,
                CreateMetadata),
            SetClockCommand clock => SetClock(
                state,
                history,
                clock,
                CreateMetadata),
            ResetClockCommand clock => ResetClock(
                state,
                history,
                clock,
                CreateMetadata),
            ExpireClockCommand clock => ExpireClock(
                state,
                history,
                clock,
                CreateMetadata),
            TriggerBuzzerCommand trigger => Accept(
                history,
                new BuzzerTriggeredEvent(CreateMetadata(), trigger.Buzzer)),
            StartOvertimeCommand => StartOvertime(
                state,
                history,
                CreateMetadata),
            ClearPendingDecisionCommand => ClearPendingDecision(
                state,
                history,
                CreateMetadata),
            EndGameCommand => EndGame(state, history, CreateMetadata),
            UndoLastActionCommand => Undo(state, history, CreateMetadata),
            _ => CommandResult.Reject(
                state,
                $"Unsupported command type: {command.GetType().Name}."),
        };
    }

    private static CommandResult CreateGame(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        CreateGameCommand command,
        Func<EventMetadata> createMetadata)
    {
        if (state.IsCreated && state.Stage != MatchStage.Final)
        {
            return CommandResult.Reject(state, "End the active game before creating another.");
        }

        var ruleErrors = command.Rules.Validate();
        if (ruleErrors.Count > 0)
        {
            return CommandResult.Reject(state, string.Join(" ", ruleErrors));
        }

        var homeName = NormalizeName(command.HomeName);
        var awayName = NormalizeName(command.AwayName);

        if (homeName is null || awayName is null)
        {
            return CommandResult.Reject(
                state,
                "Team names must contain between 1 and 32 characters.");
        }

        var homeColor = NormalizeColor(command.HomeColorHex);
        var awayColor = NormalizeColor(command.AwayColorHex);

        if (homeColor is null || awayColor is null)
        {
            return CommandResult.Reject(
                state,
                "Team colors must use six-digit hexadecimal notation, for example #FF5252.");
        }

        var created = new GameCreatedEvent(
            createMetadata(),
            Guid.NewGuid(),
            command.Metadata,
            command.Rules,
            homeName,
            awayName,
            homeColor,
            awayColor);

        return Accept(history, created);
    }

    private static CommandResult AdjustScore(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        AdjustScoreCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        if (command.Delta is not (-2 or -1 or 1 or 2))
        {
            return CommandResult.Reject(state, "Score adjustments must be +1, +2, -1, or -2.");
        }

        var before = state.GetTeam(command.Team).Score;
        var after = before + command.Delta;

        if (after < 0)
        {
            return CommandResult.Reject(state, "A score cannot be negative.");
        }

        var scoreAdjusted = new ScoreAdjustedEvent(
            createMetadata(),
            command.Team,
            command.Delta,
            before,
            after);
        var overtimePointsAfter = state.GetTeam(command.Team).OvertimePoints +
            command.Delta;
        if (state.Stage == MatchStage.Overtime &&
            command.Delta > 0 &&
            overtimePointsAfter >= state.Rules.OvertimeWinningPoints)
        {
            var homeScore = command.Team == TeamSide.Home
                ? after
                : state.Home.Score;
            var awayScore = command.Team == TeamSide.Away
                ? after
                : state.Away.Score;
            return Accept(
                history,
                [
                    scoreAdjusted,
                    new GameEndedEvent(createMetadata(), homeScore, awayScore),
                ]);
        }

        return Accept(history, scoreAdjusted);
    }

    private static CommandResult AdjustFoul(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        AdjustFoulCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        if (command.Delta is not (-1 or 1))
        {
            return CommandResult.Reject(state, "Fouls can only be adjusted by one.");
        }

        var before = state.GetTeam(command.Team).Fouls;
        var after = before + command.Delta;

        if (after < 0)
        {
            return CommandResult.Reject(state, "A foul count cannot be negative.");
        }

        return Accept(
            history,
            new FoulAdjustedEvent(
                createMetadata(),
                command.Team,
                command.Delta,
                before,
                after));
    }

    private static CommandResult ChangeTeamName(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        ChangeTeamNameCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        var name = NormalizeName(command.Name);
        if (name is null)
        {
            return CommandResult.Reject(
                state,
                "Team names must contain between 1 and 32 characters.");
        }

        var before = state.GetTeam(command.Team).Name;
        if (string.Equals(before, name, StringComparison.Ordinal))
        {
            return CommandResult.Reject(state, "The team name is unchanged.");
        }

        return Accept(
            history,
            new TeamNameChangedEvent(createMetadata(), command.Team, before, name));
    }

    private static CommandResult ChangeTeamColor(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        ChangeTeamColorCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        var color = NormalizeColor(command.ColorHex);
        if (color is null)
        {
            return CommandResult.Reject(
                state,
                "Team colors must use six-digit hexadecimal notation, for example #FF5252.");
        }

        var before = state.GetTeam(command.Team).ColorHex;
        if (string.Equals(before, color, StringComparison.Ordinal))
        {
            return CommandResult.Reject(state, "The team color is unchanged.");
        }

        return Accept(
            history,
            new TeamColorChangedEvent(createMetadata(), command.Team, before, color));
    }

    private static CommandResult SetLinkedClocksRunning(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        SetLinkedClocksRunningCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        if (command.IsRunning &&
            (state.GameClock.Remaining <= TimeSpan.Zero ||
             state.ShotClock.Remaining <= TimeSpan.Zero))
        {
            return CommandResult.Reject(
                state,
                "Reset any clock at zero before starting linked clocks.");
        }

        var events = new List<MatchEvent>();
        AddRunningEvent(
            state.GameClock,
            ClockKind.Game,
            command.IsRunning,
            createMetadata,
            events);
        AddRunningEvent(
            state.ShotClock,
            ClockKind.Shot,
            command.IsRunning,
            createMetadata,
            events);

        return events.Count == 0
            ? CommandResult.Reject(
                state,
                command.IsRunning ? "Both clocks are already running." : "Both clocks are paused.")
            : Accept(history, events);
    }

    private static CommandResult SwapTeams(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        return rejection is not null
            ? CommandResult.Reject(state, rejection)
            : Accept(history, new TeamsSwappedEvent(createMetadata()));
    }

    private static CommandResult SetClockRunning(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        SetClockRunningCommand command,
        Func<EventMetadata> createMetadata) =>
        SetLinkedClocksRunning(
            state,
            history,
            new SetLinkedClocksRunningCommand(command.IsRunning, command.Source),
            createMetadata);

    private static CommandResult AdjustClock(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        AdjustClockCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        if (command.Delta == TimeSpan.Zero)
        {
            return CommandResult.Reject(state, "Clock adjustment cannot be zero.");
        }

        var clock = state.GetClock(command.Clock);
        var maximum = command.Clock == ClockKind.Game
            ? MaximumGameClock
            : state.Rules.ShotClockDuration;
        var after = Clamp(clock.Remaining + command.Delta, TimeSpan.Zero, maximum);

        if (after == clock.Remaining)
        {
            return CommandResult.Reject(state, "The clock is already at its allowed limit.");
        }

        var events = new List<MatchEvent>();
        AddPauseForOtherClockIfNeeded(
            state,
            command.Clock,
            clock.IsRunning && after > TimeSpan.Zero,
            createMetadata,
            events);
        events.Add(
            new ClockChangedEvent(
                createMetadata(),
                command.Clock,
                ClockOperation.Adjusted,
                clock.Remaining,
                after,
                clock.IsRunning,
                clock.IsRunning && after > TimeSpan.Zero));
        return Accept(history, events);
    }

    private static CommandResult SetClock(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        SetClockCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        var maximum = command.Clock == ClockKind.Game
            ? MaximumGameClock
            : state.Rules.ShotClockDuration;

        if (command.Remaining < TimeSpan.Zero || command.Remaining > maximum)
        {
            return CommandResult.Reject(
                state,
                $"Clock time must be between zero and {maximum}.");
        }

        var clock = state.GetClock(command.Clock);
        var isRunning = !command.Stop && clock.IsRunning && command.Remaining > TimeSpan.Zero;

        var events = new List<MatchEvent>();
        AddPauseForOtherClockIfNeeded(
            state,
            command.Clock,
            isRunning,
            createMetadata,
            events);
        events.Add(
            new ClockChangedEvent(
                createMetadata(),
                command.Clock,
                ClockOperation.Set,
                clock.Remaining,
                command.Remaining,
                clock.IsRunning,
                isRunning));
        return Accept(history, events);
    }

    private static CommandResult ResetClock(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        ResetClockCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        var clock = state.GetClock(command.Clock);
        var resetValue = command.Clock == ClockKind.Game
            ? state.Rules.RegularDuration
            : state.Rules.ShotClockDuration;
        var isRunning = !command.Stop && clock.IsRunning;

        var events = new List<MatchEvent>();
        AddPauseForOtherClockIfNeeded(
            state,
            command.Clock,
            isRunning,
            createMetadata,
            events);
        events.Add(
            new ClockChangedEvent(
                createMetadata(),
                command.Clock,
                ClockOperation.Reset,
                clock.Remaining,
                resetValue,
                clock.IsRunning,
                isRunning));
        return Accept(history, events);
    }

    private static CommandResult ExpireClock(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        ExpireClockCommand command,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        var clock = state.GetClock(command.Clock);
        if (!clock.IsRunning || clock.HasExpired)
        {
            return CommandResult.Reject(state, "The clock is not eligible to expire.");
        }

        var buzzer = command.Clock == ClockKind.Game
            ? BuzzerKind.GameClock
            : BuzzerKind.ShotClock;
        var events = new List<MatchEvent>
        {
            new ClockExpiredEvent(createMetadata(), command.Clock, clock.Remaining),
        };
        AddPauseForOtherClockIfNeeded(
            state,
            command.Clock,
            willKeepRunning: false,
            createMetadata,
            events);
        events.Add(new BuzzerTriggeredEvent(createMetadata(), buzzer));

        return Accept(history, events);
    }

    private static CommandResult StartOvertime(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        Func<EventMetadata> createMetadata)
    {
        if (state.Stage != MatchStage.Regular ||
            state.PendingDecision != PendingDecision.StartOvertime)
        {
            return CommandResult.Reject(
                state,
                "Overtime can only start after regulation expires with a tied score.");
        }

        return Accept(
            history,
            new OvertimeStartedEvent(
                createMetadata(),
                state.Rules.ShotClockDuration,
                state.Metadata.GetOvertimePossession()));
    }

    private static CommandResult ClearPendingDecision(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        Func<EventMetadata> createMetadata)
    {
        if (state.PendingDecision == PendingDecision.None)
        {
            return CommandResult.Reject(state, "There is no pending decision to clear.");
        }

        return Accept(
            history,
            new PendingDecisionClearedEvent(createMetadata(), state.PendingDecision));
    }

    private static CommandResult EndGame(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        return Accept(
            history,
            new GameEndedEvent(
                createMetadata(),
                state.Home.Score,
                state.Away.Score));
    }

    private static CommandResult Undo(
        MatchState state,
        IReadOnlyList<MatchEvent> history,
        Func<EventMetadata> createMetadata)
    {
        var rejection = EnsurePlayable(state);
        if (rejection is not null)
        {
            return CommandResult.Reject(state, rejection);
        }

        var revertedIds = history
            .OfType<EventRevertedEvent>()
            .Select(matchEvent => matchEvent.TargetEventId)
            .ToHashSet();
        var target = history
            .Reverse()
            .FirstOrDefault(matchEvent =>
                !revertedIds.Contains(matchEvent.EventId) &&
                IsUndoable(matchEvent));

        if (target is null)
        {
            return CommandResult.Reject(state, "There is no action available to undo.");
        }

        if (target is ClockChangedEvent
            {
                Operation: ClockOperation.Started or ClockOperation.Paused,
            })
        {
            return CommandResult.Reject(
                state,
                "Clock start and pause actions cannot be undone; use the clock control.");
        }

        return Accept(
            history,
            new EventRevertedEvent(createMetadata(), target.EventId));
    }

    private static bool IsUndoable(MatchEvent matchEvent) =>
        matchEvent is
            ScoreAdjustedEvent or
            FoulAdjustedEvent or
            TeamNameChangedEvent or
            TeamColorChangedEvent or
            TeamsSwappedEvent or
            ClockChangedEvent;

    private static void AddRunningEvent(
        ClockState clock,
        ClockKind kind,
        bool isRunning,
        Func<EventMetadata> createMetadata,
        List<MatchEvent> events)
    {
        if (clock.IsRunning == isRunning)
        {
            return;
        }

        events.Add(
            new ClockChangedEvent(
                createMetadata(),
                kind,
                isRunning ? ClockOperation.Started : ClockOperation.Paused,
                clock.Remaining,
                clock.Remaining,
                clock.IsRunning,
                isRunning));
    }

    private static void AddPauseForOtherClockIfNeeded(
        MatchState state,
        ClockKind changedClock,
        bool willKeepRunning,
        Func<EventMetadata> createMetadata,
        List<MatchEvent> events)
    {
        if (willKeepRunning)
        {
            return;
        }

        var otherClockKind = changedClock == ClockKind.Game
            ? ClockKind.Shot
            : ClockKind.Game;
        var otherClock = state.GetClock(otherClockKind);
        AddRunningEvent(
            otherClock,
            otherClockKind,
            isRunning: false,
            createMetadata,
            events);
    }

    private static CommandResult Accept(
        IReadOnlyList<MatchEvent> history,
        MatchEvent matchEvent) =>
        Accept(history, [matchEvent]);

    private static CommandResult Accept(
        IReadOnlyList<MatchEvent> history,
        IReadOnlyList<MatchEvent> newEvents)
    {
        var allEvents = history.Concat(newEvents);
        var state = MatchReducer.Replay(allEvents);
        return CommandResult.Accept(state, newEvents);
    }

    private static string? EnsurePlayable(MatchState state)
    {
        if (!state.IsCreated)
        {
            return "Create a game first.";
        }

        return state.Stage == MatchStage.Final
            ? "The game is final."
            : null;
    }

    private static string? NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        return normalized.Length <= 32 ? normalized : null;
    }

    private static string? NormalizeColor(string color)
    {
        if (color.Length != 7 || color[0] != '#')
        {
            return null;
        }

        for (var index = 1; index < color.Length; index++)
        {
            if (!Uri.IsHexDigit(color[index]))
            {
                return null;
            }
        }

        return color.ToUpperInvariant();
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }
}
