namespace ThreeByThree.Centar.Scoreboard.Application.Persistence;

public interface IGameStore
{
    string ActiveFilePath { get; }

    string CompletedGamesDirectory { get; }

    Task<IReadOnlyList<SavedGameInfo>> ListGamesAsync(
        CancellationToken cancellationToken = default);

    Task<GameDocument?> LoadGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default);

    Task<string> SaveActiveAsync(
        GameDocument document,
        CancellationToken cancellationToken = default);

    Task<GameDocument?> LoadActiveAsync(CancellationToken cancellationToken = default);

    Task<string> ArchiveCompletedAsync(
        GameDocument document,
        CancellationToken cancellationToken = default);

    Task<string> ExportAsync(
        GameDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task DeleteActiveAsync(CancellationToken cancellationToken = default);
}
