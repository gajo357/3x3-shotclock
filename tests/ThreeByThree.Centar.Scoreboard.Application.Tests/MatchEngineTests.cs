using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Tests;

[TestClass]
public sealed class MatchEngineTests
{
    private static readonly DateTimeOffset RecordedAt =
        new(2026, 7, 27, 18, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void Execute_CreateGameWithValidInputs_NormalizesAndInitializesMatch()
    {
        var scenario = new Scenario
        {
            RecordedAtUtc = RecordedAt,
            SessionElapsed = TimeSpan.FromMilliseconds(-250),
        };
        var metadata = new MatchMetadata
        {
            TournamentName = "3x3 Centar",
            ScheduledGameId = "QF-1",
            CourtName = "Main",
            Category = "Men",
        };
        var rules = MatchRules.Fiba3x3 with
        {
            RegularDuration = TimeSpan.FromMinutes(9),
            ShotClockDuration = TimeSpan.FromSeconds(10),
        };

        var result = scenario.Execute(
            CreateCommand(
                rules,
                homeName: "  Falcons  ",
                awayName: "  Eagles ",
                homeColor: "#aa00ff",
                awayColor: "#00bb11",
                metadata: metadata,
                source: CommandSource.Keyboard));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var created = Assert.IsInstanceOfType<GameCreatedEvent>(result.Events[0]);
        Assert.AreNotEqual(Guid.Empty, created.GameId);
        Assert.AreEqual(1L, created.Sequence);
        Assert.AreEqual(RecordedAt, created.Metadata.RecordedAtUtc);
        Assert.AreEqual(0L, created.Metadata.SessionElapsedMilliseconds);
        Assert.AreEqual(CommandSource.Keyboard, created.Metadata.Source);
        Assert.AreSame(metadata, created.MatchMetadata);
        Assert.AreSame(rules, created.Rules);
        Assert.AreEqual("Falcons", result.State.Home.Name);
        Assert.AreEqual("Eagles", result.State.Away.Name);
        Assert.AreEqual("#AA00FF", result.State.Home.ColorHex);
        Assert.AreEqual("#00BB11", result.State.Away.ColorHex);
        Assert.AreEqual(TimeSpan.FromMinutes(9), result.State.GameClock.Remaining);
        Assert.AreEqual(TimeSpan.FromSeconds(10), result.State.ShotClock.Remaining);
        Assert.AreEqual(MatchStage.Regular, result.State.Stage);
        Assert.AreEqual(MatchStatus.Ready, result.State.Status);
    }

    [TestMethod]
    [DataRow(
        TeamSide.Home,
        CoinTossChoice.OpeningPossession,
        TeamSide.Home,
        DisplayName = "Home chooses opening possession")]
    [DataRow(
        TeamSide.Home,
        CoinTossChoice.OvertimePossession,
        TeamSide.Away,
        DisplayName = "Home reserves overtime possession")]
    [DataRow(
        TeamSide.Away,
        CoinTossChoice.OpeningPossession,
        TeamSide.Away,
        DisplayName = "Away chooses opening possession")]
    [DataRow(
        TeamSide.Away,
        CoinTossChoice.OvertimePossession,
        TeamSide.Home,
        DisplayName = "Away reserves overtime possession")]
    public void Execute_CreateGame_CapturesCoinTossAndOpeningPossession(
        TeamSide winner,
        CoinTossChoice choice,
        TeamSide expectedOpeningPossession)
    {
        var scenario = new Scenario();
        var metadata = new MatchMetadata
        {
            TournamentName = "3x3 Centar",
            CoinTossWinner = winner,
            CoinTossSelection = choice,
        };

        var result = scenario.Execute(CreateCommand(metadata: metadata));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var created = Assert.IsInstanceOfType<GameCreatedEvent>(result.Events[0]);
        Assert.AreEqual(winner, created.MatchMetadata.CoinTossWinner);
        Assert.AreEqual(choice, created.MatchMetadata.CoinTossSelection);
        Assert.AreEqual(winner, result.State.Metadata.CoinTossWinner);
        Assert.AreEqual(choice, result.State.Metadata.CoinTossSelection);
        Assert.AreEqual(expectedOpeningPossession, result.State.StartingPossession);
    }

    [TestMethod]
    public void Execute_CreateGameWithInvalidRules_RejectsWithoutEvents()
    {
        var scenario = new Scenario();
        var rules = MatchRules.Fiba3x3 with { WinningScore = 0 };

        var result = scenario.Execute(CreateCommand(rules));

        Assert.IsFalse(result.IsAccepted);
        Assert.Contains("Winning score must be greater than zero.", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.AreSame(MatchState.Empty, result.State);
        Assert.IsEmpty(scenario.History);
    }

    [TestMethod]
    public void Execute_CreateGameWithInvalidName_RejectsWithoutEvents()
    {
        var scenario = new Scenario();

        var blankResult = scenario.Execute(CreateCommand(homeName: "   "));
        var longResult = scenario.Execute(CreateCommand(homeName: new string('X', 33)));

        Assert.IsFalse(blankResult.IsAccepted);
        Assert.Contains("between 1 and 32 characters", blankResult.Message);
        Assert.IsFalse(longResult.IsAccepted);
        Assert.Contains("between 1 and 32 characters", longResult.Message);
        Assert.IsEmpty(scenario.History);
    }

    [TestMethod]
    public void Execute_CreateGameWithInvalidColor_RejectsWithoutEvents()
    {
        var scenario = new Scenario();

        var missingHash = scenario.Execute(CreateCommand(homeColor: "112233"));
        var invalidDigit = scenario.Execute(CreateCommand(homeColor: "#GG2233"));

        Assert.IsFalse(missingHash.IsAccepted);
        Assert.Contains("six-digit hexadecimal notation", missingHash.Message);
        Assert.IsFalse(invalidDigit.IsAccepted);
        Assert.Contains("six-digit hexadecimal notation", invalidDigit.Message);
        Assert.IsEmpty(scenario.History);
    }

    [TestMethod]
    public void Execute_CreateGameWhileActive_RejectsReplacement()
    {
        var scenario = Scenario.Created();
        var activeState = scenario.State;
        var activeGameId = activeState.GameId;

        var result = scenario.Execute(CreateCommand(homeName: "Replacement"));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("End the active game before creating another.", result.Message);
        Assert.AreSame(activeState, result.State);
        Assert.AreEqual(activeGameId, scenario.State.GameId);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    public void Execute_GameplayCommandBeforeCreation_RejectsWithoutEvents()
    {
        var scenario = new Scenario();

        var result = scenario.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("Create a game first.", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.IsFalse(result.State.IsCreated);
        Assert.IsEmpty(scenario.History);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(-1)]
    [DataRow(-2)]
    public void Execute_AdjustScoreWithAllowedDelta_UpdatesScoreAndAuditValues(int delta)
    {
        var scenario = Scenario.Created();
        if (delta < 0)
        {
            scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        }

        var before = scenario.State.Home.Score;

        var result = scenario.Execute(
            new AdjustScoreCommand(TeamSide.Home, delta, CommandSource.Keyboard));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var scoreEvent = Assert.IsInstanceOfType<ScoreAdjustedEvent>(result.Events[0]);
        Assert.AreEqual(TeamSide.Home, scoreEvent.Team);
        Assert.AreEqual(delta, scoreEvent.Delta);
        Assert.AreEqual(before, scoreEvent.Before);
        Assert.AreEqual(before + delta, scoreEvent.After);
        Assert.AreEqual(before + delta, result.State.Home.Score);
        Assert.AreEqual(0, result.State.Away.Score);
        Assert.AreEqual(CommandSource.Keyboard, scoreEvent.Metadata.Source);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(-3)]
    public void Execute_AdjustScoreWithUnsupportedDelta_RejectsWithoutChangingScore(int delta)
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new AdjustScoreCommand(TeamSide.Home, delta));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("Score adjustments must be +1, +2, -1, or -2.", result.Message);
        Assert.AreEqual(0, scenario.State.Home.Score);
        Assert.HasCount(1, scenario.History);
        Assert.IsEmpty(result.Events);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(-2)]
    public void Execute_AdjustScoreBelowZero_RejectsNegativeScore(int delta)
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new AdjustScoreCommand(TeamSide.Away, delta));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("A score cannot be negative.", result.Message);
        Assert.AreEqual(0, scenario.State.Away.Score);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    public void Execute_AdjustFoulUpAndDown_UpdatesCountAndAuditValues()
    {
        var scenario = Scenario.Created();

        var increase = scenario.Execute(
            new AdjustFoulCommand(TeamSide.Away, 1, CommandSource.Keyboard));
        var decrease = scenario.Execute(new AdjustFoulCommand(TeamSide.Away, -1));

        Assert.IsTrue(increase.IsAccepted);
        var increaseEvent = Assert.IsInstanceOfType<FoulAdjustedEvent>(increase.Events[0]);
        Assert.AreEqual(0, increaseEvent.Before);
        Assert.AreEqual(1, increaseEvent.After);
        Assert.AreEqual(CommandSource.Keyboard, increaseEvent.Metadata.Source);
        Assert.IsTrue(decrease.IsAccepted);
        var decreaseEvent = Assert.IsInstanceOfType<FoulAdjustedEvent>(decrease.Events[0]);
        Assert.AreEqual(1, decreaseEvent.Before);
        Assert.AreEqual(0, decreaseEvent.After);
        Assert.AreEqual(0, scenario.State.Away.Fouls);
        Assert.AreEqual(PenaltyState.None, scenario.State.AwayPenalty);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(-2)]
    public void Execute_AdjustFoulWithUnsupportedDelta_RejectsWithoutChangingCount(int delta)
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new AdjustFoulCommand(TeamSide.Home, delta));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("Fouls can only be adjusted by one.", result.Message);
        Assert.AreEqual(0, scenario.State.Home.Fouls);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    public void Execute_AdjustFoulBelowZero_RejectsNegativeCount()
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new AdjustFoulCommand(TeamSide.Home, -1));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("A foul count cannot be negative.", result.Message);
        Assert.AreEqual(0, scenario.State.Home.Fouls);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    public void Execute_ControllerMouseTargetCommands_ApplyIncreaseAndDecreaseSemantics()
    {
        var scenario = Scenario.Created();

        var pointIncrease = scenario.Execute(
            new AdjustScoreCommand(
                TeamSide.Home,
                1,
                CommandSource.ControllerButton));
        var pointDecrease = scenario.Execute(
            new AdjustScoreCommand(
                TeamSide.Home,
                -1,
                CommandSource.ControllerButton));
        var foulIncrease = scenario.Execute(
            new AdjustFoulCommand(
                TeamSide.Away,
                1,
                CommandSource.ControllerButton));
        var foulDecrease = scenario.Execute(
            new AdjustFoulCommand(
                TeamSide.Away,
                -1,
                CommandSource.ControllerButton));
        var shotDecrease = scenario.Execute(
            new AdjustClockCommand(
                ClockKind.Shot,
                TimeSpan.FromSeconds(-1),
                CommandSource.ControllerButton));
        var shotIncrease = scenario.Execute(
            new AdjustClockCommand(
                ClockKind.Shot,
                TimeSpan.FromSeconds(1),
                CommandSource.ControllerButton));

        Assert.IsTrue(pointIncrease.IsAccepted);
        Assert.IsTrue(pointDecrease.IsAccepted);
        Assert.IsTrue(foulIncrease.IsAccepted);
        Assert.IsTrue(foulDecrease.IsAccepted);
        Assert.IsTrue(shotDecrease.IsAccepted);
        Assert.IsTrue(shotIncrease.IsAccepted);
        var pointUp = Assert.IsInstanceOfType<ScoreAdjustedEvent>(pointIncrease.Events[0]);
        var pointDown = Assert.IsInstanceOfType<ScoreAdjustedEvent>(pointDecrease.Events[0]);
        var foulUp = Assert.IsInstanceOfType<FoulAdjustedEvent>(foulIncrease.Events[0]);
        var foulDown = Assert.IsInstanceOfType<FoulAdjustedEvent>(foulDecrease.Events[0]);
        var shotDown = Assert.IsInstanceOfType<ClockChangedEvent>(shotDecrease.Events[0]);
        var shotUp = Assert.IsInstanceOfType<ClockChangedEvent>(shotIncrease.Events[0]);
        Assert.AreEqual(1, pointUp.Delta);
        Assert.AreEqual(-1, pointDown.Delta);
        Assert.AreEqual(1, foulUp.Delta);
        Assert.AreEqual(-1, foulDown.Delta);
        Assert.AreEqual(TimeSpan.FromSeconds(11), shotDown.After);
        Assert.AreEqual(TimeSpan.FromSeconds(12), shotUp.After);
        Assert.AreEqual(0, scenario.State.Home.Score);
        Assert.AreEqual(0, scenario.State.Away.Fouls);
        Assert.AreEqual(TimeSpan.FromSeconds(12), scenario.State.ShotClock.Remaining);
        Assert.IsTrue(
            new MatchEvent[] { pointUp, pointDown, foulUp, foulDown, shotDown, shotUp }
                .All(matchEvent =>
                    matchEvent.Metadata.Source == CommandSource.ControllerButton));
    }

    [TestMethod]
    public void Execute_RegularScoreCrossesThenDropsBelowWinningThreshold_ReversesAlert()
    {
        var rules = MatchRules.Fiba3x3 with { WinningScore = 2 };
        var scenario = Scenario.Created(rules);

        var winning = scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        var correction = scenario.Execute(new AdjustScoreCommand(TeamSide.Home, -1));

        Assert.IsTrue(winning.IsAccepted);
        Assert.AreEqual(2, winning.State.Home.Score);
        Assert.AreEqual(PendingDecision.ConfirmWinningScore, winning.State.PendingDecision);
        Assert.IsTrue(correction.IsAccepted);
        Assert.AreEqual(1, correction.State.Home.Score);
        Assert.AreEqual(PendingDecision.None, correction.State.PendingDecision);
    }

    [TestMethod]
    public void Execute_StartOvertimeAfterTiedExpiration_PreservesScoreAndResetsOvertimeState()
    {
        var rules = MatchRules.Fiba3x3 with
        {
            ShotClockDuration = TimeSpan.FromSeconds(9),
        };
        var scenario = Scenario.Created(rules);
        scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        scenario.Execute(new AdjustScoreCommand(TeamSide.Away, 2));
        scenario.Execute(new SetLinkedClocksRunningCommand(true));
        var expiration = scenario.Execute(new ExpireClockCommand(ClockKind.Game));

        var result = scenario.Execute(new StartOvertimeCommand());

        Assert.AreEqual(PendingDecision.StartOvertime, expiration.State.PendingDecision);
        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        Assert.IsInstanceOfType<OvertimeStartedEvent>(result.Events[0]);
        Assert.AreEqual(MatchStage.Overtime, result.State.Stage);
        Assert.AreEqual(MatchStatus.Overtime, result.State.Status);
        Assert.AreEqual(PendingDecision.None, result.State.PendingDecision);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(2, result.State.Away.Score);
        Assert.AreEqual(0, result.State.Home.OvertimePoints);
        Assert.AreEqual(0, result.State.Away.OvertimePoints);
        Assert.AreEqual(TimeSpan.FromSeconds(9), result.State.ShotClock.Remaining);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
    }

    [TestMethod]
    [DataRow(
        TeamSide.Home,
        CoinTossChoice.OpeningPossession,
        TeamSide.Home,
        TeamSide.Away,
        DisplayName = "Home opens, away receives overtime")]
    [DataRow(
        TeamSide.Home,
        CoinTossChoice.OvertimePossession,
        TeamSide.Away,
        TeamSide.Home,
        DisplayName = "Home reserves overtime")]
    [DataRow(
        TeamSide.Away,
        CoinTossChoice.OpeningPossession,
        TeamSide.Away,
        TeamSide.Home,
        DisplayName = "Away opens, home receives overtime")]
    [DataRow(
        TeamSide.Away,
        CoinTossChoice.OvertimePossession,
        TeamSide.Home,
        TeamSide.Away,
        DisplayName = "Away reserves overtime")]
    public void Execute_StartOvertime_ResolvesPossessionFromCoinToss(
        TeamSide winner,
        CoinTossChoice choice,
        TeamSide expectedOpeningPossession,
        TeamSide expectedOvertimePossession)
    {
        var metadata = new MatchMetadata
        {
            CoinTossWinner = winner,
            CoinTossSelection = choice,
        };
        var scenario = Scenario.Created(metadata: metadata);
        var openingPossession = scenario.State.StartingPossession;
        scenario.Execute(new SetLinkedClocksRunningCommand(true));
        scenario.Execute(new ExpireClockCommand(ClockKind.Game));

        var result = scenario.Execute(new StartOvertimeCommand());

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(expectedOpeningPossession, openingPossession);
        Assert.HasCount(1, result.Events);
        var overtime = Assert.IsInstanceOfType<OvertimeStartedEvent>(result.Events[0]);
        Assert.AreEqual(expectedOvertimePossession, overtime.StartingPossession);
        Assert.AreEqual(expectedOvertimePossession, result.State.StartingPossession);
        Assert.AreEqual(MatchStage.Overtime, result.State.Stage);
    }

    [TestMethod]
    public void Execute_StartOvertimeWithoutTiedExpiration_RejectsTransition()
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new StartOvertimeCommand());

        Assert.IsFalse(result.IsAccepted);
        Assert.Contains("only start after regulation expires with a tied score", result.Message);
        Assert.AreEqual(MatchStage.Regular, scenario.State.Stage);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    [DataRow(
        TeamSide.Home,
        TeamSide.Away,
        2,
        1,
        DisplayName = "Home wins 2-1 in overtime")]
    [DataRow(
        TeamSide.Away,
        TeamSide.Home,
        1,
        2,
        DisplayName = "Away wins 2-1 in overtime")]
    public void Execute_OvertimeSingleTwoPointScore_EndsImmediatelyWithoutWinByTwo(
        TeamSide winner,
        TeamSide opponent,
        int expectedHomeScore,
        int expectedAwayScore)
    {
        var scenario = Scenario.InOvertime();
        var opponentPoint = scenario.Execute(
            new AdjustScoreCommand(opponent, 1));

        var winningScore = scenario.Execute(
            new AdjustScoreCommand(winner, 2));
        var correction = scenario.Execute(
            new AdjustScoreCommand(winner, -1));

        Assert.IsTrue(opponentPoint.IsAccepted);
        Assert.AreEqual(1, opponentPoint.State.GetTeam(opponent).OvertimePoints);
        Assert.IsTrue(winningScore.IsAccepted);
        Assert.HasCount(2, winningScore.Events);
        var score = Assert.IsInstanceOfType<ScoreAdjustedEvent>(winningScore.Events[0]);
        var ended = Assert.IsInstanceOfType<GameEndedEvent>(winningScore.Events[1]);
        Assert.AreEqual(winner, score.Team);
        Assert.AreEqual(2, score.Delta);
        Assert.AreEqual(2, winningScore.State.GetTeam(winner).OvertimePoints);
        Assert.AreEqual(1, winningScore.State.GetTeam(opponent).OvertimePoints);
        Assert.AreEqual(expectedHomeScore, ended.HomeScore);
        Assert.AreEqual(expectedAwayScore, ended.AwayScore);
        Assert.AreEqual(MatchStage.Final, winningScore.State.Stage);
        Assert.AreEqual(PendingDecision.None, winningScore.State.PendingDecision);
        Assert.IsFalse(correction.IsAccepted);
        Assert.AreEqual(2, correction.State.GetTeam(winner).Score);
    }

    [TestMethod]
    public void Execute_OvertimeTwoOnePointScores_EndsOnlyOnSecondPoint()
    {
        var scenario = Scenario.InOvertime();

        var firstPoint = scenario.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));
        var secondPoint = scenario.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsTrue(firstPoint.IsAccepted);
        Assert.HasCount(1, firstPoint.Events);
        Assert.IsInstanceOfType<ScoreAdjustedEvent>(firstPoint.Events[0]);
        Assert.AreEqual(1, firstPoint.State.Home.OvertimePoints);
        Assert.AreEqual(MatchStage.Overtime, firstPoint.State.Stage);
        Assert.AreEqual(PendingDecision.None, firstPoint.State.PendingDecision);
        Assert.IsTrue(secondPoint.IsAccepted);
        Assert.HasCount(2, secondPoint.Events);
        Assert.IsInstanceOfType<ScoreAdjustedEvent>(secondPoint.Events[0]);
        Assert.IsInstanceOfType<GameEndedEvent>(secondPoint.Events[1]);
        Assert.AreEqual(2, secondPoint.State.Home.OvertimePoints);
        Assert.AreEqual(MatchStage.Final, secondPoint.State.Stage);
        Assert.AreEqual(PendingDecision.None, secondPoint.State.PendingDecision);
    }

    [TestMethod]
    public void Execute_OvertimeAtRegularWinningScore_DoesNotRaiseRegularWinningAlert()
    {
        var scenario = Scenario.Created();
        for (var index = 0; index < 10; index++)
        {
            scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
            scenario.Execute(new AdjustScoreCommand(TeamSide.Away, 2));
        }

        scenario.Execute(new SetLinkedClocksRunningCommand(true));
        scenario.Execute(new ExpireClockCommand(ClockKind.Game));
        scenario.Execute(new StartOvertimeCommand());

        var result = scenario.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        Assert.IsInstanceOfType<ScoreAdjustedEvent>(result.Events[0]);
        Assert.IsEmpty(result.Events.OfType<GameEndedEvent>().ToArray());
        Assert.AreEqual(21, result.State.Home.Score);
        Assert.AreEqual(1, result.State.Home.OvertimePoints);
        Assert.AreEqual(MatchStage.Overtime, result.State.Stage);
        Assert.AreEqual(PendingDecision.None, result.State.PendingDecision);
    }

    [TestMethod]
    public void Execute_SetLinkedClocksRunning_StartsAndPausesBothClocks()
    {
        var scenario = Scenario.Created();

        var started = scenario.Execute(
            new SetLinkedClocksRunningCommand(true, CommandSource.Keyboard));
        var paused = scenario.Execute(new SetLinkedClocksRunningCommand(false));
        var duplicatePause = scenario.Execute(new SetLinkedClocksRunningCommand(false));

        Assert.IsTrue(started.IsAccepted);
        Assert.HasCount(2, started.Events);
        var gameStarted = Assert.IsInstanceOfType<ClockChangedEvent>(started.Events[0]);
        var shotStarted = Assert.IsInstanceOfType<ClockChangedEvent>(started.Events[1]);
        Assert.AreEqual(ClockKind.Game, gameStarted.Clock);
        Assert.AreEqual(ClockKind.Shot, shotStarted.Clock);
        Assert.AreEqual(ClockOperation.Started, gameStarted.Operation);
        Assert.AreEqual(ClockOperation.Started, shotStarted.Operation);
        Assert.AreEqual(CommandSource.Keyboard, gameStarted.Metadata.Source);
        Assert.IsTrue(started.State.GameClock.IsRunning);
        Assert.IsTrue(started.State.ShotClock.IsRunning);
        Assert.IsTrue(started.State.HasStarted);
        Assert.IsTrue(paused.IsAccepted);
        Assert.HasCount(2, paused.Events);
        Assert.IsFalse(paused.State.GameClock.IsRunning);
        Assert.IsFalse(paused.State.ShotClock.IsRunning);
        Assert.AreEqual(MatchStatus.Paused, paused.State.Status);
        Assert.IsFalse(duplicatePause.IsAccepted);
        Assert.AreEqual("Both clocks are paused.", duplicatePause.Message);
    }

    [TestMethod]
    public void Execute_SetLinkedClocksRunningInOvertime_ControlsOnlyShotClock()
    {
        var scenario = Scenario.InOvertime();

        var started = scenario.Execute(
            new SetLinkedClocksRunningCommand(true, CommandSource.Keyboard));
        var duplicateStart = scenario.Execute(new SetLinkedClocksRunningCommand(true));
        var paused = scenario.Execute(new SetLinkedClocksRunningCommand(false));
        var duplicatePause = scenario.Execute(new SetLinkedClocksRunningCommand(false));

        Assert.IsTrue(started.IsAccepted);
        Assert.HasCount(1, started.Events);
        var shotStarted = Assert.IsInstanceOfType<ClockChangedEvent>(started.Events[0]);
        Assert.AreEqual(ClockKind.Shot, shotStarted.Clock);
        Assert.AreEqual(ClockOperation.Started, shotStarted.Operation);
        Assert.AreEqual(CommandSource.Keyboard, shotStarted.Metadata.Source);
        Assert.AreEqual(TimeSpan.Zero, started.State.GameClock.Remaining);
        Assert.IsFalse(started.State.GameClock.IsRunning);
        Assert.IsTrue(started.State.ShotClock.IsRunning);
        Assert.IsFalse(duplicateStart.IsAccepted);
        Assert.AreEqual("The shot clock is already running.", duplicateStart.Message);
        Assert.IsTrue(paused.IsAccepted);
        Assert.HasCount(1, paused.Events);
        var shotPaused = Assert.IsInstanceOfType<ClockChangedEvent>(paused.Events[0]);
        Assert.AreEqual(ClockKind.Shot, shotPaused.Clock);
        Assert.AreEqual(ClockOperation.Paused, shotPaused.Operation);
        Assert.IsFalse(paused.State.GameClock.IsRunning);
        Assert.IsFalse(paused.State.ShotClock.IsRunning);
        Assert.IsFalse(duplicatePause.IsAccepted);
        Assert.AreEqual("The shot clock is paused.", duplicatePause.Message);
    }

    [TestMethod]
    public void Execute_StartOvertimePlayWithShotClockAtZero_RejectsStart()
    {
        var scenario = Scenario.InOvertime();
        scenario.Execute(new SetClockCommand(ClockKind.Shot, TimeSpan.Zero, Stop: true));

        var result = scenario.Execute(new SetLinkedClocksRunningCommand(true));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("Reset the shot clock before starting overtime play.", result.Message);
        Assert.AreEqual(TimeSpan.Zero, result.State.GameClock.Remaining);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, result.State.ShotClock.Remaining);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
    }

    [TestMethod]
    public void Execute_StartClockAtZero_RejectsUntilClockIsReset()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetClockCommand(ClockKind.Shot, TimeSpan.Zero, Stop: true));

        var result = scenario.Execute(new SetClockRunningCommand(ClockKind.Shot, true));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("Reset any clock at zero before starting linked clocks.", result.Message);
        Assert.IsFalse(scenario.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, scenario.State.ShotClock.Remaining);
        Assert.IsFalse(scenario.State.ShotClock.IsRunning);
    }

    [TestMethod]
    public void Execute_StartLinkedClocksWithAClockAtZero_RejectsBothStarts()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetClockCommand(ClockKind.Game, TimeSpan.Zero, Stop: true));

        var result = scenario.Execute(new SetLinkedClocksRunningCommand(true));

        Assert.IsFalse(result.IsAccepted);
        Assert.Contains("Reset any clock at zero", result.Message);
        Assert.IsFalse(scenario.State.GameClock.IsRunning);
        Assert.IsFalse(scenario.State.ShotClock.IsRunning);
    }

    [TestMethod]
    [DataRow(ClockKind.Game)]
    [DataRow(ClockKind.Shot)]
    public void Execute_SetClockRunningForEitherClock_StartsAndPausesBothClocks(
        ClockKind requestedClock)
    {
        var scenario = Scenario.Created();

        var started = scenario.Execute(
            new SetClockRunningCommand(requestedClock, true, CommandSource.Keyboard));
        var paused = scenario.Execute(
            new SetClockRunningCommand(requestedClock, false, CommandSource.Keyboard));

        Assert.IsTrue(started.IsAccepted);
        Assert.HasCount(2, started.Events);
        Assert.IsTrue(started.State.GameClock.IsRunning);
        Assert.IsTrue(started.State.ShotClock.IsRunning);
        Assert.IsTrue(started.Events
            .OfType<ClockChangedEvent>()
            .All(matchEvent =>
                matchEvent.Operation == ClockOperation.Started &&
                matchEvent.Metadata.Source == CommandSource.Keyboard));
        Assert.IsTrue(paused.IsAccepted);
        Assert.HasCount(2, paused.Events);
        Assert.IsFalse(paused.State.GameClock.IsRunning);
        Assert.IsFalse(paused.State.ShotClock.IsRunning);
        Assert.IsTrue(paused.Events
            .OfType<ClockChangedEvent>()
            .All(matchEvent => matchEvent.Operation == ClockOperation.Paused));
    }

    [TestMethod]
    public void Execute_AdjustClockBeyondBounds_ClampsAndStopsAtZero()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetClockRunningCommand(ClockKind.Shot, true));

        var zero = scenario.Execute(
            new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(-20)));
        var maximum = scenario.Execute(
            new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(20)));
        var gameMaximum = scenario.Execute(
            new AdjustClockCommand(ClockKind.Game, TimeSpan.FromHours(2)));

        Assert.IsTrue(zero.IsAccepted);
        Assert.HasCount(2, zero.Events);
        var zeroEvent = zero.Events
            .OfType<ClockChangedEvent>()
            .Single(matchEvent => matchEvent.Clock == ClockKind.Shot);
        Assert.AreEqual(TimeSpan.Zero, zeroEvent.After);
        Assert.IsFalse(zeroEvent.IsRunning);
        Assert.AreEqual(ClockOperation.Adjusted, zeroEvent.Operation);
        Assert.IsFalse(zero.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, zero.State.ShotClock.Remaining);
        Assert.IsFalse(zero.State.ShotClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(12), maximum.State.ShotClock.Remaining);
        Assert.IsFalse(maximum.State.ShotClock.IsRunning);
        Assert.AreEqual(
            TimeSpan.FromMinutes(100) - TimeSpan.FromMilliseconds(100),
            gameMaximum.State.GameClock.Remaining);
    }

    [TestMethod]
    public void Execute_AdjustClockAtLimitOrByZero_RejectsNoOp()
    {
        var scenario = Scenario.Created();

        var zeroDelta = scenario.Execute(
            new AdjustClockCommand(ClockKind.Shot, TimeSpan.Zero));
        var atMaximum = scenario.Execute(
            new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(1)));

        Assert.IsFalse(zeroDelta.IsAccepted);
        Assert.AreEqual("Clock adjustment cannot be zero.", zeroDelta.Message);
        Assert.IsFalse(atMaximum.IsAccepted);
        Assert.AreEqual("The clock is already at its allowed limit.", atMaximum.Message);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    [DataRow(ClockKind.Game)]
    [DataRow(ClockKind.Shot)]
    public void Execute_AdjustEitherRunningClockToZero_PausesBothClocks(
        ClockKind adjustedClock)
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetLinkedClocksRunningCommand(true));

        var result = scenario.Execute(
            new AdjustClockCommand(adjustedClock, -TimeSpan.FromHours(2)));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(2, result.Events);
        Assert.AreEqual(TimeSpan.Zero, result.State.GetClock(adjustedClock).Remaining);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        var companionPause = result.Events
            .OfType<ClockChangedEvent>()
            .Single(matchEvent => matchEvent.Clock != adjustedClock);
        Assert.AreEqual(ClockOperation.Paused, companionPause.Operation);
        Assert.IsFalse(companionPause.IsRunning);
    }

    [TestMethod]
    [DataRow(-60)]
    [DataRow(-1)]
    [DataRow(1)]
    [DataRow(60)]
    public void Execute_ControllerGameClockDeltas_AdjustSecondsAndMinutes(int deltaSeconds)
    {
        var scenario = Scenario.Created();
        var before = scenario.State.GameClock.Remaining;

        var result = scenario.Execute(
            new AdjustClockCommand(
                ClockKind.Game,
                TimeSpan.FromSeconds(deltaSeconds),
                CommandSource.ControllerButton));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var adjusted = Assert.IsInstanceOfType<ClockChangedEvent>(result.Events[0]);
        Assert.AreEqual(ClockKind.Game, adjusted.Clock);
        Assert.AreEqual(ClockOperation.Adjusted, adjusted.Operation);
        Assert.AreEqual(TimeSpan.FromSeconds(deltaSeconds), adjusted.After - adjusted.Before);
        Assert.AreEqual(before + TimeSpan.FromSeconds(deltaSeconds), result.State.GameClock.Remaining);
        Assert.AreEqual(CommandSource.ControllerButton, adjusted.Metadata.Source);
        Assert.AreEqual(TimeSpan.FromSeconds(12), result.State.ShotClock.Remaining);
    }

    [TestMethod]
    [DataRow(ClockKind.Game, -100)]
    [DataRow(ClockKind.Game, 6_000_000)]
    [DataRow(ClockKind.Shot, -100)]
    [DataRow(ClockKind.Shot, 12_100)]
    public void Execute_SetClockOutsideAllowedRange_Rejects(
        ClockKind clock,
        int milliseconds)
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(
            new SetClockCommand(
                clock,
                TimeSpan.FromMilliseconds(milliseconds),
                Stop: false));

        Assert.IsFalse(result.IsAccepted);
        Assert.Contains("Clock time must be between zero", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    public void Execute_SetClockWhileRunning_PreservesOrStopsRunningAsRequested()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetClockRunningCommand(ClockKind.Shot, true));

        var preserve = scenario.Execute(
            new SetClockCommand(
                ClockKind.Shot,
                TimeSpan.FromSeconds(8),
                Stop: false));
        var stop = scenario.Execute(
            new SetClockCommand(
                ClockKind.Shot,
                TimeSpan.FromSeconds(6),
                Stop: true));
        scenario.Execute(new SetClockRunningCommand(ClockKind.Shot, true));
        var zero = scenario.Execute(
            new SetClockCommand(ClockKind.Shot, TimeSpan.Zero, Stop: false));

        Assert.IsTrue(preserve.State.ShotClock.IsRunning);
        Assert.IsTrue(preserve.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(8), preserve.State.ShotClock.Remaining);
        Assert.IsFalse(stop.State.ShotClock.IsRunning);
        Assert.IsFalse(stop.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromSeconds(6), stop.State.ShotClock.Remaining);
        Assert.IsFalse(zero.State.ShotClock.IsRunning);
        Assert.IsFalse(zero.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, zero.State.ShotClock.Remaining);
        Assert.IsFalse(zero.State.ShotClock.HasExpired);
    }

    [TestMethod]
    public void Execute_ResetClock_PreservesRunningUnlessStopIsRequested()
    {
        var scenario = Scenario.Created();
        scenario.Execute(
            new SetClockCommand(
                ClockKind.Game,
                TimeSpan.FromMinutes(3),
                Stop: true));
        scenario.Execute(new SetClockRunningCommand(ClockKind.Game, true));

        var runningReset = scenario.Execute(
            new ResetClockCommand(ClockKind.Game, Stop: false));
        scenario.Execute(
            new SetClockCommand(
                ClockKind.Shot,
                TimeSpan.FromSeconds(3),
                Stop: true));
        scenario.Execute(new SetClockRunningCommand(ClockKind.Shot, true));
        var stoppedReset = scenario.Execute(
            new ResetClockCommand(ClockKind.Shot, Stop: true));

        Assert.AreEqual(TimeSpan.FromMinutes(10), runningReset.State.GameClock.Remaining);
        Assert.IsTrue(runningReset.State.GameClock.IsRunning);
        var gameEvent = Assert.IsInstanceOfType<ClockChangedEvent>(runningReset.Events[0]);
        Assert.AreEqual(ClockOperation.Reset, gameEvent.Operation);
        Assert.AreEqual(TimeSpan.FromSeconds(12), stoppedReset.State.ShotClock.Remaining);
        Assert.IsFalse(stoppedReset.State.ShotClock.IsRunning);
        Assert.IsFalse(stoppedReset.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromMinutes(10), stoppedReset.State.GameClock.Remaining);
    }

    [TestMethod]
    public void Execute_ExpireShotClockTwice_EmitsExpirationAndBuzzerExactlyOnce()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetClockRunningCommand(ClockKind.Shot, true));

        var first = scenario.Execute(new ExpireClockCommand(ClockKind.Shot));
        var historyCountAfterFirst = scenario.History.Count;
        var second = scenario.Execute(new ExpireClockCommand(ClockKind.Shot));

        Assert.IsTrue(first.IsAccepted);
        Assert.HasCount(3, first.Events);
        var expiration = Assert.IsInstanceOfType<ClockExpiredEvent>(first.Events[0]);
        var gamePaused = Assert.IsInstanceOfType<ClockChangedEvent>(first.Events[1]);
        var buzzer = Assert.IsInstanceOfType<BuzzerTriggeredEvent>(first.Events[2]);
        Assert.AreEqual(ClockKind.Shot, expiration.Clock);
        Assert.AreEqual(ClockKind.Game, gamePaused.Clock);
        Assert.AreEqual(ClockOperation.Paused, gamePaused.Operation);
        Assert.AreEqual(BuzzerKind.ShotClock, buzzer.Buzzer);
        Assert.AreEqual(TimeSpan.Zero, first.State.ShotClock.Remaining);
        Assert.IsTrue(first.State.ShotClock.HasExpired);
        Assert.IsFalse(first.State.ShotClock.IsRunning);
        Assert.IsFalse(first.State.GameClock.IsRunning);
        Assert.AreEqual(TimeSpan.FromMinutes(10), first.State.GameClock.Remaining);
        Assert.IsFalse(second.IsAccepted);
        Assert.AreEqual("The clock is not eligible to expire.", second.Message);
        Assert.IsEmpty(second.Events);
        Assert.HasCount(historyCountAfterFirst, scenario.History);
        Assert.HasCount(1, scenario.History.OfType<ClockExpiredEvent>().ToArray());
        Assert.HasCount(
            1,
            scenario.History
                .OfType<BuzzerTriggeredEvent>()
                .Where(matchEvent => matchEvent.Buzzer == BuzzerKind.ShotClock)
                .ToArray());
    }

    [TestMethod]
    public void Execute_ExpirePausedClock_RejectsWithoutExpirationOrBuzzer()
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new ExpireClockCommand(ClockKind.Shot));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("The clock is not eligible to expire.", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.IsFalse(scenario.State.ShotClock.HasExpired);
        Assert.AreEqual(TimeSpan.FromSeconds(12), scenario.State.ShotClock.Remaining);
        Assert.HasCount(1, scenario.History);
        Assert.IsEmpty(scenario.History.OfType<BuzzerTriggeredEvent>().ToArray());
    }

    [TestMethod]
    [DataRow(0, PendingDecision.StartOvertime)]
    [DataRow(1, PendingDecision.ConfirmFinalScore)]
    public void Execute_ExpireGameClock_StopsBothClocksAndRequestsExpectedDecision(
        int homeLead,
        PendingDecision expectedDecision)
    {
        var scenario = Scenario.Created();
        if (homeLead > 0)
        {
            scenario.Execute(new AdjustScoreCommand(TeamSide.Home, homeLead));
        }

        scenario.Execute(new SetLinkedClocksRunningCommand(true));

        var result = scenario.Execute(new ExpireClockCommand(ClockKind.Game));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(3, result.Events);
        var expiration = Assert.IsInstanceOfType<ClockExpiredEvent>(result.Events[0]);
        var shotPaused = Assert.IsInstanceOfType<ClockChangedEvent>(result.Events[1]);
        var buzzer = Assert.IsInstanceOfType<BuzzerTriggeredEvent>(result.Events[2]);
        Assert.AreEqual(ClockKind.Game, expiration.Clock);
        Assert.AreEqual(ClockKind.Shot, shotPaused.Clock);
        Assert.AreEqual(ClockOperation.Paused, shotPaused.Operation);
        Assert.AreEqual(BuzzerKind.GameClock, buzzer.Buzzer);
        Assert.AreEqual(expectedDecision, result.State.PendingDecision);
        Assert.AreEqual(TimeSpan.Zero, result.State.GameClock.Remaining);
        Assert.IsTrue(result.State.GameClock.HasExpired);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.HasExpired);
    }

    [TestMethod]
    public void Execute_ChangeTeamNameAndColor_NormalizesAndRecordsBeforeAfterValues()
    {
        var scenario = Scenario.Created();

        var nameResult = scenario.Execute(
            new ChangeTeamNameCommand(TeamSide.Home, "  Falcons  "));
        var colorResult = scenario.Execute(
            new ChangeTeamColorCommand(TeamSide.Home, "#aa00ff"));

        Assert.IsTrue(nameResult.IsAccepted);
        var nameEvent = Assert.IsInstanceOfType<TeamNameChangedEvent>(nameResult.Events[0]);
        Assert.AreEqual("Home", nameEvent.Before);
        Assert.AreEqual("Falcons", nameEvent.After);
        Assert.IsTrue(colorResult.IsAccepted);
        var colorEvent = Assert.IsInstanceOfType<TeamColorChangedEvent>(colorResult.Events[0]);
        Assert.AreEqual("#FFFFFF", colorEvent.Before);
        Assert.AreEqual("#AA00FF", colorEvent.After);
        Assert.AreEqual("Falcons", scenario.State.Home.Name);
        Assert.AreEqual("#AA00FF", scenario.State.Home.ColorHex);
    }

    [TestMethod]
    public void Execute_UnchangedTeamEdits_RejectNormalizedEquivalentValues()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new ChangeTeamNameCommand(TeamSide.Home, "Falcons"));
        scenario.Execute(new ChangeTeamColorCommand(TeamSide.Home, "#aa00ff"));
        var historyCount = scenario.History.Count;

        var nameResult = scenario.Execute(
            new ChangeTeamNameCommand(TeamSide.Home, " Falcons "));
        var colorResult = scenario.Execute(
            new ChangeTeamColorCommand(TeamSide.Home, "#AA00FF"));

        Assert.IsFalse(nameResult.IsAccepted);
        Assert.AreEqual("The team name is unchanged.", nameResult.Message);
        Assert.IsFalse(colorResult.IsAccepted);
        Assert.AreEqual("The team color is unchanged.", colorResult.Message);
        Assert.HasCount(historyCount, scenario.History);
    }

    [TestMethod]
    public void Execute_InvalidTeamEdits_RejectWithoutEvents()
    {
        var scenario = Scenario.Created();
        var historyCount = scenario.History.Count;

        var nameResult = scenario.Execute(
            new ChangeTeamNameCommand(TeamSide.Away, "   "));
        var colorResult = scenario.Execute(
            new ChangeTeamColorCommand(TeamSide.Away, "#12345Z"));

        Assert.IsFalse(nameResult.IsAccepted);
        Assert.Contains("between 1 and 32 characters", nameResult.Message);
        Assert.IsEmpty(nameResult.Events);
        Assert.IsFalse(colorResult.IsAccepted);
        Assert.Contains("six-digit hexadecimal notation", colorResult.Message);
        Assert.IsEmpty(colorResult.Events);
        Assert.HasCount(historyCount, scenario.History);
    }

    [TestMethod]
    public void Execute_SwapTeams_MovesCompleteTeamState()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new ChangeTeamNameCommand(TeamSide.Home, "Falcons"));
        scenario.Execute(new ChangeTeamColorCommand(TeamSide.Home, "#112233"));
        scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        scenario.Execute(new AdjustFoulCommand(TeamSide.Home, 1));
        scenario.Execute(new ChangeTeamNameCommand(TeamSide.Away, "Eagles"));
        scenario.Execute(new AdjustScoreCommand(TeamSide.Away, 1));
        var homeBefore = scenario.State.Home;
        var awayBefore = scenario.State.Away;

        var result = scenario.Execute(new SwapTeamsCommand(CommandSource.Keyboard));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var swapped = Assert.IsInstanceOfType<TeamsSwappedEvent>(result.Events[0]);
        Assert.AreEqual(CommandSource.Keyboard, swapped.Metadata.Source);
        Assert.AreEqual(awayBefore, result.State.Home);
        Assert.AreEqual(homeBefore, result.State.Away);
        Assert.AreEqual("Eagles", result.State.Home.Name);
        Assert.AreEqual(1, result.State.Home.Score);
        Assert.AreEqual("Falcons", result.State.Away.Name);
        Assert.AreEqual("#112233", result.State.Away.ColorHex);
        Assert.AreEqual(2, result.State.Away.Score);
        Assert.AreEqual(1, result.State.Away.Fouls);
    }

    [TestMethod]
    public void Execute_SwapTeamsOutsideActiveGame_RejectsLifecycle()
    {
        var uncreated = new Scenario();
        var finalized = Scenario.Created();
        finalized.Execute(new EndGameCommand());
        var finalizedHistoryCount = finalized.History.Count;

        var uncreatedResult = uncreated.Execute(new SwapTeamsCommand());
        var finalizedResult = finalized.Execute(new SwapTeamsCommand());

        Assert.IsFalse(uncreatedResult.IsAccepted);
        Assert.AreEqual("Create a game first.", uncreatedResult.Message);
        Assert.IsEmpty(uncreatedResult.Events);
        Assert.IsFalse(finalizedResult.IsAccepted);
        Assert.AreEqual("The game is final.", finalizedResult.Message);
        Assert.IsEmpty(finalizedResult.Events);
        Assert.HasCount(finalizedHistoryCount, finalized.History);
    }

    [TestMethod]
    public void Execute_EndGame_FinalizesScoreStopsClocksAndRejectsGameplay()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        scenario.Execute(new AdjustScoreCommand(TeamSide.Away, 1));
        scenario.Execute(new SetLinkedClocksRunningCommand(true));

        var result = scenario.Execute(new EndGameCommand());
        var rejectedScore = scenario.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var ended = Assert.IsInstanceOfType<GameEndedEvent>(result.Events[0]);
        Assert.AreEqual(2, ended.HomeScore);
        Assert.AreEqual(1, ended.AwayScore);
        Assert.AreEqual(MatchStage.Final, result.State.Stage);
        Assert.AreEqual(MatchStatus.Final, result.State.Status);
        Assert.AreEqual(PendingDecision.None, result.State.PendingDecision);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        Assert.IsFalse(rejectedScore.IsAccepted);
        Assert.AreEqual("The game is final.", rejectedScore.Message);
        Assert.AreEqual(2, scenario.State.Home.Score);
    }

    [TestMethod]
    public void Execute_CreateGameAfterFinalization_StartsFreshGameWithContinuingAuditSequence()
    {
        var scenario = Scenario.Created();
        var originalGameId = scenario.State.GameId;
        scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        scenario.Execute(new EndGameCommand());

        var result = scenario.Execute(
            CreateCommand(homeName: "New Home", awayName: "New Away"));

        Assert.IsTrue(result.IsAccepted);
        var created = Assert.IsInstanceOfType<GameCreatedEvent>(result.Events[0]);
        Assert.AreEqual(4L, created.Sequence);
        Assert.AreNotEqual(originalGameId, result.State.GameId);
        Assert.AreEqual("New Home", result.State.Home.Name);
        Assert.AreEqual("New Away", result.State.Away.Name);
        Assert.AreEqual(0, result.State.Home.Score);
        Assert.AreEqual(0, result.State.Away.Score);
        Assert.AreEqual(MatchStage.Regular, result.State.Stage);
        Assert.HasCount(4, scenario.History);
    }

    [TestMethod]
    public void Execute_ClearPendingDecision_WhenPresentRecordsAndClearsPreviousDecision()
    {
        var rules = MatchRules.Fiba3x3 with { WinningScore = 1 };
        var scenario = Scenario.Created(rules);
        scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 1));

        var result = scenario.Execute(new ClearPendingDecisionCommand());
        var duplicate = scenario.Execute(new ClearPendingDecisionCommand());

        Assert.IsTrue(result.IsAccepted);
        var cleared = Assert.IsInstanceOfType<PendingDecisionClearedEvent>(result.Events[0]);
        Assert.AreEqual(PendingDecision.ConfirmWinningScore, cleared.PreviousDecision);
        Assert.AreEqual(PendingDecision.None, result.State.PendingDecision);
        Assert.IsFalse(duplicate.IsAccepted);
        Assert.AreEqual("There is no pending decision to clear.", duplicate.Message);
    }

    [TestMethod]
    public void Execute_UndoLastActions_AppendsReversionsAndRestoresReplayedState()
    {
        var scenario = Scenario.Created();
        var score = scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        var foul = scenario.Execute(new AdjustFoulCommand(TeamSide.Home, 1));
        var scoreEvent = Assert.IsInstanceOfType<ScoreAdjustedEvent>(score.Events[0]);
        var foulEvent = Assert.IsInstanceOfType<FoulAdjustedEvent>(foul.Events[0]);

        var undoFoul = scenario.Execute(
            new UndoLastActionCommand(CommandSource.Keyboard));
        var undoScore = scenario.Execute(new UndoLastActionCommand());

        Assert.IsTrue(undoFoul.IsAccepted);
        var foulReversion = Assert.IsInstanceOfType<EventRevertedEvent>(undoFoul.Events[0]);
        Assert.AreEqual(foulEvent.EventId, foulReversion.TargetEventId);
        Assert.AreEqual(CommandSource.Keyboard, foulReversion.Metadata.Source);
        Assert.AreEqual(2, undoFoul.State.Home.Score);
        Assert.AreEqual(0, undoFoul.State.Home.Fouls);
        Assert.IsTrue(undoScore.IsAccepted);
        var scoreReversion = Assert.IsInstanceOfType<EventRevertedEvent>(undoScore.Events[0]);
        Assert.AreEqual(scoreEvent.EventId, scoreReversion.TargetEventId);
        Assert.AreEqual(0, undoScore.State.Home.Score);
        Assert.AreEqual(0, undoScore.State.Home.Fouls);
        Assert.HasCount(5, scenario.History);
        Assert.Contains(scoreEvent, scenario.History);
        Assert.Contains(foulEvent, scenario.History);
        Assert.HasCount(2, scenario.History.OfType<EventRevertedEvent>().ToArray());
        Assert.AreEqual(5L, scenario.State.LastEventSequence);
    }

    [TestMethod]
    public void Execute_UndoAfterManualBuzzer_SkipsNonUndoableBuzzer()
    {
        var scenario = Scenario.Created();
        var score = scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 1));
        var scoreEvent = Assert.IsInstanceOfType<ScoreAdjustedEvent>(score.Events[0]);
        var buzzer = scenario.Execute(new TriggerBuzzerCommand());

        var result = scenario.Execute(new UndoLastActionCommand());

        Assert.IsTrue(buzzer.IsAccepted);
        var buzzerEvent = Assert.IsInstanceOfType<BuzzerTriggeredEvent>(buzzer.Events[0]);
        Assert.AreEqual(BuzzerKind.Manual, buzzerEvent.Buzzer);
        Assert.IsTrue(result.IsAccepted);
        var reversion = Assert.IsInstanceOfType<EventRevertedEvent>(result.Events[0]);
        Assert.AreEqual(scoreEvent.EventId, reversion.TargetEventId);
        Assert.AreEqual(0, result.State.Home.Score);
        Assert.Contains(buzzerEvent, scenario.History);
    }

    [TestMethod]
    public void Execute_TriggerBuzzerWithRequestedKind_EmitsRequestedCue()
    {
        var scenario = Scenario.Created();
        var before = scenario.State;

        var result = scenario.Execute(
            new TriggerBuzzerCommand(
                CommandSource.ClockScheduler,
                BuzzerKind.ShotClockWarning));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(1, result.Events);
        var buzzer = Assert.IsInstanceOfType<BuzzerTriggeredEvent>(result.Events[0]);
        Assert.AreEqual(BuzzerKind.ShotClockWarning, buzzer.Buzzer);
        Assert.AreEqual(CommandSource.ClockScheduler, buzzer.Metadata.Source);
        Assert.AreEqual(before.Home.Score, result.State.Home.Score);
        Assert.AreEqual(before.Away.Score, result.State.Away.Score);
        Assert.AreEqual(before.GameClock, result.State.GameClock);
        Assert.AreEqual(before.ShotClock, result.State.ShotClock);
    }

    [TestMethod]
    public void Execute_UndoWithNoUndoableAction_RejectsWithoutAppendingEvent()
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new UndoLastActionCommand());

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("There is no action available to undo.", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.HasCount(1, scenario.History);
    }

    [TestMethod]
    public void Execute_UndoAfterFinalization_RejectsWithoutReopeningGame()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        scenario.Execute(new EndGameCommand());
        var historyCount = scenario.History.Count;

        var result = scenario.Execute(new UndoLastActionCommand());

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("The game is final.", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.AreEqual(MatchStage.Final, scenario.State.Stage);
        Assert.AreEqual(2, scenario.State.Home.Score);
        Assert.HasCount(historyCount, scenario.History);
    }

    [TestMethod]
    public void Execute_UndoClockStart_RejectsToPreserveLinkedClockState()
    {
        var scenario = Scenario.Created();
        scenario.Execute(new SetLinkedClocksRunningCommand(true));
        var historyCount = scenario.History.Count;

        var result = scenario.Execute(new UndoLastActionCommand());

        Assert.IsFalse(result.IsAccepted);
        Assert.Contains("cannot be undone", result.Message);
        Assert.IsTrue(scenario.State.GameClock.IsRunning);
        Assert.IsTrue(scenario.State.ShotClock.IsRunning);
        Assert.HasCount(historyCount, scenario.History);
    }

    [TestMethod]
    public void Execute_MultiEventCommand_AssignsIncreasingMetadataFromCommandContext()
    {
        var scenario = Scenario.Created();
        scenario.RecordedAtUtc = RecordedAt;
        scenario.SessionElapsed = TimeSpan.FromMilliseconds(1234);

        var result = scenario.Execute(
            new SetLinkedClocksRunningCommand(true, CommandSource.Keyboard));

        Assert.IsTrue(result.IsAccepted);
        Assert.HasCount(2, result.Events);
        Assert.AreEqual(2L, result.Events[0].Sequence);
        Assert.AreEqual(3L, result.Events[1].Sequence);
        Assert.AreEqual(RecordedAt, result.Events[0].Metadata.RecordedAtUtc);
        Assert.AreEqual(RecordedAt, result.Events[1].Metadata.RecordedAtUtc);
        Assert.AreEqual(1234L, result.Events[0].Metadata.SessionElapsedMilliseconds);
        Assert.AreEqual(1234L, result.Events[1].Metadata.SessionElapsedMilliseconds);
        Assert.AreEqual(CommandSource.Keyboard, result.Events[0].Metadata.Source);
        Assert.AreEqual(CommandSource.Keyboard, result.Events[1].Metadata.Source);
        Assert.AreNotEqual(result.Events[0].EventId, result.Events[1].EventId);
    }

    [TestMethod]
    public void Execute_NullArguments_ThrowArgumentNullException()
    {
        var engine = new MatchEngine();
        var command = new TriggerBuzzerCommand();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => engine.Execute(
                null!,
                [],
                command,
                RecordedAt,
                TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => engine.Execute(
                MatchState.Empty,
                null!,
                command,
                RecordedAt,
                TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => engine.Execute(
                MatchState.Empty,
                [],
                null!,
                RecordedAt,
                TimeSpan.Zero));
    }

    [TestMethod]
    public void Execute_UnsupportedCommand_RejectsWithTypeName()
    {
        var scenario = Scenario.Created();

        var result = scenario.Execute(new UnsupportedCommand());

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual("Unsupported command type: UnsupportedCommand.", result.Message);
        Assert.IsEmpty(result.Events);
        Assert.HasCount(1, scenario.History);
    }

    private static CreateGameCommand CreateCommand(
        MatchRules? rules = null,
        string homeName = "Home",
        string awayName = "Away",
        string homeColor = "#FFFFFF",
        string awayColor = "#FF5252",
        MatchMetadata? metadata = null,
        CommandSource source = CommandSource.ControllerButton) =>
        new(
            metadata ?? new MatchMetadata(),
            rules ?? MatchRules.Fiba3x3,
            homeName,
            awayName,
            homeColor,
            awayColor,
            source);

    private sealed class Scenario
    {
        private readonly MatchEngine engine = new();

        public MatchState State { get; private set; } = MatchState.Empty;

        public List<MatchEvent> History { get; } = [];

        public DateTimeOffset RecordedAtUtc { get; set; } = RecordedAt;

        public TimeSpan SessionElapsed { get; set; } = TimeSpan.Zero;

        public static Scenario Created(
            MatchRules? rules = null,
            MatchMetadata? metadata = null)
        {
            var scenario = new Scenario();
            var result = scenario.Execute(
                CreateCommand(rules, metadata: metadata));
            if (!result.IsAccepted)
            {
                throw new InvalidOperationException(
                    $"The test fixture game could not be created: {result.Message}");
            }

            return scenario;
        }

        public static Scenario InOvertime()
        {
            var scenario = Created();
            scenario.Execute(new SetLinkedClocksRunningCommand(true));
            scenario.Execute(new ExpireClockCommand(ClockKind.Game));
            var overtime = scenario.Execute(new StartOvertimeCommand());
            if (!overtime.IsAccepted)
            {
                throw new InvalidOperationException(
                    $"The test fixture could not enter overtime: {overtime.Message}");
            }

            return scenario;
        }

        public CommandResult Execute(MatchCommand command)
        {
            var result = engine.Execute(
                State,
                History,
                command,
                RecordedAtUtc,
                SessionElapsed);

            if (result.IsAccepted)
            {
                History.AddRange(result.Events);
                State = result.State;
            }

            return result;
        }
    }

    private sealed record UnsupportedCommand()
        : MatchCommand(CommandSource.System);
}
