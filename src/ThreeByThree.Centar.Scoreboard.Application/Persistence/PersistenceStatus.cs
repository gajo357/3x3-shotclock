namespace ThreeByThree.Centar.Scoreboard.Application.Persistence;

public sealed record PersistenceStatus(
    string Message,
    string? CurrentFile,
    DateTimeOffset? LastSavedAtUtc,
    bool HasError);
