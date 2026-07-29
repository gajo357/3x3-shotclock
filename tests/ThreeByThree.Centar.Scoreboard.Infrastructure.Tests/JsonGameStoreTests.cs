using System.Text.Json.Nodes;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Settings;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Tests;

[TestClass]
public sealed class JsonGameStoreTests
{
    private string testRoot = null!;
    private JsonGameStore store = null!;

    [TestInitialize]
    public void Initialize()
    {
        testRoot = Path.Combine(
            Path.GetTempPath(),
            "ThreeByThree.Centar.Scoreboard.Tests",
            Guid.NewGuid().ToString("N"));
        store = new JsonGameStore(
            new GameStoragePaths(
                Path.Combine(testRoot, "active"),
                Path.Combine(testRoot, "games")));
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
    public async Task SaveThenLoad_RoundTripsPolymorphicEventsAndSnapshot()
    {
        using var session = CreateSession();
        session.Execute(new ChangeTeamNameCommand(TeamSide.Home, "Centar"));
        session.Execute(new ChangeTeamColorCommand(TeamSide.Away, "#112233"));
        session.Execute(new AdjustScoreCommand(TeamSide.Home, 2));
        session.Execute(new AdjustFoulCommand(TeamSide.Away, 1));
        session.Execute(new ResetClockCommand(ClockKind.Shot, Stop: true));
        var document = Capture(session);

        var path = await store.SaveActiveAsync(document);
        var loaded = await store.LoadActiveAsync();

        Assert.AreEqual(store.CompletedGamesDirectory, Path.GetDirectoryName(path));
        Assert.IsNotNull(loaded);
        Assert.AreEqual(document.GameId, loaded.GameId);
        Assert.AreEqual(2, loaded.Snapshot.Home.Score);
        Assert.AreEqual(1, loaded.Snapshot.Away.Fouls);
        Assert.HasCount(document.Events.Count, loaded.Events);
        Assert.IsInstanceOfType<GameCreatedEvent>(loaded.Events[0]);
        Assert.IsTrue(loaded.Events.Any(matchEvent => matchEvent is TeamNameChangedEvent));
        Assert.IsTrue(loaded.Events.Any(matchEvent => matchEvent is ClockChangedEvent));
    }

    [TestMethod]
    public async Task SaveThenLoad_OvertimePossession_RoundTripsAndReplays()
    {
        var metadata = new MatchMetadata
        {
            TournamentName = "3x3 Centar",
            CourtName = "Main Court",
            CoinTossWinner = TeamSide.Away,
            CoinTossSelection = CoinTossChoice.OpeningPossession,
        };
        using var session = CreateSession(metadata);
        Assert.IsTrue(session.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        Assert.IsTrue(session.Execute(
            new AdjustScoreCommand(TeamSide.Away, 2)).IsAccepted);
        Assert.IsTrue(session.Execute(
            new SetLinkedClocksRunningCommand(true)).IsAccepted);
        Assert.IsTrue(session.Execute(
            new ExpireClockCommand(ClockKind.Game)).IsAccepted);
        var overtime = session.Execute(new StartOvertimeCommand());
        var document = Capture(session);

        await store.SaveActiveAsync(document);
        var loaded = await store.LoadGameAsync(document.GameId);

        Assert.IsTrue(overtime.IsAccepted);
        Assert.IsNotNull(loaded);
        var createdEvent = Assert.ContainsSingle(
            loaded.Events.OfType<GameCreatedEvent>());
        var overtimeEvent = Assert.ContainsSingle(
            loaded.Events.OfType<OvertimeStartedEvent>());
        Assert.AreEqual(TeamSide.Away, createdEvent.MatchMetadata.CoinTossWinner);
        Assert.AreEqual(
            CoinTossChoice.OpeningPossession,
            createdEvent.MatchMetadata.CoinTossSelection);
        Assert.AreEqual(TeamSide.Home, overtimeEvent.StartingPossession);
        Assert.AreEqual(TeamSide.Away, loaded.Snapshot.Metadata.CoinTossWinner);
        Assert.AreEqual(
            CoinTossChoice.OpeningPossession,
            loaded.Snapshot.Metadata.CoinTossSelection);
        Assert.AreEqual(TeamSide.Home, loaded.Snapshot.StartingPossession);
        Assert.AreEqual(MatchStage.Overtime, loaded.Snapshot.Stage);

        var replayed = MatchReducer.Replay(loaded.Events);

        Assert.AreEqual(TeamSide.Away, replayed.Metadata.CoinTossWinner);
        Assert.AreEqual(
            CoinTossChoice.OpeningPossession,
            replayed.Metadata.CoinTossSelection);
        Assert.AreEqual(TeamSide.Home, replayed.StartingPossession);
        Assert.AreEqual(MatchStage.Overtime, replayed.Stage);
        Assert.AreEqual(loaded.Snapshot.LastEventSequence, replayed.LastEventSequence);
    }

    [TestMethod]
    public async Task SaveThenLoad_TournamentLinkAndGroupClassification_RoundTripsAndReplays()
    {
        var tournamentId = Guid.NewGuid();
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var metadata = new MatchMetadata
        {
            TournamentId = tournamentId,
            TournamentName = "City Cup",
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            CourtName = "Center Court",
            Category = "Under 18",
            GameType = GameType.Group,
            Group = "Z",
        };
        using var session = CreateSession(metadata);
        Assert.IsTrue(session.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        var document = Capture(session);

        await store.SaveActiveAsync(document);
        var loaded = await store.LoadGameAsync(document.GameId);

        Assert.IsNotNull(loaded);
        var createdEvent = Assert.ContainsSingle(
            loaded.Events.OfType<GameCreatedEvent>());
        Assert.AreEqual(metadata, createdEvent.MatchMetadata);
        Assert.AreEqual(metadata, loaded.Snapshot.Metadata);
        Assert.AreEqual(tournamentId, loaded.Snapshot.Metadata.TournamentId);
        Assert.AreEqual(homeTeamId, loaded.Snapshot.Metadata.HomeTeamId);
        Assert.AreEqual(awayTeamId, loaded.Snapshot.Metadata.AwayTeamId);
        Assert.AreEqual(GameType.Group, loaded.Snapshot.Metadata.GameType);
        Assert.AreEqual("Z", loaded.Snapshot.Metadata.Group);
        Assert.AreEqual("Home", loaded.Snapshot.Home.Name);
        Assert.AreEqual("#FFFFFF", loaded.Snapshot.Home.ColorHex);
        Assert.AreEqual(2, loaded.Snapshot.Home.Score);
        Assert.AreEqual("Away", loaded.Snapshot.Away.Name);
        Assert.AreEqual("#FF5252", loaded.Snapshot.Away.ColorHex);

        var replayed = MatchReducer.Replay(loaded.Events);

        Assert.AreEqual(metadata, replayed.Metadata);
        Assert.AreEqual(homeTeamId, replayed.Metadata.HomeTeamId);
        Assert.AreEqual(awayTeamId, replayed.Metadata.AwayTeamId);
        Assert.AreEqual(2, replayed.Home.Score);
        Assert.AreEqual(loaded.Snapshot.LastEventSequence, replayed.LastEventSequence);
    }

    [TestMethod]
    public async Task LoadGame_LegacyJsonWithoutTournamentTeamIdsGameTypeOrGroup_LoadsWithDefaults()
    {
        var metadata = new MatchMetadata
        {
            TournamentId = Guid.NewGuid(),
            TournamentName = "Legacy Cup",
            HomeTeamId = Guid.NewGuid(),
            AwayTeamId = Guid.NewGuid(),
            CourtName = "Legacy Court",
            Category = "Under 18",
            GameType = GameType.Group,
            Group = "A",
        };
        using var session = CreateSession(metadata);
        Assert.IsTrue(session.Execute(
            new AdjustScoreCommand(TeamSide.Away, 1)).IsAccepted);
        var document = Capture(session);
        var path = await store.SaveActiveAsync(document);
        await StripNewMatchMetadataAsync(path);

        var loaded = await store.LoadGameAsync(document.GameId);

        Assert.IsNotNull(loaded);
        Assert.IsNull(loaded.Snapshot.Metadata.TournamentId);
        Assert.IsNull(loaded.Snapshot.Metadata.HomeTeamId);
        Assert.IsNull(loaded.Snapshot.Metadata.AwayTeamId);
        Assert.AreEqual(GameType.Unspecified, loaded.Snapshot.Metadata.GameType);
        Assert.AreEqual(string.Empty, loaded.Snapshot.Metadata.Group);
        Assert.AreEqual("Legacy Cup", loaded.Snapshot.Metadata.TournamentName);
        Assert.AreEqual("UNDER 18", loaded.Snapshot.Metadata.GetGameTypeLabel());
        Assert.AreEqual("Home", loaded.Snapshot.Home.Name);
        Assert.AreEqual("Away", loaded.Snapshot.Away.Name);
        Assert.AreEqual(1, loaded.Snapshot.Away.Score);
        Assert.AreEqual(MatchStage.Regular, loaded.Snapshot.Stage);

        var replayed = MatchReducer.Replay(loaded.Events);

        Assert.AreEqual(loaded.Snapshot.Metadata, replayed.Metadata);
        Assert.AreEqual(loaded.Snapshot.GameId, replayed.GameId);
        Assert.AreEqual(1, replayed.Away.Score);
        Assert.AreEqual(loaded.Snapshot.LastEventSequence, replayed.LastEventSequence);
    }

    [TestMethod]
    public async Task ListGames_NewOptionalMetadataMissing_DoesNotSkipLegacyGame()
    {
        var metadata = new MatchMetadata
        {
            TournamentId = Guid.NewGuid(),
            TournamentName = "Legacy Listed Cup",
            HomeTeamId = Guid.NewGuid(),
            AwayTeamId = Guid.NewGuid(),
            Category = "Qualifier",
            GameType = GameType.Qualifier,
        };
        using var session = CreateSession(metadata);
        var document = Capture(session);
        var path = await store.SaveActiveAsync(document);
        await StripNewMatchMetadataAsync(path);

        var games = await store.ListGamesAsync();

        var game = Assert.ContainsSingle(games);
        Assert.AreEqual(document.GameId, game.GameId);
        Assert.AreEqual("Legacy Listed Cup", game.TournamentName);
        Assert.AreEqual("Home", game.HomeName);
        Assert.AreEqual("Away", game.AwayName);
        Assert.AreEqual(MatchStage.Regular, game.Stage);
        Assert.AreEqual(path, game.FilePath);
    }

    [TestMethod]
    public async Task SaveActive_GameFileNameUsesLocalIsoDateThenGameIdInLibraryDirectory()
    {
        using var session = CreateSession();
        var createdAtUtc = new DateTimeOffset(
            2026,
            7,
            27,
            12,
            30,
            0,
            TimeSpan.Zero);
        var document = Capture(session) with
        {
            CreatedAtUtc = createdAtUtc,
            SavedAtUtc = createdAtUtc.AddMinutes(4),
        };
        var expectedFileName = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{createdAtUtc.ToLocalTime():yyyy-MM-dd}_{document.GameId:D}.json");

        var path = await store.SaveActiveAsync(document);

        Assert.AreEqual(store.CompletedGamesDirectory, Path.GetDirectoryName(path));
        Assert.AreEqual(expectedFileName, Path.GetFileName(path));
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public async Task ListGames_ValidDocuments_ReturnsAllLatestFirst()
    {
        using var newestSession = CreateSession();
        using var secondSession = CreateSession();
        using var oldestSession = CreateSession();
        Assert.IsTrue(oldestSession.Execute(new EndGameCommand()).IsAccepted);
        var newest = Capture(newestSession) with
        {
            CreatedAtUtc = new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero),
            SavedAtUtc = new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero),
        };
        var second = Capture(secondSession) with
        {
            CreatedAtUtc = newest.CreatedAtUtc,
            SavedAtUtc = newest.SavedAtUtc.AddMinutes(-1),
        };
        var oldest = Capture(oldestSession) with
        {
            CreatedAtUtc = newest.CreatedAtUtc.AddDays(-1),
            SavedAtUtc = newest.SavedAtUtc.AddDays(1),
        };
        await store.SaveActiveAsync(oldest);
        await store.SaveActiveAsync(newest);
        await store.SaveActiveAsync(second);

        var games = await store.ListGamesAsync();

        Assert.HasCount(3, games);
        CollectionAssert.AreEqual(
            new[] { newest.GameId, second.GameId, oldest.GameId },
            games.Select(game => game.GameId).ToArray());
        Assert.AreEqual("3x3 Centar", games[0].TournamentName);
        Assert.AreEqual("Home", games[0].HomeName);
        Assert.AreEqual("Away", games[0].AwayName);
        Assert.IsFalse(games[0].IsFinished);
        Assert.IsTrue(games[2].IsFinished);
        Assert.IsTrue(games.All(game =>
            Path.GetDirectoryName(game.FilePath) == store.CompletedGamesDirectory));
    }

    [TestMethod]
    public async Task ListGames_MalformedAndInvalidJson_SkipsBadFiles()
    {
        using var firstSession = CreateSession();
        using var secondSession = CreateSession();
        var first = Capture(firstSession);
        var second = Capture(secondSession) with
        {
            CreatedAtUtc = first.CreatedAtUtc.AddMinutes(-1),
            SavedAtUtc = first.SavedAtUtc.AddMinutes(-1),
        };
        await store.SaveActiveAsync(first);
        await store.SaveActiveAsync(second);
        await File.WriteAllTextAsync(
            Path.Combine(store.CompletedGamesDirectory, "malformed.json"),
            "{ this is not valid JSON");
        await File.WriteAllTextAsync(
            Path.Combine(store.CompletedGamesDirectory, "incomplete.json"),
            "{}");

        var games = await store.ListGamesAsync();
        var loadedFirst = await store.LoadGameAsync(first.GameId);
        var missing = await store.LoadGameAsync(Guid.NewGuid());

        Assert.HasCount(2, games);
        CollectionAssert.AreEquivalent(
            new[] { first.GameId, second.GameId },
            games.Select(game => game.GameId).ToArray());
        Assert.IsNotNull(loadedFirst);
        Assert.AreEqual(first.GameId, loadedFirst.GameId);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task LoadActive_ValidatedDocument_RecoversNewSessionPaused()
    {
        using var original = CreateSession();
        Assert.IsTrue(original.Execute(
            new AdjustScoreCommand(TeamSide.Home, 2)).IsAccepted);
        Assert.IsTrue(original.Execute(
            new AdjustFoulCommand(TeamSide.Away, 1)).IsAccepted);
        Assert.IsTrue(original.Execute(
            new SetLinkedClocksRunningCommand(true)).IsAccepted);
        var saved = Capture(original);
        await store.SaveActiveAsync(saved);

        var loaded = await store.LoadActiveAsync();
        using var recovered = new MatchSession(new MatchEngine(), TimeProvider.System);
        Assert.IsNotNull(loaded);
        var result = recovered.Recover(loaded.Events, loaded.Snapshot);
        var loadedEventIds = loaded.Events
            .Select(matchEvent => matchEvent.EventId)
            .ToArray();
        var recoveredOriginalEventIds = recovered.History
            .Take(loaded.Events.Count)
            .Select(matchEvent => matchEvent.EventId)
            .ToArray();

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(2, result.State.Home.Score);
        Assert.AreEqual(1, result.State.Away.Fouls);
        Assert.AreEqual(MatchStatus.Paused, result.State.Status);
        Assert.IsFalse(result.State.GameClock.IsRunning);
        Assert.IsFalse(result.State.ShotClock.IsRunning);
        Assert.HasCount(2, result.Events);
        Assert.IsTrue(result.Events.All(matchEvent =>
            matchEvent is ClockChangedEvent
            {
                Metadata.Source: CommandSource.Recovery,
                IsRunning: false,
            }));
        CollectionAssert.AreEqual(loadedEventIds, recoveredOriginalEventIds);
    }

    [TestMethod]
    public async Task SaveActive_ExistingFile_ReplacesItAndLeavesNoTemporaryFiles()
    {
        using var session = CreateSession();
        var path = await store.SaveActiveAsync(Capture(session));
        session.Execute(new AdjustScoreCommand(TeamSide.Away, 1));

        await store.SaveActiveAsync(Capture(session));
        var loaded = await store.LoadActiveAsync();
        var temporaryFiles = Directory.GetFiles(
            Path.GetDirectoryName(path)!,
            "*.tmp");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded.Snapshot.Away.Score);
        Assert.IsEmpty(temporaryFiles);
    }

    [TestMethod]
    public async Task ArchiveCompleted_ExistingGame_AtomicallyUpdatesStableMatchFile()
    {
        using var session = CreateSession();
        session.Execute(new AdjustScoreCommand(TeamSide.Home, 21));
        session.Execute(new EndGameCommand());
        var firstDocument = Capture(session);
        var firstPath = await store.ArchiveCompletedAsync(firstDocument);
        var firstBytes = await File.ReadAllBytesAsync(firstPath);
        var laterDocument = firstDocument with
        {
            SavedAtUtc = firstDocument.SavedAtUtc.AddMinutes(5),
        };

        var secondPath = await store.ArchiveCompletedAsync(laterDocument);
        var secondBytes = await File.ReadAllBytesAsync(secondPath);
        var loaded = await store.LoadGameAsync(laterDocument.GameId);
        var temporaryFiles = Directory.GetFiles(
            store.CompletedGamesDirectory,
            "*.tmp");

        Assert.AreEqual(firstPath, secondPath);
        Assert.IsFalse(firstBytes.SequenceEqual(secondBytes));
        Assert.IsNotNull(loaded);
        Assert.AreEqual(laterDocument.SavedAtUtc, loaded.SavedAtUtc);
        Assert.IsEmpty(temporaryFiles);
    }

    [TestMethod]
    public async Task ListGames_TamperedSnapshot_SkipsInvalidDocument()
    {
        using var session = CreateSession();
        var document = Capture(session) with
        {
            Snapshot = session.Snapshot with
            {
                Home = session.Snapshot.Home with { Score = 99 },
            },
        };
        await store.SaveActiveAsync(document);

        var games = await store.ListGamesAsync();
        var loaded = await store.LoadGameAsync(document.GameId);

        Assert.IsEmpty(games);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task ListGames_TamperedMatchMetadata_SkipsInvalidDocument()
    {
        var metadata = new MatchMetadata
        {
            TournamentId = Guid.NewGuid(),
            TournamentName = "Integrity Cup",
            HomeTeamId = Guid.NewGuid(),
            AwayTeamId = Guid.NewGuid(),
            GameType = GameType.Group,
            Group = "A",
        };
        using var session = CreateSession(metadata);
        var document = Capture(session) with
        {
            Snapshot = session.Snapshot with
            {
                Metadata = session.Snapshot.Metadata with
                {
                    AwayTeamId = Guid.NewGuid(),
                    Group = "B",
                },
            },
        };
        await store.SaveActiveAsync(document);

        var games = await store.ListGamesAsync();
        var loaded = await store.LoadGameAsync(document.GameId);

        Assert.IsEmpty(games);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task ListGames_MissingEventSequence_SkipsInvalidDocument()
    {
        using var session = CreateSession();
        Assert.IsTrue(session.Execute(
            new AdjustScoreCommand(TeamSide.Home, 1)).IsAccepted);
        Assert.IsTrue(session.Execute(
            new AdjustFoulCommand(TeamSide.Away, 1)).IsAccepted);
        var complete = Capture(session);
        var missingSequence = complete with
        {
            Events =
            [
                complete.Events[0],
                complete.Events[2],
            ],
        };
        await store.SaveActiveAsync(missingSequence);

        var games = await store.ListGamesAsync();
        var loaded = await store.LoadGameAsync(missingSequence.GameId);

        Assert.IsEmpty(games);
        Assert.IsNull(loaded);
    }

    [TestMethod]
    public async Task Settings_SaveThenLoad_NormalizesAndAtomicallyReplaces()
    {
        var paths = new GameStoragePaths(
            Path.Combine(testRoot, "active"),
            Path.Combine(testRoot, "games"));
        var settingsStore = new JsonSettingsStore(paths);
        await settingsStore.SaveAsync(
            new AppSettings
            {
                AudioEnabled = false,
                VolumePercent = 150,
                ScoreboardTopmost = false,
                SelectedMonitorDeviceName = "  DISPLAY2  ",
            });
        await settingsStore.SaveAsync(
            new AppSettings
            {
                AudioEnabled = true,
                VolumePercent = 55,
                ScoreboardTopmost = true,
                SelectedMonitorDeviceName = "DISPLAY3",
            });

        var loaded = await settingsStore.LoadAsync();
        var temporaryFiles = Directory.GetFiles(
            Path.GetDirectoryName(settingsStore.SettingsFilePath)!,
            "*.tmp");

        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.AudioEnabled);
        Assert.AreEqual(55, loaded.VolumePercent);
        Assert.IsTrue(loaded.ScoreboardTopmost);
        Assert.AreEqual("DISPLAY3", loaded.SelectedMonitorDeviceName);
        Assert.IsEmpty(temporaryFiles);
    }

    private static MatchSession CreateSession(MatchMetadata? metadata = null)
    {
        var session = new MatchSession(new MatchEngine(), TimeProvider.System);
        var result = session.Execute(
            new CreateGameCommand(
                metadata ?? new MatchMetadata
                {
                    TournamentName = "3x3 Centar",
                    CourtName = "Main Court",
                },
                MatchRules.Fiba3x3,
                "Home",
                "Away",
                "#FFFFFF",
                "#FF5252"));
        if (!result.IsAccepted)
        {
            session.Dispose();
            throw new InvalidOperationException(result.Message);
        }

        return session;
    }

    private static GameDocument Capture(MatchSession session) =>
        GameDocument.Capture(
            session.Snapshot,
            session.History,
            DateTimeOffset.UtcNow);

    private static async Task StripNewMatchMetadataAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("The saved game JSON could not be parsed.");
        var snapshotMetadata = root["snapshot"]?["metadata"]?.AsObject()
            ?? throw new InvalidDataException("Snapshot metadata was not found.");
        var createdMetadata = root["events"]?[0]?["matchMetadata"]?.AsObject()
            ?? throw new InvalidDataException("Created-event metadata was not found.");
        RemoveNewMetadata(snapshotMetadata);
        RemoveNewMetadata(createdMetadata);
        await File.WriteAllTextAsync(path, root.ToJsonString());
    }

    private static void RemoveNewMetadata(JsonObject metadata)
    {
        _ = metadata.Remove("tournamentId");
        _ = metadata.Remove("homeTeamId");
        _ = metadata.Remove("awayTeamId");
        _ = metadata.Remove("gameType");
        _ = metadata.Remove("group");
    }
}
