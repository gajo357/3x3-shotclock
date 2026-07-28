namespace ThreeByThree.Centar.Scoreboard.Application.Persistence;

public interface IMatchPersistenceService
{
    event EventHandler? StatusChanged;

    PersistenceStatus Status { get; }

    string CompletedGamesDirectory { get; }

    Task<IReadOnlyList<SavedGameInfo>> ListGamesAsync(
        CancellationToken cancellationToken = default);

    Task<CommandResult> OpenGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default);

    Task<GameDocument?> LoadRecoveryAsync(CancellationToken cancellationToken = default);

    CommandResult Recover(GameDocument document);

    Task DiscardRecoveryAsync(CancellationToken cancellationToken = default);

    Task<string> ExportCurrentAsync(
        string destinationPath,
        CancellationToken cancellationToken = default);

    void Start();

    Task StopAsync(CancellationToken cancellationToken = default);
}
