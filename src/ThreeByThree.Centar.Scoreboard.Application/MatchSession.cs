using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application;

public sealed class MatchSession : IDisposable
{
    private static readonly TimeSpan ShotClockWarningRemaining = TimeSpan.FromSeconds(5);
    private readonly object gate = new();
    private readonly MatchEngine engine;
    private readonly TimeProvider timeProvider;
    private readonly RuntimeCountdownClock gameClock;
    private readonly RuntimeCountdownClock shotClock;
    private readonly long sessionStartedAtTimestamp;
    private readonly List<MatchEvent> history = [];

    private MatchState eventState = MatchState.Empty;
    private ITimer? gameExpirationTimer;
    private ITimer? shotExpirationTimer;
    private ITimer? shotWarningTimer;
    private bool shotClockWarningPlayed;
    private bool isDisposed;

    public MatchSession(MatchEngine engine, TimeProvider timeProvider)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        gameClock = new RuntimeCountdownClock(timeProvider);
        shotClock = new RuntimeCountdownClock(timeProvider);
        sessionStartedAtTimestamp = timeProvider.GetTimestamp();
        gameClock.Synchronize(eventState.GameClock);
        shotClock.Synchronize(eventState.ShotClock);
    }

    public event EventHandler<MatchSnapshotChangedEventArgs>? SnapshotChanged;

    public event EventHandler<MatchEventsCommittedEventArgs>? EventsCommitted;

    public event EventHandler<MatchSessionErrorEventArgs>? BackgroundError;

    public MatchState Snapshot
    {
        get
        {
            lock (gate)
            {
                ThrowIfDisposed();
                return ProjectSnapshotUnsafe();
            }
        }
    }

    public IReadOnlyList<MatchEvent> History
    {
        get
        {
            lock (gate)
            {
                ThrowIfDisposed();
                return history.ToArray();
            }
        }
    }

    public MatchSessionCheckpoint CaptureCheckpoint()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            return new MatchSessionCheckpoint(
                ProjectSnapshotUnsafe(),
                history.ToArray());
        }
    }

    public CommandResult Execute(MatchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        MatchState snapshot;
        CommandResult result;

        lock (gate)
        {
            ThrowIfDisposed();

            var before = ProjectSnapshotUnsafe();
            var startsNewGame =
                command is CreateGameCommand &&
                before.Stage == MatchStage.Final;
            IReadOnlyList<MatchEvent> commandHistory =
                startsNewGame ? [] : history;
            var engineResult = engine.Execute(
                before,
                commandHistory,
                command,
                timeProvider.GetUtcNow(),
                timeProvider.GetElapsedTime(
                    sessionStartedAtTimestamp,
                    timeProvider.GetTimestamp()));

            if (!engineResult.IsAccepted)
            {
                return CommandResult.Reject(before, engineResult.Message);
            }

            if (startsNewGame)
            {
                history.Clear();
            }

            history.AddRange(engineResult.Events);
            ApplyClockEffectsUnsafe(before, engineResult);
            snapshot = ProjectSnapshotUnsafe();
            result = CommandResult.Accept(snapshot, engineResult.Events, engineResult.Message);
        }

        EventsCommitted?.Invoke(
            this,
            new MatchEventsCommittedEventArgs(snapshot, result.Events));
        SnapshotChanged?.Invoke(this, new MatchSnapshotChangedEventArgs(snapshot));
        return result;
    }

    public CommandResult Recover(
        IReadOnlyList<MatchEvent> recoveredHistory,
        MatchState persistedSnapshot) =>
        OpenSavedGameCore(
            recoveredHistory,
            persistedSnapshot,
            replaceCurrent: false,
            allowFinished: false);

    public CommandResult OpenSavedGame(
        IReadOnlyList<MatchEvent> savedHistory,
        MatchState persistedSnapshot,
        bool replaceCurrent = false) =>
        OpenSavedGameCore(
            savedHistory,
            persistedSnapshot,
            replaceCurrent,
            allowFinished: true);

    private CommandResult OpenSavedGameCore(
        IReadOnlyList<MatchEvent> savedHistory,
        MatchState persistedSnapshot,
        bool replaceCurrent,
        bool allowFinished)
    {
        ArgumentNullException.ThrowIfNull(savedHistory);
        ArgumentNullException.ThrowIfNull(persistedSnapshot);

        MatchState snapshot;
        List<MatchEvent> recoveryEvents;

        lock (gate)
        {
            ThrowIfDisposed();

            if (!replaceCurrent && (history.Count > 0 || eventState.IsCreated))
            {
                return CommandResult.Reject(
                    ProjectSnapshotUnsafe(),
                    "A match is already loaded.");
            }

            var orderedHistory = savedHistory
                .OrderBy(matchEvent => matchEvent.Sequence)
                .ToArray();
            var replayed = MatchReducer.Replay(orderedHistory);
            if (!replayed.IsCreated ||
                replayed.GameId != persistedSnapshot.GameId ||
                replayed.Stage != persistedSnapshot.Stage ||
                replayed.Home.Score != persistedSnapshot.Home.Score ||
                replayed.Away.Score != persistedSnapshot.Away.Score ||
                replayed.Home.Fouls != persistedSnapshot.Home.Fouls ||
                replayed.Away.Fouls != persistedSnapshot.Away.Fouls ||
                replayed.StartingPossession != persistedSnapshot.StartingPossession ||
                (!allowFinished && replayed.Stage == MatchStage.Final))
            {
                return CommandResult.Reject(
                    ProjectSnapshotUnsafe(),
                    allowFinished
                        ? "The saved game document is invalid."
                        : "The recovery document is not an unfinished match.");
            }

            history.Clear();
            history.AddRange(orderedHistory);
            recoveryEvents = replayed.Stage == MatchStage.Final
                ? []
                : CreateRecoveryClockEvents(replayed, persistedSnapshot, history);
            history.AddRange(recoveryEvents);
            eventState = MatchReducer.Replay(history);
            shotClockWarningPlayed =
                eventState.ShotClock.HasExpired ||
                eventState.ShotClock.Remaining <= TimeSpan.Zero;
            gameClock.Synchronize(eventState.GameClock);
            shotClock.Synchronize(eventState.ShotClock);
            ScheduleExpirationUnsafe(ClockKind.Game);
            ScheduleExpirationUnsafe(ClockKind.Shot);
            snapshot = ProjectSnapshotUnsafe();
        }

        if (recoveryEvents.Count > 0)
        {
            EventsCommitted?.Invoke(
                this,
                new MatchEventsCommittedEventArgs(snapshot, recoveryEvents));
        }

        SnapshotChanged?.Invoke(this, new MatchSnapshotChangedEventArgs(snapshot));
        return CommandResult.Accept(
            snapshot,
            recoveryEvents,
            snapshot.Stage == MatchStage.Final
                ? "Opened finished game."
                : "Opened unfinished game in a paused state.");
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            gameExpirationTimer?.Dispose();
            shotExpirationTimer?.Dispose();
            shotWarningTimer?.Dispose();
            gameExpirationTimer = null;
            shotExpirationTimer = null;
            shotWarningTimer = null;
        }
    }

    private void ApplyClockEffectsUnsafe(MatchState before, CommandResult result)
    {
        var affectedClocks = GetAffectedClocks(result.Events);
        var nextState = result.State;

        if (!affectedClocks.Contains(ClockKind.Game))
        {
            nextState = nextState with { GameClock = before.GameClock };
        }

        if (!affectedClocks.Contains(ClockKind.Shot))
        {
            nextState = nextState with { ShotClock = before.ShotClock };
        }

        ApplyClockUndoCompensation(before, result.Events, ref nextState, affectedClocks);
        eventState = nextState;

        if (affectedClocks.Contains(ClockKind.Game))
        {
            gameClock.Synchronize(eventState.GameClock);
            ScheduleExpirationUnsafe(ClockKind.Game);
        }

        if (affectedClocks.Contains(ClockKind.Shot))
        {
            UpdateShotClockWarningStateUnsafe(result.Events, nextState.ShotClock);
            shotClock.Synchronize(eventState.ShotClock);
            ScheduleExpirationUnsafe(ClockKind.Shot);
        }
    }

    private void ApplyClockUndoCompensation(
        MatchState before,
        IReadOnlyList<MatchEvent> events,
        ref MatchState nextState,
        HashSet<ClockKind> affectedClocks)
    {
        var reversion = events.OfType<EventRevertedEvent>().SingleOrDefault();
        if (reversion is null)
        {
            return;
        }

        var target = history
            .FirstOrDefault(matchEvent => matchEvent.EventId == reversion.TargetEventId);
        if (target is not ClockChangedEvent
            {
                Operation: ClockOperation.Adjusted or ClockOperation.Set or ClockOperation.Reset,
            } clockChange)
        {
            return;
        }

        affectedClocks.Add(clockChange.Clock);
        var current = before.GetClock(clockChange.Clock);
        var maximum = clockChange.Clock == ClockKind.Game
            ? TimeSpan.FromMinutes(100) - TimeSpan.FromMilliseconds(100)
            : before.Rules.ShotClockDuration;
        var remaining = Clamp(
            current.Remaining + (clockChange.Before - clockChange.After),
            TimeSpan.Zero,
            maximum);
        var compensated = current with
        {
            Remaining = remaining,
            IsRunning = current.IsRunning && remaining > TimeSpan.Zero,
            HasExpired = false,
        };

        nextState = clockChange.Clock == ClockKind.Game
            ? nextState with { GameClock = compensated }
            : nextState with { ShotClock = compensated };
    }

    private HashSet<ClockKind> GetAffectedClocks(IReadOnlyList<MatchEvent> events)
    {
        var affected = events
            .OfType<ClockChangedEvent>()
            .Select(matchEvent => matchEvent.Clock)
            .ToHashSet();

        foreach (var matchEvent in events)
        {
            switch (matchEvent)
            {
                case GameCreatedEvent or OvertimeStartedEvent or GameEndedEvent:
                    affected.Add(ClockKind.Game);
                    affected.Add(ClockKind.Shot);
                    break;
                case ClockExpiredEvent { Clock: ClockKind.Game }:
                    affected.Add(ClockKind.Game);
                    affected.Add(ClockKind.Shot);
                    break;
                case ClockExpiredEvent { Clock: ClockKind.Shot }:
                    affected.Add(ClockKind.Game);
                    affected.Add(ClockKind.Shot);
                    break;
                case EventRevertedEvent reversion:
                    var target = history.FirstOrDefault(
                        historicalEvent => historicalEvent.EventId == reversion.TargetEventId);
                    if (target is ClockChangedEvent clock)
                    {
                        affected.Add(clock.Clock);
                    }

                    break;
            }
        }

        return affected;
    }

    private MatchState ProjectSnapshotUnsafe() =>
        eventState with
        {
            GameClock = gameClock.Project(eventState.GameClock),
            ShotClock = shotClock.Project(eventState.ShotClock),
        };

    private void ScheduleExpirationUnsafe(ClockKind kind)
    {
        var clock = kind == ClockKind.Game ? gameClock : shotClock;
        var currentTimer = kind == ClockKind.Game
            ? gameExpirationTimer
            : shotExpirationTimer;
        currentTimer?.Dispose();

        ITimer? replacement = null;
        if (clock.IsRunning && clock.Remaining > TimeSpan.Zero)
        {
            replacement = timeProvider.CreateTimer(
                static state =>
                {
                    var timerState = (ExpirationTimerState)state!;
                    timerState.Session.OnExpirationTimer(timerState.Clock);
                },
                new ExpirationTimerState(this, kind),
                clock.Remaining,
                Timeout.InfiniteTimeSpan);
        }

        if (kind == ClockKind.Game)
        {
            gameExpirationTimer = replacement;
        }
        else
        {
            shotExpirationTimer = replacement;
            ScheduleShotClockWarningUnsafe();
        }
    }

    private void ScheduleShotClockWarningUnsafe()
    {
        shotWarningTimer?.Dispose();
        shotWarningTimer = null;

        if (!shotClock.IsRunning ||
            shotClock.Remaining <= TimeSpan.Zero ||
            shotClockWarningPlayed)
        {
            return;
        }

        var dueTime = shotClock.Remaining - ShotClockWarningRemaining;
        if (dueTime <= TimeSpan.Zero)
        {
            // Avoid a synchronous callback while a clock command is still
            // applying its state transition.
            dueTime = TimeSpan.FromMilliseconds(1);
        }

        shotWarningTimer = timeProvider.CreateTimer(
            static state => ((MatchSession)state!).OnShotClockWarningTimer(),
            this,
            dueTime,
            Timeout.InfiniteTimeSpan);
    }

    private void OnShotClockWarningTimer()
    {
        try
        {
            lock (gate)
            {
                if (isDisposed ||
                    !shotClock.IsRunning ||
                    shotClockWarningPlayed ||
                    shotClock.Remaining <= TimeSpan.Zero)
                {
                    return;
                }

                if (shotClock.Remaining > ShotClockWarningRemaining)
                {
                    ScheduleShotClockWarningUnsafe();
                    return;
                }

                shotClockWarningPlayed = true;
                shotWarningTimer?.Dispose();
                shotWarningTimer = null;
            }

            Execute(
                new TriggerBuzzerCommand(
                    CommandSource.ClockScheduler,
                    BuzzerKind.ShotClockWarning));
        }
        catch (Exception exception)
        {
            BackgroundError?.Invoke(this, new MatchSessionErrorEventArgs(exception));
        }
    }

    private void OnExpirationTimer(ClockKind kind)
    {
        try
        {
            lock (gate)
            {
                if (isDisposed)
                {
                    return;
                }

                var clock = kind == ClockKind.Game ? gameClock : shotClock;
                if (!clock.IsRunning)
                {
                    return;
                }

                if (clock.Remaining > TimeSpan.Zero)
                {
                    ScheduleExpirationUnsafe(kind);
                    return;
                }
            }

            Execute(new ExpireClockCommand(kind));
        }
        catch (Exception exception)
        {
            BackgroundError?.Invoke(this, new MatchSessionErrorEventArgs(exception));
        }
    }

    private void UpdateShotClockWarningStateUnsafe(
        IReadOnlyList<MatchEvent> events,
        ClockState nextShotClock)
    {
        if (events.Any(matchEvent =>
                matchEvent is GameCreatedEvent or OvertimeStartedEvent ||
                matchEvent is ClockChangedEvent
                {
                    Clock: ClockKind.Shot,
                    Operation: ClockOperation.Set or ClockOperation.Reset,
                }))
        {
            shotClockWarningPlayed = false;
        }

        if (nextShotClock.Remaining > ShotClockWarningRemaining)
        {
            shotClockWarningPlayed = false;
        }

        if (events.Any(matchEvent =>
                matchEvent is ClockExpiredEvent { Clock: ClockKind.Shot }))
        {
            shotClockWarningPlayed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private List<MatchEvent> CreateRecoveryClockEvents(
        MatchState replayed,
        MatchState persistedSnapshot,
        List<MatchEvent> baseHistory)
    {
        var events = new List<MatchEvent>(2);
        var sequence = baseHistory.Count == 0
            ? 0
            : baseHistory.Max(matchEvent => matchEvent.Sequence);
        var recordedAtUtc = timeProvider.GetUtcNow();
        var elapsed = timeProvider.GetElapsedTime(
                sessionStartedAtTimestamp,
                timeProvider.GetTimestamp())
            .TotalMilliseconds;

        AddRecoveryClockEvent(
            ClockKind.Game,
            replayed.GameClock,
            persistedSnapshot.GameClock,
            ref sequence,
            recordedAtUtc,
            elapsed,
            events);
        AddRecoveryClockEvent(
            ClockKind.Shot,
            replayed.ShotClock,
            persistedSnapshot.ShotClock,
            ref sequence,
            recordedAtUtc,
            elapsed,
            events);
        return events;
    }

    private static void AddRecoveryClockEvent(
        ClockKind kind,
        ClockState replayedClock,
        ClockState persistedClock,
        ref long sequence,
        DateTimeOffset recordedAtUtc,
        double elapsedMilliseconds,
        List<MatchEvent> events)
    {
        var remaining = persistedClock.Remaining > TimeSpan.Zero
            ? persistedClock.Remaining
            : TimeSpan.Zero;
        if (replayedClock.Remaining == remaining && !replayedClock.IsRunning)
        {
            return;
        }

        events.Add(
            new ClockChangedEvent(
                new EventMetadata(
                    Guid.NewGuid(),
                    ++sequence,
                    recordedAtUtc,
                    (long)elapsedMilliseconds,
                    CommandSource.Recovery),
                kind,
                ClockOperation.Set,
                replayedClock.Remaining,
                remaining,
                replayedClock.IsRunning,
                IsRunning: false));
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private sealed record ExpirationTimerState(MatchSession Session, ClockKind Clock);
}
