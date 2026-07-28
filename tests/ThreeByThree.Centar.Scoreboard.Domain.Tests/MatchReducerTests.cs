using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Tests;

[TestClass]
public sealed class MatchReducerTests
{
    private static readonly DateTimeOffset RecordedAt =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Replay_UnorderedEvents_AppliesSequenceOrderAndTracksLatestSequence()
    {
        var gameId = Guid.NewGuid();
        MatchEvent[] events =
        [
            new ScoreAdjustedEvent(
                Metadata(2),
                TeamSide.Home,
                2,
                0,
                2),
            Created(1, gameId),
        ];

        var state = MatchReducer.Replay(events);

        Assert.AreEqual(gameId, state.GameId);
        Assert.AreEqual(MatchStage.Regular, state.Stage);
        Assert.AreEqual(2, state.Home.Score);
        Assert.AreEqual(0, state.Away.Score);
        Assert.AreEqual(2L, state.LastEventSequence);
    }

    [TestMethod]
    public void Replay_RevertedEvent_ExcludesTargetButRetainsLatestAuditSequence()
    {
        var scoreEventId = Guid.NewGuid();
        MatchEvent[] events =
        [
            Created(1, Guid.NewGuid()),
            new ScoreAdjustedEvent(
                Metadata(2, scoreEventId),
                TeamSide.Home,
                2,
                0,
                2),
            new EventRevertedEvent(Metadata(3), scoreEventId),
        ];

        var state = MatchReducer.Replay(events);

        Assert.HasCount(3, events);
        Assert.AreEqual(0, state.Home.Score);
        Assert.AreEqual(3L, state.LastEventSequence);
        Assert.IsTrue(state.IsCreated);
    }

    [TestMethod]
    public void Apply_GameClockExpirationWithTie_StopsBothClocksAndRequestsOvertime()
    {
        var state = RunningState(homeScore: 8, awayScore: 8);
        var expiration = new ClockExpiredEvent(
            Metadata(5),
            ClockKind.Game,
            TimeSpan.FromMilliseconds(40));

        var result = MatchReducer.Apply(state, expiration);

        Assert.AreEqual(TimeSpan.Zero, result.GameClock.Remaining);
        Assert.IsFalse(result.GameClock.IsRunning);
        Assert.IsTrue(result.GameClock.HasExpired);
        Assert.IsFalse(result.ShotClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(7), result.ShotClock.Remaining);
        Assert.IsFalse(result.ShotClock.HasExpired);
        Assert.AreEqual(PendingDecision.StartOvertime, result.PendingDecision);
        Assert.IsTrue(result.HasStarted);
        Assert.AreEqual(5L, result.LastEventSequence);
    }

    [TestMethod]
    public void Apply_GameClockExpirationWithLead_RequestsFinalScoreConfirmation()
    {
        var state = RunningState(homeScore: 11, awayScore: 9);
        var expiration = new ClockExpiredEvent(
            Metadata(6),
            ClockKind.Game,
            TimeSpan.FromMilliseconds(50));

        var result = MatchReducer.Apply(state, expiration);

        Assert.AreEqual(PendingDecision.ConfirmFinalScore, result.PendingDecision);
        Assert.AreEqual(11, result.Home.Score);
        Assert.AreEqual(9, result.Away.Score);
        Assert.IsFalse(result.GameClock.IsRunning);
        Assert.IsFalse(result.ShotClock.IsRunning);
    }

    [TestMethod]
    public void Apply_ShotClockExpiration_StopsBothClocksWithoutRequestingDecision()
    {
        var state = RunningState(homeScore: 4, awayScore: 3);
        var expiration = new ClockExpiredEvent(
            Metadata(4),
            ClockKind.Shot,
            TimeSpan.FromMilliseconds(30));

        var result = MatchReducer.Apply(state, expiration);

        Assert.IsFalse(result.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromMinutes(4), result.GameClock.Remaining);
        Assert.AreEqual(TimeSpan.Zero, result.ShotClock.Remaining);
        Assert.IsFalse(result.ShotClock.IsRunning);
        Assert.IsTrue(result.ShotClock.HasExpired);
        Assert.AreEqual(PendingDecision.None, result.PendingDecision);
    }

    [TestMethod]
    public void Apply_RegularScoringAcrossWinningBoundary_RaisesThenClearsWinningDecision()
    {
        var state = RunningState(homeScore: 20, awayScore: 15) with
        {
            GameClock = new ClockState { Remaining = TimeSpan.FromMinutes(2) },
            ShotClock = new ClockState { Remaining = TimeSpan.FromSeconds(10) },
        };
        var winningScore = new ScoreAdjustedEvent(
            Metadata(7),
            TeamSide.Home,
            1,
            20,
            21);
        var correction = new ScoreAdjustedEvent(
            Metadata(8),
            TeamSide.Home,
            -1,
            21,
            20);

        var winningState = MatchReducer.Apply(state, winningScore);
        var correctedState = MatchReducer.Apply(winningState, correction);

        Assert.AreEqual(21, winningState.Home.Score);
        Assert.AreEqual(PendingDecision.ConfirmWinningScore, winningState.PendingDecision);
        Assert.AreEqual(20, correctedState.Home.Score);
        Assert.AreEqual(PendingDecision.None, correctedState.PendingDecision);
    }

    [TestMethod]
    public void Apply_OvertimeScoreAtRegularWinningThreshold_DoesNotRaiseWinningDecision()
    {
        var state = RunningState(homeScore: 20, awayScore: 20) with
        {
            Stage = MatchStage.Overtime,
            Home = new TeamState
            {
                Name = "Home",
                Score = 20,
                OvertimePoints = 0,
            },
            GameClock = new ClockState(),
            ShotClock = new ClockState { Remaining = TimeSpan.FromSeconds(12) },
        };
        var score = new ScoreAdjustedEvent(
            Metadata(9),
            TeamSide.Home,
            1,
            20,
            21);

        var result = MatchReducer.Apply(state, score);

        Assert.AreEqual(21, result.Home.Score);
        Assert.AreEqual(1, result.Home.OvertimePoints);
        Assert.AreEqual(MatchStage.Overtime, result.Stage);
        Assert.AreEqual(PendingDecision.None, result.PendingDecision);
    }

    [TestMethod]
    public void Apply_OvertimeStarted_ResetsPointsDecisionAndShotClock()
    {
        var state = RunningState(homeScore: 13, awayScore: 13) with
        {
            PendingDecision = PendingDecision.StartOvertime,
            Home = new TeamState
            {
                Name = "Home",
                Score = 13,
                OvertimePoints = 3,
            },
            Away = new TeamState
            {
                Name = "Away",
                Score = 13,
                OvertimePoints = 4,
            },
        };
        var overtime = new OvertimeStartedEvent(
            Metadata(12),
            TimeSpan.FromSeconds(9),
            TeamSide.Away);

        var result = MatchReducer.Apply(state, overtime);

        Assert.AreEqual(MatchStage.Overtime, result.Stage);
        Assert.AreEqual(PendingDecision.None, result.PendingDecision);
        Assert.AreEqual(0, result.Home.OvertimePoints);
        Assert.AreEqual(0, result.Away.OvertimePoints);
        Assert.AreEqual(13, result.Home.Score);
        Assert.AreEqual(13, result.Away.Score);
        Assert.IsFalse(result.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(9), result.ShotClock.Remaining);
        Assert.IsFalse(result.ShotClock.IsRunning);
        Assert.IsFalse(result.ShotClock.HasExpired);
        Assert.AreEqual(TeamSide.Away, result.StartingPossession);
    }

    [TestMethod]
    public void Apply_OvertimeCorrectionBeforeFirstPoint_DoesNotCreateNegativeOvertimePoints()
    {
        var state = RunningState(homeScore: 10, awayScore: 10) with
        {
            Stage = MatchStage.Overtime,
            Home = new TeamState
            {
                Name = "Home",
                Score = 10,
                OvertimePoints = 0,
            },
        };
        var correction = new ScoreAdjustedEvent(
            Metadata(13),
            TeamSide.Home,
            -1,
            10,
            9);

        var result = MatchReducer.Apply(state, correction);

        Assert.AreEqual(9, result.Home.Score);
        Assert.AreEqual(0, result.Home.OvertimePoints);
        Assert.AreEqual(PendingDecision.None, result.PendingDecision);
    }

    [TestMethod]
    public void Replay_NullEvents_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => MatchReducer.Replay(null!));
    }

    [TestMethod]
    public void Replay_EmptyHistory_ReturnsEmptyUncreatedState()
    {
        var result = MatchReducer.Replay([]);

        Assert.IsFalse(result.IsCreated);
        Assert.AreEqual(MatchStage.Setup, result.Stage);
        Assert.AreEqual(MatchStatus.Setup, result.Status);
        Assert.AreEqual(0L, result.LastEventSequence);
    }

    [TestMethod]
    public void Apply_NullArguments_ThrowArgumentNullException()
    {
        var state = RunningState(0, 0);
        var matchEvent = new BuzzerTriggeredEvent(Metadata(1), BuzzerKind.Manual);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => MatchReducer.Apply(null!, matchEvent));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => MatchReducer.Apply(state, null!));
    }

    [TestMethod]
    public void Apply_UnsupportedEvent_ThrowsInvalidOperationExceptionWithTypeName()
    {
        var matchEvent = new UnsupportedEvent(Metadata(1));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => MatchReducer.Apply(MatchState.Empty, matchEvent));

        Assert.AreEqual(
            "Unsupported match event type: UnsupportedEvent.",
            exception.Message);
    }

    private static MatchState RunningState(int homeScore, int awayScore) =>
        new()
        {
            GameId = Guid.NewGuid(),
            Stage = MatchStage.Regular,
            Rules = MatchRules.Fiba3x3,
            Home = new TeamState { Name = "Home", Score = homeScore },
            Away = new TeamState { Name = "Away", Score = awayScore },
            GameClock = new ClockState
            {
                Remaining = TimeSpan.FromMinutes(4),
                IsRunning = true,
            },
            ShotClock = new ClockState
            {
                Remaining = TimeSpan.FromSeconds(7),
                IsRunning = true,
            },
        };

    private static GameCreatedEvent Created(long sequence, Guid gameId) =>
        new(
            Metadata(sequence),
            gameId,
            new MatchMetadata { TournamentName = "Tournament" },
            MatchRules.Fiba3x3,
            "Home",
            "Away",
            "#FFFFFF",
            "#000000");

    private static EventMetadata Metadata(long sequence, Guid? eventId = null) =>
        new(
            eventId ?? Guid.NewGuid(),
            sequence,
            RecordedAt,
            sequence * 100,
            CommandSource.System);

    private sealed record UnsupportedEvent(EventMetadata Metadata)
        : MatchEvent(Metadata);
}
