using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application;

public sealed class MatchSnapshotChangedEventArgs(MatchState snapshot) : EventArgs
{
    public MatchState Snapshot { get; } = snapshot;
}

public sealed class MatchEventsCommittedEventArgs(
    MatchState snapshot,
    IReadOnlyList<MatchEvent> events) : EventArgs
{
    public MatchState Snapshot { get; } = snapshot;

    public IReadOnlyList<MatchEvent> Events { get; } = events;
}

public sealed record MatchSessionCheckpoint(
    MatchState Snapshot,
    IReadOnlyList<MatchEvent> Events);

public sealed class MatchSessionErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
