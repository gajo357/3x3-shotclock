namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record MatchRules
{
    public static MatchRules Fiba3x3 { get; } = new();

    public TimeSpan RegularDuration { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan ShotClockDuration { get; init; } = TimeSpan.FromSeconds(12);

    public int WinningScore { get; init; } = 21;

    public int OvertimeWinningPoints { get; init; } = 2;

    public int PenaltyFoulThreshold { get; init; } = 7;

    public int DoublePenaltyFoulThreshold { get; init; } = 10;

    public TimeSpan GameClockTenthsThreshold { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan ShotClockTenthsThreshold { get; init; } = TimeSpan.FromSeconds(5);

    public bool LinkedClocks { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (RegularDuration <= TimeSpan.Zero)
        {
            errors.Add("Regular game duration must be greater than zero.");
        }

        if (ShotClockDuration <= TimeSpan.Zero)
        {
            errors.Add("Shot-clock duration must be greater than zero.");
        }

        if (WinningScore <= 0)
        {
            errors.Add("Winning score must be greater than zero.");
        }

        if (OvertimeWinningPoints <= 0)
        {
            errors.Add("Overtime winning points must be greater than zero.");
        }

        if (PenaltyFoulThreshold <= 0)
        {
            errors.Add("Penalty foul threshold must be greater than zero.");
        }

        if (DoublePenaltyFoulThreshold < PenaltyFoulThreshold)
        {
            errors.Add("Double-penalty threshold cannot be lower than the penalty threshold.");
        }

        if (GameClockTenthsThreshold < TimeSpan.Zero ||
            GameClockTenthsThreshold > RegularDuration)
        {
            errors.Add("Game-clock tenths threshold must be within the game duration.");
        }

        if (ShotClockTenthsThreshold < TimeSpan.Zero ||
            ShotClockTenthsThreshold > ShotClockDuration)
        {
            errors.Add("Shot-clock tenths threshold must be within the shot-clock duration.");
        }

        return errors;
    }

    public PenaltyState GetPenaltyState(int fouls)
    {
        if (fouls >= DoublePenaltyFoulThreshold)
        {
            return PenaltyState.DoublePenalty;
        }

        return fouls >= PenaltyFoulThreshold
            ? PenaltyState.Penalty
            : PenaltyState.None;
    }
}
