using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application;

internal sealed class RuntimeCountdownClock(TimeProvider timeProvider)
{
    private TimeSpan remainingWhenStarted;
    private long startedAtTimestamp;

    public bool IsRunning { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (!IsRunning)
            {
                return remainingWhenStarted;
            }

            var elapsed = timeProvider.GetElapsedTime(
                startedAtTimestamp,
                timeProvider.GetTimestamp());
            var remaining = remainingWhenStarted - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public void Synchronize(ClockState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        remainingWhenStarted = state.Remaining > TimeSpan.Zero
            ? state.Remaining
            : TimeSpan.Zero;
        IsRunning = state.IsRunning && remainingWhenStarted > TimeSpan.Zero;
        startedAtTimestamp = timeProvider.GetTimestamp();
    }

    public ClockState Project(ClockState state) =>
        state with
        {
            Remaining = Remaining,
            IsRunning = IsRunning,
        };
}
