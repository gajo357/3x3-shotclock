using System.Text.Json;
using System.Threading.Channels;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

public sealed class MatchPersistenceService : IMatchPersistenceService, IDisposable
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);
    private readonly MatchSession session;
    private readonly IGameStore store;
    private readonly TimeProvider timeProvider;
    private readonly object enqueueGate = new();
    private readonly Channel<PersistenceRequest> writes =
        Channel.CreateUnbounded<PersistenceRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
    private readonly CancellationTokenSource periodicCancellation = new();
    private Task writerTask = Task.CompletedTask;
    private Task periodicTask = Task.CompletedTask;
    private bool isStarted;
    private bool isStopped;
    private bool isDisposed;

    public MatchPersistenceService(
        MatchSession session,
        IGameStore store,
        TimeProvider timeProvider)
    {
        this.session = session;
        this.store = store;
        this.timeProvider = timeProvider;
        Status = new PersistenceStatus("Not started", null, null, HasError: false);
    }

    public event EventHandler? StatusChanged;

    public PersistenceStatus Status { get; private set; }

    public string CompletedGamesDirectory => store.CompletedGamesDirectory;

    public Task<IReadOnlyList<SavedGameInfo>> ListGamesAsync(
        CancellationToken cancellationToken = default) =>
        store.ListGamesAsync(cancellationToken);

    public async Task<CommandResult> OpenGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        var current = session.Snapshot;
        if (current.GameId == gameId)
        {
            return CommandResult.Accept(
                current,
                [],
                current.Stage == MatchStage.Final
                    ? "Finished game is already open."
                    : "Game is already open.");
        }

        var game = (await store.ListGamesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.GameId == gameId);
        if (game is null)
        {
            return CommandResult.Reject(current, "The selected saved game was not found.");
        }

        QueueCurrentCheckpoint();
        var document = await store
            .LoadGameAsync(gameId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return CommandResult.Reject(current, "The selected saved game was not found.");
        }

        var result = session.OpenSavedGame(
            document.Events,
            document.Snapshot,
            replaceCurrent: true);
        if (result.IsAccepted)
        {
            QueueCurrentCheckpoint();
            UpdateStatus(
                result.State.Stage == MatchStage.Final
                    ? "Opened · final"
                    : "Opened · paused",
                game.FilePath,
                document.SavedAtUtc,
                hasError: false);
        }

        return result;
    }

    public async Task<GameDocument?> LoadRecoveryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await store.LoadActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            UpdateStatus(
                $"Recovery file error: {exception.Message}",
                store.ActiveFilePath,
                null,
                hasError: true);
            return null;
        }
    }

    public CommandResult Recover(GameDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = session.Recover(document.Events, document.Snapshot);
        UpdateStatus(
            result.IsAccepted ? "Recovered · paused" : result.Message,
            store.ActiveFilePath,
            document.SavedAtUtc,
            hasError: !result.IsAccepted);
        return result;
    }

    public async Task DiscardRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await store.DeleteActiveAsync(cancellationToken).ConfigureAwait(false);
        UpdateStatus("Recovery discarded", null, null, hasError: false);
    }

    public async Task<string> ExportCurrentAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = session.CaptureCheckpoint();
        if (!checkpoint.Snapshot.IsCreated)
        {
            throw new InvalidOperationException("There is no game to export.");
        }

        var document = GameDocument.Capture(
            checkpoint.Snapshot,
            checkpoint.Events,
            timeProvider.GetUtcNow());
        var path = await store
            .ExportAsync(document, destinationPath, cancellationToken)
            .ConfigureAwait(false);
        UpdateStatus("Exported", path, document.SavedAtUtc, hasError: false);
        return path;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (isStarted)
        {
            return;
        }

        isStarted = true;
        session.EventsCommitted += OnEventsCommitted;
        writerTask = Task.Run(ProcessWritesAsync);
        periodicTask = CapturePeriodicSnapshotsAsync(periodicCancellation.Token);
        QueueCurrentCheckpoint();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!isStarted || isStopped)
        {
            return;
        }

        isStopped = true;
        await periodicCancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await periodicTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (periodicCancellation.IsCancellationRequested)
        {
        }

        session.EventsCommitted -= OnEventsCommitted;
        QueueCurrentCheckpoint();
        lock (enqueueGate)
        {
            writes.Writer.TryComplete();
        }

        await writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        session.EventsCommitted -= OnEventsCommitted;
        periodicCancellation.Cancel();
        periodicCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnEventsCommitted(object? sender, MatchEventsCommittedEventArgs e)
    {
        if (!e.Snapshot.IsCreated || e.Events.Count == 0)
        {
            return;
        }

        QueueRequest(
            new PersistenceRequest(
                e.Snapshot,
                [.. e.Events],
                timeProvider.GetUtcNow(),
                PersistenceRequestKind.Action));
    }

    private void QueueCurrentCheckpoint()
    {
        try
        {
            var checkpoint = session.CaptureCheckpoint();
            if (!checkpoint.Snapshot.IsCreated)
            {
                return;
            }

            QueueRequest(
                new PersistenceRequest(
                    checkpoint.Snapshot,
                    [.. checkpoint.Events],
                    timeProvider.GetUtcNow(),
                    PersistenceRequestKind.Checkpoint));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void QueuePeriodicSnapshot(MatchState snapshot)
    {
        if (!snapshot.IsCreated)
        {
            return;
        }

        QueueRequest(
            new PersistenceRequest(
                snapshot,
                [],
                timeProvider.GetUtcNow(),
                PersistenceRequestKind.Snapshot));
    }

    private void QueueRequest(PersistenceRequest request)
    {
        lock (enqueueGate)
        {
            _ = writes.Writer.TryWrite(request);
        }
    }

    private async Task CapturePeriodicSnapshotsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SnapshotInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var snapshot = session.Snapshot;
            if (snapshot.Stage != MatchStage.Final &&
                (snapshot.GameClock.IsRunning || snapshot.ShotClock.IsRunning))
            {
                QueuePeriodicSnapshot(snapshot);
            }
        }
    }

    private async Task ProcessWritesAsync()
    {
        var journals = new Dictionary<Guid, GameJournalState>();
        var completedGameIds = new HashSet<Guid>();

        await foreach (var request in writes.Reader.ReadAllAsync())
        {
            if (!request.Snapshot.IsCreated)
            {
                continue;
            }

            if (completedGameIds.Contains(request.Snapshot.GameId))
            {
                continue;
            }

            if (!journals.TryGetValue(request.Snapshot.GameId, out var journal))
            {
                journal = new GameJournalState();
                journals.Add(request.Snapshot.GameId, journal);
            }

            MergeEvents(journal.Events, request.Events);
            if (request.Snapshot.LastEventSequence > journal.LatestSnapshot.LastEventSequence ||
                (request.Snapshot.LastEventSequence == journal.LatestSnapshot.LastEventSequence &&
                 request.Kind != PersistenceRequestKind.Action))
            {
                journal.LatestSnapshot = request.Snapshot;
                journal.LatestCapturedAtUtc = request.CapturedAtUtc;
            }

            journal.IsDirty = true;
            await PersistPendingGamesAsync(journals, completedGameIds).ConfigureAwait(false);
        }
    }

    private async Task PersistPendingGamesAsync(
        Dictionary<Guid, GameJournalState> journals,
        HashSet<Guid> completedGameIds)
    {
        foreach (var (gameId, journal) in journals.ToArray())
        {
            if (!journal.IsDirty ||
                !TryCreateDocument(
                    journal.Events,
                    journal.LatestSnapshot,
                    journal.LatestCapturedAtUtc,
                    out var document))
            {
                continue;
            }

            try
            {
                var activePath = await store.SaveActiveAsync(document).ConfigureAwait(false);
                journal.IsDirty = false;
                if (document.Snapshot.Stage == MatchStage.Final)
                {
                    completedGameIds.Add(document.GameId);
                    journals.Remove(gameId);
                    UpdateStatus(
                        "Saved · final",
                        activePath,
                        document.SavedAtUtc,
                        hasError: false);
                }
                else
                {
                    UpdateStatus(
                        "Saved",
                        activePath,
                        document.SavedAtUtc,
                        hasError: false);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException or
                InvalidOperationException)
            {
                UpdateStatus(
                    $"Save failed: {exception.Message}",
                    store.ActiveFilePath,
                    null,
                    hasError: true);
            }
        }
    }

    private static void MergeEvents(
        SortedDictionary<long, MatchEvent> journal,
        IReadOnlyList<MatchEvent> events)
    {
        foreach (var matchEvent in events)
        {
            journal[matchEvent.Sequence] = matchEvent;
        }
    }

    private static bool TryCreateDocument(
        SortedDictionary<long, MatchEvent> journal,
        MatchState snapshot,
        DateTimeOffset capturedAtUtc,
        out GameDocument document)
    {
        document = new GameDocument();
        if (!snapshot.IsCreated ||
            snapshot.LastEventSequence <= 0 ||
            capturedAtUtc == DateTimeOffset.MinValue ||
            journal.Count < snapshot.LastEventSequence)
        {
            return false;
        }

        var events = journal.Values
            .TakeWhile(matchEvent => matchEvent.Sequence <= snapshot.LastEventSequence)
            .ToArray();
        if (events.Length != snapshot.LastEventSequence ||
            events[0] is not GameCreatedEvent ||
            events[^1].Sequence != snapshot.LastEventSequence)
        {
            return false;
        }

        for (var index = 0; index < events.Length; index++)
        {
            if (events[index].Sequence != index + 1)
            {
                return false;
            }
        }

        document = GameDocument.Capture(snapshot, events, capturedAtUtc);
        return true;
    }

    private void UpdateStatus(
        string message,
        string? path,
        DateTimeOffset? lastSavedAtUtc,
        bool hasError)
    {
        Status = new PersistenceStatus(message, path, lastSavedAtUtc, hasError);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record PersistenceRequest(
        MatchState Snapshot,
        IReadOnlyList<MatchEvent> Events,
        DateTimeOffset CapturedAtUtc,
        PersistenceRequestKind Kind);

    private sealed class GameJournalState
    {
        public SortedDictionary<long, MatchEvent> Events { get; } = [];

        public MatchState LatestSnapshot { get; set; } = MatchState.Empty;

        public DateTimeOffset LatestCapturedAtUtc { get; set; } = DateTimeOffset.MinValue;

        public bool IsDirty { get; set; }
    }

    private enum PersistenceRequestKind
    {
        Action,
        Snapshot,
        Checkpoint,
    }
}
