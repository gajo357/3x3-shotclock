using System.Reflection;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class MatchPersistenceServiceTests
{
    private readonly TestContext testContext;
    private readonly string testRoot;

    public MatchPersistenceServiceTests(TestContext testContext)
    {
        this.testContext = testContext;
        testRoot = Path.Combine(
            Path.GetTempPath(),
            "ThreeByThree.Centar.Scoreboard.Tests",
            Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task EventsCommitted_RapidConsecutiveActions_PersistsEveryEventInCommitOrderToActiveJson()
    {
        var jsonStore = CreateJsonStore();
        var store = new ObservingGameStore(jsonStore);
        using var session = CreateEmptySession();
        using var persistence = new MatchPersistenceService(
            session,
            store,
            TimeProvider.System);
        persistence.Start();

        var created = ExecuteCreate(session);
        for (var index = 0; index < 12; index++)
        {
            Assert.IsTrue(session.Execute(
                new AdjustScoreCommand(TeamSide.Home, 1)).IsAccepted);
            Assert.IsTrue(session.Execute(
                new AdjustScoreCommand(TeamSide.Home, -1)).IsAccepted);
        }

        var expectedEvents = session.History.ToArray();

        GameDocument? loaded;
        try
        {
            await store.WaitForSequenceAsync(
                expectedEvents[^1].Sequence,
                testContext.CancellationToken);
            loaded = await jsonStore.LoadActiveAsync(testContext.CancellationToken);
        }
        finally
        {
            await persistence.StopAsync(testContext.CancellationToken);
        }

        Assert.IsTrue(created.IsAccepted);
        Assert.IsNotNull(loaded);
        Assert.HasCount(expectedEvents.Length, loaded.Events);
        CollectionAssert.AreEqual(
            expectedEvents.Select(matchEvent => matchEvent.EventId).ToArray(),
            loaded.Events.Select(matchEvent => matchEvent.EventId).ToArray());
        CollectionAssert.AreEqual(
            Enumerable.Range(1, expectedEvents.Length)
                .Select(sequence => (long)sequence)
                .ToArray(),
            loaded.Events.Select(matchEvent => matchEvent.Sequence).ToArray());
        Assert.AreEqual(expectedEvents[^1].Sequence, loaded.Snapshot.LastEventSequence);
        Assert.AreEqual(0, loaded.Snapshot.Home.Score);
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task EventsCommitted_BlockedStoreWrite_CommandCompletesBeforeWriteIsReleased()
    {
        var store = new ControlledGameStore(blockWrites: true);
        using var session = CreateCreatedSession();
        using var persistence = new MatchPersistenceService(
            session,
            store,
            TimeProvider.System);
        persistence.Start();
        await store.FirstSaveStarted.Task.WaitAsync(testContext.CancellationToken);

        CommandResult result;
        try
        {
            var commandTask = Task.Run(
                () => session.Execute(new AdjustScoreCommand(TeamSide.Home, 2)));
            result = await commandTask.WaitAsync(
                TimeSpan.FromSeconds(1),
                testContext.CancellationToken);
        }
        finally
        {
            store.ReleaseWrites();
            await persistence.StopAsync(testContext.CancellationToken);
        }

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(2, session.Snapshot.Home.Score);
        Assert.IsGreaterThanOrEqualTo(2, store.SavedDocuments.Count);
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task StopAsync_BlockedWriter_WaitsAndFlushesAllQueuedActions()
    {
        var store = new ControlledGameStore(blockWrites: true);
        using var session = CreateCreatedSession();
        using var persistence = new MatchPersistenceService(
            session,
            store,
            TimeProvider.System);
        persistence.Start();
        await store.FirstSaveStarted.Task.WaitAsync(testContext.CancellationToken);

        Assert.IsTrue(session.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        Assert.IsTrue(session.Execute(
            new AdjustFoulCommand(TeamSide.Away, 1)).IsAccepted);
        Assert.IsTrue(session.Execute(
            new ChangeTeamNameCommand(TeamSide.Home, "Centar")).IsAccepted);
        var expectedEvents = session.History.ToArray();

        var stopTask = persistence.StopAsync(testContext.CancellationToken);
        var completedBeforeRelease = stopTask.IsCompleted;
        store.ReleaseWrites();
        await stopTask;
        var finalDocument = store.SavedDocuments[^1];

        Assert.IsFalse(completedBeforeRelease);
        Assert.AreEqual("Centar", finalDocument.Snapshot.Home.Name);
        Assert.AreEqual(2, finalDocument.Snapshot.Home.Score);
        Assert.AreEqual(1, finalDocument.Snapshot.Away.Fouls);
        Assert.HasCount(expectedEvents.Length, finalDocument.Events);
        CollectionAssert.AreEqual(
            expectedEvents.Select(matchEvent => matchEvent.EventId).ToArray(),
            finalDocument.Events.Select(matchEvent => matchEvent.EventId).ToArray());
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task ProcessWrites_TransientFailures_DoNotAffectCommandsAndStopCheckpointRecoversAllEvents()
    {
        var store = new ControlledGameStore(failuresBeforeSuccess: 3);
        using var session = CreateEmptySession();
        using var persistence = new MatchPersistenceService(
            session,
            store,
            TimeProvider.System);
        var errorStatusObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.StatusChanged += (_, _) =>
        {
            if (persistence.Status.HasError)
            {
                errorStatusObserved.TrySetResult();
            }
        };
        persistence.Start();

        var created = ExecuteCreate(session);
        await errorStatusObserved.Task.WaitAsync(testContext.CancellationToken);
        var score = session.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        var foul = session.Execute(new AdjustFoulCommand(TeamSide.Away, 1));
        var expectedEvents = session.History.ToArray();

        await persistence.StopAsync(testContext.CancellationToken);
        var savedDocuments = store.SavedDocuments;
        var recoveredDocument = savedDocuments[^1];

        Assert.IsTrue(created.IsAccepted);
        Assert.IsTrue(score.IsAccepted);
        Assert.IsTrue(foul.IsAccepted);
        Assert.AreEqual(2, session.Snapshot.Home.Score);
        Assert.AreEqual(1, session.Snapshot.Away.Fouls);
        Assert.AreEqual(3, store.FailedSaveCount);
        Assert.HasCount(1, savedDocuments);
        Assert.IsFalse(persistence.Status.HasError);
        Assert.HasCount(expectedEvents.Length, recoveredDocument.Events);
        CollectionAssert.AreEqual(
            expectedEvents.Select(matchEvent => matchEvent.EventId).ToArray(),
            recoveredDocument.Events.Select(matchEvent => matchEvent.EventId).ToArray());
        Assert.AreEqual(
            expectedEvents[^1].Sequence,
            recoveredDocument.Snapshot.LastEventSequence);
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task ProcessWrites_FailedCurrentGame_RetriesAfterOpeningAnotherGame()
    {
        var store = new ControlledGameStore(failuresBeforeSuccess: 1);
        using var target = CreateCreatedSession();
        Assert.IsTrue(target.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        var targetDocument = GameDocument.Capture(
            target.Snapshot,
            target.History,
            DateTimeOffset.UtcNow);
        store.AddAvailableDocument(targetDocument);
        using var current = CreateCreatedSession();
        Assert.IsTrue(current.Execute(
            new AdjustScoreCommand(TeamSide.Away, 1)).IsAccepted);
        var currentGameId = current.Snapshot.GameId;
        var currentEvents = current.History.ToArray();
        using var persistence = new MatchPersistenceService(
            current,
            store,
            TimeProvider.System);
        var failureObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.StatusChanged += (_, _) =>
        {
            if (persistence.Status.HasError)
            {
                failureObserved.TrySetResult();
            }
        };
        persistence.Start();
        await failureObserved.Task.WaitAsync(testContext.CancellationToken);

        var opened = await persistence.OpenGameAsync(
            targetDocument.GameId,
            testContext.CancellationToken);
        var targetEventsAfterOpen = current.History.ToArray();
        await persistence.StopAsync(testContext.CancellationToken);
        var savedDocuments = store.SavedDocuments;
        var savedCurrent = savedDocuments
            .Where(document => document.GameId == currentGameId)
            .MaxBy(document => document.SavedAtUtc);
        var savedTarget = savedDocuments
            .Where(document => document.GameId == targetDocument.GameId)
            .MaxBy(document => document.SavedAtUtc);

        Assert.IsTrue(opened.IsAccepted);
        Assert.AreEqual(targetDocument.GameId, opened.State.GameId);
        Assert.AreEqual(1, store.FailedSaveCount);
        CollectionAssert.AreEquivalent(
            new[] { currentGameId, targetDocument.GameId },
            savedDocuments.Select(document => document.GameId).Distinct().ToArray());
        Assert.IsNotNull(savedCurrent);
        CollectionAssert.AreEqual(
            currentEvents.Select(matchEvent => matchEvent.EventId).ToArray(),
            savedCurrent.Events.Select(matchEvent => matchEvent.EventId).ToArray());
        Assert.IsNotNull(savedTarget);
        CollectionAssert.AreEqual(
            targetEventsAfterOpen.Select(matchEvent => matchEvent.EventId).ToArray(),
            savedTarget.Events.Select(matchEvent => matchEvent.EventId).ToArray());
        Assert.IsFalse(persistence.Status.HasError);
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task QueuePeriodicSnapshot_StaleSnapshotAfterAction_DoesNotOverwriteNewerAction()
    {
        var store = new ControlledGameStore(blockWrites: true);
        using var session = CreateCreatedSession();
        var stalePeriodicSnapshot = session.Snapshot;
        using var persistence = new MatchPersistenceService(
            session,
            store,
            TimeProvider.System);
        persistence.Start();
        await store.FirstSaveStarted.Task.WaitAsync(testContext.CancellationToken);

        var action = session.Execute(new AdjustScoreCommand(TeamSide.Home, 1));
        QueuePeriodicSnapshot(persistence, stalePeriodicSnapshot);
        var expectedEvents = session.History.ToArray();

        var stopTask = persistence.StopAsync(testContext.CancellationToken);
        store.ReleaseWrites();
        await stopTask;
        var savedDocuments = store.SavedDocuments;
        var actionSequence = expectedEvents[^1].Sequence;
        var firstActionSnapshot = savedDocuments
            .Select((document, index) => (document, index))
            .First(item => item.document.Snapshot.LastEventSequence == actionSequence)
            .index;
        var documentsAfterAction = savedDocuments.Skip(firstActionSnapshot).ToArray();
        var finalDocument = savedDocuments[^1];

        Assert.IsTrue(action.IsAccepted);
        Assert.IsNotEmpty(documentsAfterAction);
        Assert.IsTrue(documentsAfterAction.All(document =>
            document.Snapshot.LastEventSequence == actionSequence));
        Assert.IsTrue(documentsAfterAction.All(document =>
            document.Snapshot.Home.Score == 1));
        Assert.HasCount(expectedEvents.Length, finalDocument.Events);
        CollectionAssert.AreEqual(
            expectedEvents.Select(matchEvent => matchEvent.EventId).ToArray(),
            finalDocument.Events.Select(matchEvent => matchEvent.EventId).ToArray());
    }

    [TestMethod]
    public async Task OpenGameAsync_DifferentLoadedMatch_ExplicitlyReplacesWithoutMixingHistory()
    {
        var store = CreateJsonStore();
        using var saved = CreateCreatedSession();
        Assert.IsTrue(saved.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        Assert.IsTrue(saved.Execute(
            new AdjustFoulCommand(TeamSide.Away, 1)).IsAccepted);
        var savedDocument = GameDocument.Capture(
            saved.Snapshot,
            saved.History,
            DateTimeOffset.UtcNow);
        var savedPath = await store.SaveActiveAsync(
            savedDocument,
            testContext.CancellationToken);
        using var current = CreateCreatedSession();
        Assert.IsTrue(current.Execute(
            new AdjustScoreCommand(TeamSide.Away, 1)).IsAccepted);
        var replacedEventIds = current.History
            .Select(matchEvent => matchEvent.EventId)
            .ToHashSet();
        using var persistence = new MatchPersistenceService(
            current,
            store,
            TimeProvider.System);

        var games = await persistence.ListGamesAsync(testContext.CancellationToken);
        var result = await persistence.OpenGameAsync(
            savedDocument.GameId,
            testContext.CancellationToken);
        var continued = current.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1));

        Assert.HasCount(1, games);
        Assert.AreEqual(savedDocument.GameId, games[0].GameId);
        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(savedDocument.GameId, result.State.GameId);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(1, result.State.Away.Fouls);
        Assert.IsFalse(current.History.Any(matchEvent =>
            replacedEventIds.Contains(matchEvent.EventId)));
        CollectionAssert.AreEqual(
            savedDocument.Events.Select(matchEvent => matchEvent.EventId).ToArray(),
            current.History
                .Take(savedDocument.Events.Count)
                .Select(matchEvent => matchEvent.EventId)
                .ToArray());
        Assert.AreEqual(savedPath, persistence.Status.CurrentFile);
        Assert.IsFalse(persistence.Status.HasError);
        Assert.IsTrue(continued.IsAccepted);
        Assert.AreEqual(3, continued.State.Home.Score);
    }

    private JsonGameStore CreateJsonStore() =>
        new(
            new GameStoragePaths(
                Path.Combine(testRoot, "active"),
                Path.Combine(testRoot, "games")));

    private static MatchSession CreateEmptySession() =>
        new(new MatchEngine(), TimeProvider.System);

    private static MatchSession CreateCreatedSession()
    {
        var session = CreateEmptySession();
        var result = ExecuteCreate(session);
        if (!result.IsAccepted)
        {
            session.Dispose();
            throw new InvalidOperationException(result.Message);
        }

        return session;
    }

    private static CommandResult ExecuteCreate(MatchSession session) =>
        session.Execute(
            new CreateGameCommand(
                new MatchMetadata
                {
                    TournamentName = "3x3 Centar",
                    CourtName = "Main Court",
                },
                MatchRules.Fiba3x3,
                "Home",
                "Away",
                "#FFFFFF",
                "#FF5252"));

    private static void QueuePeriodicSnapshot(
        MatchPersistenceService persistence,
        MatchState snapshot)
    {
        var method = typeof(MatchPersistenceService).GetMethod(
            "QueuePeriodicSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "The periodic snapshot queue seam was not found.");
        _ = method.Invoke(persistence, [snapshot]);
    }

    private sealed class ControlledGameStore(
        bool blockWrites = false,
        int failuresBeforeSuccess = 0) : IGameStore
    {
        private readonly object gate = new();
        private readonly List<GameDocument> availableDocuments = [];
        private readonly List<GameDocument> savedDocuments = [];
        private readonly TaskCompletionSource writesReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int remainingFailures = failuresBeforeSuccess;
        private int failedSaveCount;

        public TaskCompletionSource FirstSaveStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string ActiveFilePath => "controlled-active-game.json";

        public string CompletedGamesDirectory => "controlled-games";

        public int FailedSaveCount
        {
            get
            {
                lock (gate)
                {
                    return failedSaveCount;
                }
            }
        }

        public IReadOnlyList<GameDocument> SavedDocuments
        {
            get
            {
                lock (gate)
                {
                    return [.. savedDocuments];
                }
            }
        }

        public Task<IReadOnlyList<SavedGameInfo>> ListGamesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SavedGameInfo> games = LoadableDocuments
                .GroupBy(document => document.GameId)
                .Select(group => group.MaxBy(document => document.SavedAtUtc)!)
                .OrderByDescending(document => document.CreatedAtUtc)
                .Select(document => new SavedGameInfo(
                    document.GameId,
                    document.CreatedAtUtc,
                    document.SavedAtUtc,
                    document.Snapshot.Stage,
                    document.Snapshot.Metadata.TournamentName,
                    document.Snapshot.Home.Name,
                    document.Snapshot.Home.Score,
                    document.Snapshot.Away.Name,
                    document.Snapshot.Away.Score,
                    ActiveFilePath))
                .ToArray();
            return Task.FromResult(games);
        }

        public Task<GameDocument?> LoadGameAsync(
            Guid gameId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = LoadableDocuments
                .Where(candidate => candidate.GameId == gameId)
                .MaxBy(candidate => candidate.SavedAtUtc);
            return Task.FromResult(document);
        }

        public async Task<string> SaveActiveAsync(
            GameDocument document,
            CancellationToken cancellationToken = default)
        {
            FirstSaveStarted.TrySetResult();

            lock (gate)
            {
                if (remainingFailures > 0)
                {
                    remainingFailures--;
                    failedSaveCount++;
                    throw new IOException("Injected transient save failure.");
                }
            }

            if (blockWrites)
            {
                await writesReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (gate)
            {
                savedDocuments.Add(
                    document with
                    {
                        Events = [.. document.Events],
                    });
            }

            return ActiveFilePath;
        }

        public Task<GameDocument?> LoadActiveAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documents = SavedDocuments;
            return Task.FromResult<GameDocument?>(
                documents.Count == 0 ? null : documents[^1]);
        }

        public Task<string> ArchiveCompletedAsync(
            GameDocument document,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("controlled-archive.json");
        }

        public Task<string> ExportAsync(
            GameDocument document,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(destinationPath);
        }

        public Task DeleteActiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void AddAvailableDocument(GameDocument document)
        {
            lock (gate)
            {
                availableDocuments.Add(
                    document with
                    {
                        Events = [.. document.Events],
                    });
            }
        }

        public void ReleaseWrites() => writesReleased.TrySetResult();

        private IReadOnlyList<GameDocument> LoadableDocuments
        {
            get
            {
                lock (gate)
                {
                    return [.. availableDocuments, .. savedDocuments];
                }
            }
        }
    }

    private sealed class ObservingGameStore(IGameStore inner) : IGameStore
    {
        private readonly object gate = new();
        private TaskCompletionSource saveObserved = CreateSignal();
        private long highestSavedSequence;

        public string ActiveFilePath => inner.ActiveFilePath;

        public string CompletedGamesDirectory => inner.CompletedGamesDirectory;

        public Task<IReadOnlyList<SavedGameInfo>> ListGamesAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListGamesAsync(cancellationToken);

        public Task<GameDocument?> LoadGameAsync(
            Guid gameId,
            CancellationToken cancellationToken = default) =>
            inner.LoadGameAsync(gameId, cancellationToken);

        public async Task<string> SaveActiveAsync(
            GameDocument document,
            CancellationToken cancellationToken = default)
        {
            var path = await inner
                .SaveActiveAsync(document, cancellationToken)
                .ConfigureAwait(false);
            TaskCompletionSource signal;
            lock (gate)
            {
                highestSavedSequence = Math.Max(
                    highestSavedSequence,
                    document.Snapshot.LastEventSequence);
                signal = saveObserved;
                saveObserved = CreateSignal();
            }

            signal.TrySetResult();
            return path;
        }

        public Task<GameDocument?> LoadActiveAsync(
            CancellationToken cancellationToken = default) =>
            inner.LoadActiveAsync(cancellationToken);

        public Task<string> ArchiveCompletedAsync(
            GameDocument document,
            CancellationToken cancellationToken = default) =>
            inner.ArchiveCompletedAsync(document, cancellationToken);

        public Task<string> ExportAsync(
            GameDocument document,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            inner.ExportAsync(document, destinationPath, cancellationToken);

        public Task DeleteActiveAsync(CancellationToken cancellationToken = default) =>
            inner.DeleteActiveAsync(cancellationToken);

        public async Task WaitForSequenceAsync(
            long sequence,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                Task waitForSave;
                lock (gate)
                {
                    if (highestSavedSequence >= sequence)
                    {
                        return;
                    }

                    waitForSave = saveObserved.Task;
                }

                await waitForSave.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static TaskCompletionSource CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
