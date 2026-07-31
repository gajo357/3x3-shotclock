using Microsoft.Extensions.Time.Testing;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Tests;

[TestClass]
public sealed class MatchSessionTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 27, 18, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void Snapshot_RunningClocks_UsesMonotonicElapsedTimeWithoutTickEvents()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        var eventCount = session.History.Count;

        timeProvider.Advance(TimeSpan.FromMilliseconds(1250));
        var snapshot = session.Snapshot;

        Assert.AreEqual(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1250),
            snapshot.GameClock.Remaining);
        Assert.AreEqual(TimeSpan.FromSeconds(12) - TimeSpan.FromMilliseconds(1250),
            snapshot.ShotClock.Remaining);
        Assert.IsTrue(snapshot.GameClock.IsRunning);
        Assert.IsTrue(snapshot.ShotClock.IsRunning);
        Assert.HasCount(eventCount, session.History);
    }

    [TestMethod]
    public void Execute_PauseLinkedClocks_CapturesCalculatedRemaining()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(3.5));

        var result = session.Execute(new SetLinkedClocksRunningCommand(false));
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var later = session.Snapshot;

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(TimeSpan.FromSeconds(596.5), result.State.GameClock.Remaining);
        Assert.AreEqual(TimeSpan.FromSeconds(8.5), result.State.ShotClock.Remaining);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        Assert.AreEqual(result.State.GameClock.Remaining, later.GameClock.Remaining);
        Assert.AreEqual(result.State.ShotClock.Remaining, later.ShotClock.Remaining);
    }

    [TestMethod]
    public void Execute_NonClockCommandWhileRunning_DoesNotRewindClockAnchors()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var score = session.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var snapshot = session.Snapshot;

        Assert.IsTrue(score.IsAccepted);
        Assert.AreEqual(2, snapshot.Home.Score);
        Assert.AreEqual(TimeSpan.FromSeconds(594), snapshot.GameClock.Remaining);
        Assert.AreEqual(TimeSpan.FromSeconds(6), snapshot.ShotClock.Remaining);
    }

    [TestMethod]
    public void Timer_RunningShotClockCrossesFiveSeconds_EmitsWarningExactlyOnce()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        var committedEvents = new List<MatchEvent>();
        session.EventsCommitted += (_, args) => committedEvents.AddRange(args.Events);

        timeProvider.Advance(TimeSpan.FromMilliseconds(6999));
        var beforeThreshold = session.Snapshot;
        var warningsBeforeThreshold = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var atThreshold = session.Snapshot;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var belowThreshold = session.Snapshot;
        var warningEvents = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();
        var committedWarnings = committedEvents
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();

        Assert.AreEqual(TimeSpan.FromMilliseconds(5001), beforeThreshold.ShotClock.Remaining);
        Assert.IsEmpty(warningsBeforeThreshold);
        Assert.AreEqual(TimeSpan.FromSeconds(5), atThreshold.ShotClock.Remaining);
        Assert.IsTrue(atThreshold.ShotClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(4), belowThreshold.ShotClock.Remaining);
        Assert.HasCount(1, warningEvents);
        Assert.AreEqual(CommandSource.ClockScheduler, warningEvents[0].Metadata.Source);
        Assert.HasCount(1, committedWarnings);
        Assert.AreEqual(warningEvents[0].EventId, committedWarnings[0].EventId);
    }

    [TestMethod]
    [DataRow(4000)]
    [DataRow(5000)]
    public void Execute_StartShotClockAtOrBelowWarningThreshold_EmitsWarningAfterMinimumDelay(
        int remainingMilliseconds)
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateSession(timeProvider);
        session.Execute(
            new SetClockCommand(
                ClockKind.Shot,
                TimeSpan.FromMilliseconds(remainingMilliseconds),
                Stop: true));

        var start = session.Execute(new SetClockRunningCommand(ClockKind.Shot, true));
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var warningEvents = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();

        Assert.IsTrue(start.IsAccepted);
        Assert.IsTrue(session.Snapshot.ShotClock.IsRunning);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(remainingMilliseconds - 1),
            session.Snapshot.ShotClock.Remaining);
        Assert.HasCount(1, warningEvents);
        Assert.AreEqual(CommandSource.ClockScheduler, warningEvents[0].Metadata.Source);
    }

    [TestMethod]
    public void Timer_ShotClockExpires_StopsBothClocksExactlyOnce()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        var committedEvents = new List<MatchEvent>();
        session.EventsCommitted += (_, args) => committedEvents.AddRange(args.Events);

        timeProvider.Advance(TimeSpan.FromSeconds(12));
        var expired = session.Snapshot;
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var later = session.Snapshot;

        Assert.AreEqual(TimeSpan.Zero, expired.ShotClock.Remaining);
        Assert.IsFalse(expired.ShotClock.IsRunning);
        Assert.IsTrue(expired.ShotClock.HasExpired);
        Assert.AreEqual(TimeSpan.FromSeconds(588), expired.GameClock.Remaining);
        Assert.IsFalse(expired.GameClock.IsRunning);
        Assert.AreEqual(expired.GameClock.Remaining, later.GameClock.Remaining);
        Assert.IsFalse(later.GameClock.IsRunning);
        Assert.HasCount(1, session.History.OfType<ClockExpiredEvent>()
            .Where(matchEvent => matchEvent.Clock == ClockKind.Shot)
            .ToArray());
        Assert.HasCount(1, session.History.OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClock)
            .ToArray());
        Assert.HasCount(1, committedEvents.OfType<ClockExpiredEvent>().ToArray());
    }

    [TestMethod]
    public void Timer_DirectAdvanceToShotClockExpiration_SuppressesLateWarningAndEmitsExpiryOnce()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(12));
        var warnings = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();
        var expiryBuzzers = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClock)
            .ToArray();
        var expirations = session.History
            .OfType<ClockExpiredEvent>()
            .Where(matchEvent => matchEvent.Clock == ClockKind.Shot)
            .ToArray();

        Assert.IsEmpty(warnings);
        Assert.HasCount(1, expiryBuzzers);
        Assert.HasCount(1, expirations);
        Assert.AreEqual(TimeSpan.Zero, session.Snapshot.ShotClock.Remaining);
        Assert.IsTrue(session.Snapshot.ShotClock.HasExpired);
        Assert.IsFalse(session.Snapshot.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(588), session.Snapshot.GameClock.Remaining);
    }

    [TestMethod]
    public void Execute_ResetExpiredShotClockThenRestart_RearmsWarningAndExpiry()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(7));
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var reset = session.Execute(
            new ResetClockCommand(ClockKind.Shot, Stop: false));
        var restarted = session.Execute(
            new SetClockRunningCommand(ClockKind.Shot, true));
        timeProvider.Advance(TimeSpan.FromSeconds(7));
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var eventCountAfterSecondExpiry = session.History.Count;
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var warnings = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();
        var expiryBuzzers = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClock)
            .ToArray();
        var expirations = session.History
            .OfType<ClockExpiredEvent>()
            .Where(matchEvent => matchEvent.Clock == ClockKind.Shot)
            .ToArray();

        Assert.IsTrue(reset.IsAccepted);
        var resetEvent = Assert.IsInstanceOfType<ClockChangedEvent>(reset.Events[0]);
        Assert.AreEqual(ClockOperation.Reset, resetEvent.Operation);
        Assert.AreEqual(TimeSpan.FromSeconds(12), reset.State.ShotClock.Remaining);
        Assert.IsFalse(reset.State.ShotClock.IsRunning);
        Assert.IsTrue(restarted.IsAccepted);
        Assert.HasCount(2, warnings);
        Assert.HasCount(2, expiryBuzzers);
        Assert.HasCount(2, expirations);
        Assert.IsLessThan(warnings[1].Sequence, expiryBuzzers[0].Sequence);
        Assert.IsLessThan(expiryBuzzers[1].Sequence, warnings[1].Sequence);
        Assert.HasCount(eventCountAfterSecondExpiry, session.History);
        Assert.AreEqual(TimeSpan.Zero, session.Snapshot.ShotClock.Remaining);
        Assert.IsTrue(session.Snapshot.ShotClock.HasExpired);
    }

    [TestMethod]
    public void Execute_PauseAndResumeBelowFiveSeconds_DoesNotDuplicateWarning()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(8));

        var paused = session.Execute(
            new SetClockRunningCommand(ClockKind.Shot, false));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var resumed = session.Execute(
            new SetClockRunningCommand(ClockKind.Shot, true));
        timeProvider.Advance(TimeSpan.FromSeconds(4));
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var warnings = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();
        var expiryBuzzers = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClock)
            .ToArray();

        Assert.IsTrue(paused.IsAccepted);
        Assert.AreEqual(TimeSpan.FromSeconds(4), paused.State.ShotClock.Remaining);
        Assert.AreEqual(TimeSpan.FromSeconds(592), paused.State.GameClock.Remaining);
        Assert.IsFalse(paused.State.GameClock.IsRunning);
        Assert.IsFalse(paused.State.ShotClock.IsRunning);
        Assert.IsTrue(resumed.IsAccepted);
        Assert.IsTrue(resumed.State.GameClock.IsRunning);
        Assert.IsTrue(resumed.State.ShotClock.IsRunning);
        Assert.HasCount(1, warnings);
        Assert.HasCount(1, expiryBuzzers);
        Assert.IsLessThan(expiryBuzzers[0].Sequence, warnings[0].Sequence);
        Assert.AreEqual(TimeSpan.Zero, session.Snapshot.ShotClock.Remaining);
        Assert.IsTrue(session.Snapshot.ShotClock.HasExpired);
        Assert.IsFalse(session.Snapshot.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(588), session.Snapshot.GameClock.Remaining);
    }

    [TestMethod]
    public void Timer_GameClockExpiresWithTie_StopsBothClocksAndRequestsOvertime()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(
            timeProvider,
            MatchRules.Fiba3x3 with
            {
                ShotClockDuration = TimeSpan.FromMinutes(11),
            });

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var snapshot = session.Snapshot;

        Assert.AreEqual(TimeSpan.Zero, snapshot.GameClock.Remaining);
        Assert.IsFalse(snapshot.GameClock.IsRunning);
        Assert.IsTrue(snapshot.GameClock.HasExpired);
        Assert.IsFalse(snapshot.ShotClock.IsRunning);
        Assert.AreEqual(PendingDecision.StartOvertime, snapshot.PendingDecision);
        Assert.HasCount(1, session.History.OfType<ClockExpiredEvent>()
            .Where(matchEvent => matchEvent.Clock == ClockKind.Game)
            .ToArray());
        Assert.HasCount(1, session.History.OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.GameClock)
            .ToArray());
    }

    [TestMethod]
    public void Execute_OvertimeShotClock_RunsAndExpiresWithoutStartingGameClock()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var rules = MatchRules.Fiba3x3 with
        {
            RegularDuration = TimeSpan.FromSeconds(1),
            GameClockTenthsThreshold = TimeSpan.FromSeconds(1),
        };
        using var session = CreateStartedSession(timeProvider, rules);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var overtime = session.Execute(new StartOvertimeCommand());
        var started = session.Execute(new SetLinkedClocksRunningCommand(true));
        timeProvider.Advance(TimeSpan.FromSeconds(7));
        var atWarning = session.Snapshot;
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var expired = session.Snapshot;
        var eventCountAfterExpiration = session.History.Count;
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        Assert.IsTrue(overtime.IsAccepted);
        Assert.IsTrue(started.IsAccepted);
        Assert.HasCount(1, started.Events);
        var startedEvent = Assert.IsInstanceOfType<ClockChangedEvent>(started.Events[0]);
        Assert.AreEqual(ClockKind.Shot, startedEvent.Clock);
        Assert.AreEqual(TimeSpan.Zero, atWarning.GameClock.Remaining);
        Assert.IsFalse(atWarning.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(5), atWarning.ShotClock.Remaining);
        Assert.IsTrue(atWarning.ShotClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, expired.GameClock.Remaining);
        Assert.IsFalse(expired.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, expired.ShotClock.Remaining);
        Assert.IsFalse(expired.ShotClock.IsRunning);
        Assert.IsTrue(expired.ShotClock.HasExpired);
        Assert.HasCount(1, session.History.OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray());
        Assert.HasCount(1, session.History.OfType<ClockExpiredEvent>()
            .Where(matchEvent => matchEvent.Clock == ClockKind.Shot)
            .ToArray());
        Assert.HasCount(1, session.History.OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClock)
            .ToArray());
        Assert.HasCount(eventCountAfterExpiration, session.History);
    }

    [TestMethod]
    public void Execute_ResetRunningShotClock_ReschedulesExpirationFromResetValue()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var reset = session.Execute(new ResetClockCommand(ClockKind.Shot, Stop: false));
        timeProvider.Advance(TimeSpan.FromSeconds(11.9));
        var beforeExpiration = session.Snapshot;
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        var expired = session.Snapshot;

        Assert.IsTrue(reset.IsAccepted);
        Assert.AreEqual(TimeSpan.FromSeconds(595), reset.State.GameClock.Remaining);
        Assert.IsTrue(reset.State.GameClock.IsRunning);
        Assert.IsTrue(reset.State.ShotClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), beforeExpiration.ShotClock.Remaining);
        Assert.IsTrue(beforeExpiration.ShotClock.IsRunning);
        Assert.IsTrue(beforeExpiration.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, expired.ShotClock.Remaining);
        Assert.IsFalse(expired.ShotClock.IsRunning);
        Assert.IsTrue(expired.ShotClock.HasExpired);
        Assert.AreEqual(TimeSpan.FromSeconds(583), expired.GameClock.Remaining);
        Assert.IsFalse(expired.GameClock.IsRunning);
    }

    [TestMethod]
    public void Execute_ResetBeforeWarning_CancelsOldTimerAndSchedulesWarningForNewCycle()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var reset = session.Execute(new ResetClockCommand(ClockKind.Shot, Stop: false));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var warningsAtOldThreshold = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var warningsAtNewThreshold = session.History
            .OfType<BuzzerTriggeredEvent>()
            .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClockWarning)
            .ToArray();

        Assert.IsTrue(reset.IsAccepted);
        Assert.AreEqual(TimeSpan.FromSeconds(12), reset.State.ShotClock.Remaining);
        Assert.IsTrue(reset.State.ShotClock.IsRunning);
        Assert.IsEmpty(warningsAtOldThreshold);
        Assert.HasCount(1, warningsAtNewThreshold);
        Assert.AreEqual(CommandSource.ClockScheduler, warningsAtNewThreshold[0].Metadata.Source);
        Assert.AreEqual(TimeSpan.FromSeconds(5), session.Snapshot.ShotClock.Remaining);
    }

    [TestMethod]
    public void Execute_UndoRunningClockAdjustment_AppliesInverseWithoutLosingElapsedTime()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateStartedSession(timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        session.Execute(new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(-1)));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var undo = session.Execute(new UndoLastActionCommand());

        Assert.IsTrue(undo.IsAccepted);
        Assert.AreEqual(TimeSpan.FromSeconds(9), undo.State.ShotClock.Remaining);
        Assert.IsTrue(undo.State.ShotClock.IsRunning);
        Assert.IsInstanceOfType<EventRevertedEvent>(undo.Events[0]);
    }

    [TestMethod]
    public void Execute_AcceptedCommand_PublishesCommittedEventsThenSnapshot()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var session = CreateSession(timeProvider);
        var notifications = new List<string>();
        session.EventsCommitted += (_, _) => notifications.Add("events");
        session.SnapshotChanged += (_, _) => notifications.Add("snapshot");

        var result = session.Execute(new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(2, notifications);
        Assert.AreEqual("events", notifications[0]);
        Assert.AreEqual("snapshot", notifications[1]);
    }

    [TestMethod]
    public void Dispose_SubsequentAccess_ThrowsObjectDisposedException()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var session = CreateSession(timeProvider);
        session.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = session.Snapshot);
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => session.Execute(new AdjustScoreCommand(TeamSide.Home, 1)));
        session.Dispose();
    }

    [TestMethod]
    public void OpenSavedGame_CompletedDocument_LoadsFinalReadOnlyState()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var original = CreateSession(timeProvider);
        Assert.IsTrue(original.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        var ended = original.Execute(new EndGameCommand());
        var document = GameDocument.Capture(
            original.Snapshot,
            original.History,
            timeProvider.GetUtcNow());
        using var opened = new MatchSession(new MatchEngine(), timeProvider);

        var result = opened.OpenSavedGame(document.Events, document.Snapshot);
        var historyBeforeMutation = opened.History.ToArray();
        var mutation = opened.Execute(
            new AdjustScoreCommand(TeamSide.Away, 1));

        Assert.IsTrue(ended.IsAccepted);
        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(MatchStage.Final, result.State.Stage);
        Assert.AreEqual(MatchStatus.Final, result.State.Status);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(0, result.State.Away.Score);
        Assert.IsEmpty(result.Events);
        Assert.IsFalse(mutation.IsAccepted);
        Assert.AreEqual(MatchStage.Final, mutation.State.Stage);
        Assert.AreEqual(0, mutation.State.Away.Score);
        CollectionAssert.AreEqual(
            historyBeforeMutation.Select(matchEvent => matchEvent.EventId).ToArray(),
            opened.History.Select(matchEvent => matchEvent.EventId).ToArray());
    }

    [TestMethod]
    public void OpenSavedGame_UnfinishedDocument_RecoversPausedAndCanContinue()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var original = CreateStartedSession(timeProvider);
        Assert.IsTrue(original.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var document = GameDocument.Capture(
            original.Snapshot,
            original.History,
            timeProvider.GetUtcNow());
        using var opened = new MatchSession(new MatchEngine(), timeProvider);

        var result = opened.OpenSavedGame(document.Events, document.Snapshot);
        var resumed = opened.Execute(new SetLinkedClocksRunningCommand(true));
        var continued = opened.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(MatchStatus.Paused, result.State.Status);
        Assert.AreEqual(document.Snapshot.GameClock.Remaining, result.State.GameClock.Remaining);
        Assert.AreEqual(document.Snapshot.ShotClock.Remaining, result.State.ShotClock.Remaining);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        Assert.HasCount(2, result.Events);
        Assert.IsTrue(result.Events.All(matchEvent =>
            matchEvent is ClockChangedEvent
            {
                Metadata.Source: CommandSource.Recovery,
                IsRunning: false,
            }));
        Assert.IsTrue(resumed.IsAccepted);
        Assert.IsTrue(continued.IsAccepted);
        Assert.AreEqual(3, continued.State.Home.Score);
        Assert.IsTrue(continued.State.GameClock.IsRunning);
        Assert.IsTrue(continued.State.ShotClock.IsRunning);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "Open saved game")]
    [DataRow(true, DisplayName = "Recover startup game")]
    public void RecoverOrOpenSavedGame_WhenMatchAlreadyLoadedWithoutReplacement_RejectsWithoutMutation(
        bool useRecoveryApi)
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var saved = CreateSession(timeProvider);
        Assert.IsTrue(saved.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        var savedDocument = GameDocument.Capture(
            saved.Snapshot,
            saved.History,
            timeProvider.GetUtcNow());
        using var current = CreateSession(timeProvider);
        Assert.IsTrue(current.Execute(
            new AdjustScoreCommand(TeamSide.Away, 1)).IsAccepted);
        var before = current.CaptureCheckpoint();

        var result = useRecoveryApi
            ? current.Recover(savedDocument.Events, savedDocument.Snapshot)
            : current.OpenSavedGame(savedDocument.Events, savedDocument.Snapshot);

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual(before.Snapshot.GameId, result.State.GameId);
        Assert.AreEqual(1, result.State.Away.Score);
        Assert.AreNotEqual(savedDocument.GameId, result.State.GameId);
        CollectionAssert.AreEqual(
            before.Events.Select(matchEvent => matchEvent.EventId).ToArray(),
            current.History.Select(matchEvent => matchEvent.EventId).ToArray());
    }

    [TestMethod]
    public void OpenSavedGame_ExplicitReplacement_ReplacesWithoutMixingHistories()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var saved = CreateSession(timeProvider);
        Assert.IsTrue(saved.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        Assert.IsTrue(saved.Execute(
            new AdjustFoulCommand(TeamSide.Away, 1)).IsAccepted);
        var savedDocument = GameDocument.Capture(
            saved.Snapshot,
            saved.History,
            timeProvider.GetUtcNow());
        var savedEventIds = savedDocument.Events
            .Select(matchEvent => matchEvent.EventId)
            .ToArray();
        using var current = CreateSession(timeProvider);
        Assert.IsTrue(current.Execute(
            new AdjustScoreCommand(TeamSide.Away, 1)).IsAccepted);
        var replacedEventIds = current.History
            .Select(matchEvent => matchEvent.EventId)
            .ToHashSet();

        var result = current.OpenSavedGame(
            savedDocument.Events,
            savedDocument.Snapshot,
            replaceCurrent: true);
        var continued = current.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(savedDocument.GameId, result.State.GameId);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(1, result.State.Away.Fouls);
        Assert.IsEmpty(result.Events);
        CollectionAssert.AreEqual(
            savedEventIds,
            current.History
                .Take(savedEventIds.Length)
                .Select(matchEvent => matchEvent.EventId)
                .ToArray());
        Assert.IsFalse(current.History.Any(matchEvent =>
            replacedEventIds.Contains(matchEvent.EventId)));
        Assert.IsTrue(continued.IsAccepted);
        Assert.AreEqual(3, continued.State.Home.Score);
    }

    [TestMethod]
    public void OpenSavedGame_InvalidExplicitReplacement_RejectsWithoutClearingCurrentMatch()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var saved = CreateSession(timeProvider);
        var invalidSnapshot = saved.Snapshot with
        {
            Home = saved.Snapshot.Home with { Score = 99 },
        };
        using var current = CreateSession(timeProvider);
        Assert.IsTrue(current.Execute(
            new AdjustScoreCommand(TeamSide.Away, 2)).IsAccepted);
        var before = current.CaptureCheckpoint();

        var result = current.OpenSavedGame(
            saved.History,
            invalidSnapshot,
            replaceCurrent: true);

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual(before.Snapshot.GameId, current.Snapshot.GameId);
        Assert.AreEqual(2, current.Snapshot.Away.Score);
        CollectionAssert.AreEqual(
            before.Events.Select(matchEvent => matchEvent.EventId).ToArray(),
            current.History.Select(matchEvent => matchEvent.EventId).ToArray());
    }

    [TestMethod]
    public void Recover_RunningSnapshot_RestoresLatestClockValuesPaused()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var original = CreateStartedSession(timeProvider);
        original.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        timeProvider.Advance(TimeSpan.FromSeconds(3.4));
        var persistedSnapshot = original.Snapshot;
        var document = GameDocument.Capture(
            persistedSnapshot,
            original.History,
            timeProvider.GetUtcNow());
        using var recovered = new MatchSession(new MatchEngine(), timeProvider);

        var result = recovered.Recover(document.Events, document.Snapshot);

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(persistedSnapshot.GameClock.Remaining, result.State.GameClock.Remaining);
        Assert.AreEqual(persistedSnapshot.ShotClock.Remaining, result.State.ShotClock.Remaining);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        Assert.AreEqual(MatchStatus.Paused, result.State.Status);
        Assert.HasCount(2, result.Events);
        Assert.IsTrue(result.Events
            .OfType<ClockChangedEvent>()
            .All(matchEvent => matchEvent.Metadata.Source == CommandSource.Recovery));
    }

    [TestMethod]
    public void Recover_DifferentSnapshotGameId_RejectsWithoutLoadingHistory()
    {
        var timeProvider = new FakeTimeProvider(Start);
        using var original = CreateSession(timeProvider);
        var mismatched = original.Snapshot with { GameId = Guid.NewGuid() };
        using var recovered = new MatchSession(new MatchEngine(), timeProvider);

        var result = recovered.Recover(original.History, mismatched);

        Assert.IsFalse(result.IsAccepted);
        Assert.IsFalse(recovered.Snapshot.IsCreated);
        Assert.IsEmpty(recovered.History);
    }

    private static MatchSession CreateStartedSession(
        FakeTimeProvider timeProvider,
        MatchRules? rules = null)
    {
        var session = CreateSession(timeProvider, rules);
        var started = session.Execute(new SetLinkedClocksRunningCommand(true));
        if (!started.IsAccepted)
        {
            session.Dispose();
            throw new InvalidOperationException(started.Message);
        }

        return session;
    }

    private static MatchSession CreateSession(
        FakeTimeProvider timeProvider,
        MatchRules? rules = null)
    {
        var session = new MatchSession(new MatchEngine(), timeProvider);
        var created = session.Execute(
            new CreateGameCommand(
                new MatchMetadata(),
                rules ?? MatchRules.Fiba3x3,
                "Home",
                "Away",
                "#FFFFFF",
                "#FF5252"));

        if (!created.IsAccepted)
        {
            session.Dispose();
            throw new InvalidOperationException(created.Message);
        }

        return session;
    }
}
