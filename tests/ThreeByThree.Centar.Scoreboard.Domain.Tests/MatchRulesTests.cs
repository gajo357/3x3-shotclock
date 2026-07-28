using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Tests;

[TestClass]
public sealed class MatchRulesTests
{
    [TestMethod]
    public void Validate_DefaultRules_ReturnsNoErrors()
    {
        var errors = MatchRules.Fiba3x3.Validate();

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_AllInvalidValues_ReturnsEveryValidationError()
    {
        var rules = new MatchRules
        {
            RegularDuration = TimeSpan.Zero,
            ShotClockDuration = TimeSpan.Zero,
            WinningScore = 0,
            OvertimeWinningPoints = 0,
            PenaltyFoulThreshold = 0,
            DoublePenaltyFoulThreshold = -1,
            GameClockTenthsThreshold = TimeSpan.FromMilliseconds(-1),
            ShotClockTenthsThreshold = TimeSpan.FromMilliseconds(1),
        };

        var errors = rules.Validate();

        Assert.HasCount(8, errors);
        Assert.Contains("Regular game duration must be greater than zero.", errors);
        Assert.Contains("Shot-clock duration must be greater than zero.", errors);
        Assert.Contains("Winning score must be greater than zero.", errors);
        Assert.Contains("Overtime winning points must be greater than zero.", errors);
        Assert.Contains("Penalty foul threshold must be greater than zero.", errors);
        Assert.Contains(
            "Double-penalty threshold cannot be lower than the penalty threshold.",
            errors);
        Assert.Contains(
            "Game-clock tenths threshold must be within the game duration.",
            errors);
        Assert.Contains(
            "Shot-clock tenths threshold must be within the shot-clock duration.",
            errors);
    }

    [TestMethod]
    public void Validate_ThresholdsAtAllowedBounds_ReturnsNoErrors()
    {
        var rules = new MatchRules
        {
            RegularDuration = TimeSpan.FromMinutes(1),
            ShotClockDuration = TimeSpan.FromSeconds(5),
            PenaltyFoulThreshold = 7,
            DoublePenaltyFoulThreshold = 7,
            GameClockTenthsThreshold = TimeSpan.FromMinutes(1),
            ShotClockTenthsThreshold = TimeSpan.FromSeconds(5),
        };

        var errors = rules.Validate();

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_TenthsThresholdOutsideEitherBound_ReturnsClockSpecificError()
    {
        var gameAboveDuration = MatchRules.Fiba3x3 with
        {
            GameClockTenthsThreshold = TimeSpan.FromMinutes(11),
        };
        var shotBelowZero = MatchRules.Fiba3x3 with
        {
            ShotClockTenthsThreshold = TimeSpan.FromMilliseconds(-1),
        };

        var gameErrors = gameAboveDuration.Validate();
        var shotErrors = shotBelowZero.Validate();

        Assert.HasCount(1, gameErrors);
        Assert.Contains(
            "Game-clock tenths threshold must be within the game duration.",
            gameErrors);
        Assert.HasCount(1, shotErrors);
        Assert.Contains(
            "Shot-clock tenths threshold must be within the shot-clock duration.",
            shotErrors);
    }

    [TestMethod]
    [DataRow(0, PenaltyState.None)]
    [DataRow(6, PenaltyState.None)]
    [DataRow(7, PenaltyState.Penalty)]
    [DataRow(9, PenaltyState.Penalty)]
    [DataRow(10, PenaltyState.DoublePenalty)]
    [DataRow(25, PenaltyState.DoublePenalty)]
    public void GetPenaltyState_BoundaryFoulCount_ReturnsExpectedState(
        int fouls,
        PenaltyState expected)
    {
        var result = MatchRules.Fiba3x3.GetPenaltyState(fouls);

        Assert.AreEqual(expected, result);
    }
}
