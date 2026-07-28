namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record ClockState
{
    public TimeSpan Remaining { get; init; }

    public bool IsRunning { get; init; }

    public bool HasExpired { get; init; }
}
