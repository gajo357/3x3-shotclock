using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Persistence;

public sealed record GameDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid GameId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset SavedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public MatchState Snapshot { get; init; } = MatchState.Empty;

    public List<MatchEvent> Events { get; init; } = [];

    public static GameDocument Capture(
        MatchState snapshot,
        IReadOnlyList<MatchEvent> events,
        DateTimeOffset savedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(events);

        var createdAtUtc = events
            .OfType<GameCreatedEvent>()
            .FirstOrDefault()?
            .Metadata
            .RecordedAtUtc ?? savedAtUtc;
        var endedAtUtc = events
            .OfType<GameEndedEvent>()
            .LastOrDefault()?
            .Metadata
            .RecordedAtUtc;

        return new GameDocument
        {
            GameId = snapshot.GameId,
            CreatedAtUtc = createdAtUtc,
            SavedAtUtc = savedAtUtc,
            EndedAtUtc = endedAtUtc,
            Snapshot = snapshot,
            Events = [.. events],
        };
    }
}
