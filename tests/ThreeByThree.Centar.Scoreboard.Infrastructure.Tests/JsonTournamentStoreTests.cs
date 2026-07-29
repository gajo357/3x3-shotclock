using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Tests;

[TestClass]
public sealed class JsonTournamentStoreTests : IDisposable
{
    private readonly TestContext testContext;
    private readonly string testRoot;
    private readonly JsonTournamentStore store;

    public JsonTournamentStoreTests(TestContext testContext)
    {
        this.testContext = testContext;
        testRoot = Path.Combine(
            Path.GetTempPath(),
            "ThreeByThree.Centar.Scoreboard.Tests",
            Guid.NewGuid().ToString("N"));
        store = new JsonTournamentStore(
            new GameStoragePaths(
                Path.Combine(testRoot, "active"),
                Path.Combine(testRoot, "games"),
                Path.Combine(testRoot, "tournaments")));
    }

    public void Dispose()
    {
        store.Dispose();
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [TestMethod]
    public async Task SaveThenList_EmptyTournament_RoundTripsWithoutInventingOptionalData()
    {
        var tournament = CreateTournament("Summer Cup");

        await store.SaveAsync(tournament, testContext.CancellationToken);
        var loaded = Assert.ContainsSingle(
            await store.ListAsync(testContext.CancellationToken));

        Assert.AreEqual(tournament.Id, loaded.Id);
        Assert.AreEqual("Summer Cup", loaded.Name);
        Assert.AreEqual(tournament.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.IsNotNull(loaded.Teams);
        Assert.IsEmpty(loaded.Teams);
    }

    [TestMethod]
    public async Task SaveThenList_TournamentWithMixedRosterAndImages_RoundTripsCompleteAggregate()
    {
        var firstTeamId = Guid.NewGuid();
        var firstPlayerId = Guid.NewGuid();
        var secondPlayerId = Guid.NewGuid();
        var secondTeamId = Guid.NewGuid();
        var tournament = CreateTournament("City Finals") with
        {
            Teams =
            [
                new TournamentTeam
                {
                    Id = firstTeamId,
                    Name = "Centar",
                    ColorHex = "#A1B2C3",
                    ImagePath = @"assets\centar.png",
                    Players =
                    [
                        new TournamentPlayer
                        {
                            Id = firstPlayerId,
                            Name = "Mila",
                            ImagePath = @"assets\mila.jpg",
                        },
                        new TournamentPlayer
                        {
                            Id = secondPlayerId,
                            Name = "Ena",
                        },
                    ],
                },
                new TournamentTeam
                {
                    Id = secondTeamId,
                    Name = "Rivals",
                    ColorHex = "#112233",
                },
            ],
        };

        await store.SaveAsync(tournament, testContext.CancellationToken);
        var loaded = Assert.ContainsSingle(
            await store.ListAsync(testContext.CancellationToken));

        Assert.HasCount(2, loaded.Teams);
        CollectionAssert.AreEqual(
            new[] { firstTeamId, secondTeamId },
            loaded.Teams.Select(team => team.Id).ToArray());
        var firstTeam = loaded.Teams[0];
        Assert.AreEqual("Centar", firstTeam.Name);
        Assert.AreEqual("#A1B2C3", firstTeam.ColorHex);
        Assert.AreEqual(@"assets\centar.png", firstTeam.ImagePath);
        Assert.HasCount(2, firstTeam.Players);
        CollectionAssert.AreEqual(
            new[] { firstPlayerId, secondPlayerId },
            firstTeam.Players.Select(player => player.Id).ToArray());
        Assert.AreEqual("Mila", firstTeam.Players[0].Name);
        Assert.AreEqual(@"assets\mila.jpg", firstTeam.Players[0].ImagePath);
        Assert.AreEqual("Ena", firstTeam.Players[1].Name);
        Assert.IsNull(firstTeam.Players[1].ImagePath);
        Assert.IsNull(loaded.Teams[1].ImagePath);
        Assert.IsNotNull(loaded.Teams[1].Players);
        Assert.IsEmpty(loaded.Teams[1].Players);
    }

    [TestMethod]
    public async Task SaveAsync_ExistingTournament_ReplacesAtomicallyWithoutTemporaryFiles()
    {
        var tournament = CreateTournament("Opening Day");
        await store.SaveAsync(tournament, testContext.CancellationToken);
        var updated = tournament with
        {
            Name = "Opening Day Updated",
            Teams =
            [
                new TournamentTeam
                {
                    Id = Guid.NewGuid(),
                    Name = "Home",
                    ColorHex = "#ABCDEF",
                },
            ],
        };

        await store.SaveAsync(updated, testContext.CancellationToken);
        var loaded = Assert.ContainsSingle(
            await store.ListAsync(testContext.CancellationToken));
        var jsonFiles = Directory.GetFiles(
            store.TournamentsDirectory,
            "*.json",
            SearchOption.TopDirectoryOnly);
        var temporaryFiles = Directory.GetFiles(
            store.TournamentsDirectory,
            "*.tmp",
            SearchOption.AllDirectories);

        Assert.AreEqual(tournament.Id, loaded.Id);
        Assert.AreEqual("Opening Day Updated", loaded.Name);
        Assert.HasCount(1, loaded.Teams);
        Assert.AreEqual("Home", loaded.Teams[0].Name);
        Assert.HasCount(1, jsonFiles);
        Assert.AreEqual(
            tournament.Id.ToString("D") + ".json",
            Path.GetFileName(jsonFiles[0]));
        Assert.IsEmpty(temporaryFiles);
    }

    [TestMethod]
    public async Task ListAsync_TournamentJsonWithoutOptionalCollections_UsesSafeDefaults()
    {
        Directory.CreateDirectory(store.TournamentsDirectory);
        var emptyId = Guid.NewGuid();
        var rosterId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var emptyJson = $$"""
            {
              "id": "{{emptyId:D}}",
              "name": "Legacy Empty Cup",
              "createdAtUtc": "2026-07-27T10:00:00+00:00"
            }
            """;
        var rosterJson = $$"""
            {
              "id": "{{rosterId:D}}",
              "name": "Legacy Roster Cup",
              "createdAtUtc": "2026-07-28T10:00:00+00:00",
              "teams": [
                {
                  "id": "{{teamId:D}}",
                  "name": "Legacy Team",
                  "colorHex": "#123456"
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(store.TournamentsDirectory, "empty.json"),
            emptyJson,
            testContext.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(store.TournamentsDirectory, "roster.json"),
            rosterJson,
            testContext.CancellationToken);

        var loaded = await store.ListAsync(testContext.CancellationToken);

        Assert.HasCount(2, loaded);
        Assert.AreEqual(rosterId, loaded[0].Id);
        Assert.HasCount(1, loaded[0].Teams);
        Assert.AreEqual(teamId, loaded[0].Teams[0].Id);
        Assert.IsNull(loaded[0].Teams[0].ImagePath);
        Assert.IsNotNull(loaded[0].Teams[0].Players);
        Assert.IsEmpty(loaded[0].Teams[0].Players);
        Assert.AreEqual(emptyId, loaded[1].Id);
        Assert.IsNotNull(loaded[1].Teams);
        Assert.IsEmpty(loaded[1].Teams);
    }

    [TestMethod]
    [DataRow(".PNG", ".png")]
    [DataRow(".JpEg", ".jpeg")]
    public async Task ImportImageAsync_SupportedImage_CopiesExactBytesIntoTournamentAssetFolder(
        string sourceExtension,
        string expectedExtension)
    {
        var tournamentId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        byte[] expectedBytes = [0, 17, 34, 51, 68, 85, 102, 119];
        Directory.CreateDirectory(testRoot);
        var sourcePath = Path.Combine(testRoot, "source" + sourceExtension);
        await File.WriteAllBytesAsync(
            sourcePath,
            expectedBytes,
            testContext.CancellationToken);

        var destinationPath = await store.ImportImageAsync(
            tournamentId,
            subjectId,
            sourcePath,
            testContext.CancellationToken);
        var actualBytes = await File.ReadAllBytesAsync(
            destinationPath,
            testContext.CancellationToken);
        var temporaryFiles = Directory.GetFiles(
            store.TournamentsDirectory,
            "*.tmp",
            SearchOption.AllDirectories);

        Assert.AreEqual(
            Path.Combine(
                store.TournamentsDirectory,
                "assets",
                tournamentId.ToString("N")),
            Path.GetDirectoryName(destinationPath));
        Assert.AreEqual(
            subjectId.ToString("N") + expectedExtension,
            Path.GetFileName(destinationPath));
        CollectionAssert.AreEqual(expectedBytes, actualBytes);
        Assert.IsEmpty(temporaryFiles);
    }

    [TestMethod]
    public async Task ImportImageAsync_MissingSource_ThrowsWithoutCreatingAssets()
    {
        var missingPath = Path.Combine(testRoot, "missing.png");

        var exception = await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => store.ImportImageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                missingPath,
                testContext.CancellationToken));

        Assert.AreEqual(missingPath, exception.FileName);
        Assert.IsFalse(Directory.Exists(store.TournamentsDirectory));
    }

    [TestMethod]
    public async Task ImportImageAsync_UnsupportedSource_ThrowsWithoutCreatingAssets()
    {
        Directory.CreateDirectory(testRoot);
        var sourcePath = Path.Combine(testRoot, "portrait.webp");
        await File.WriteAllBytesAsync(
            sourcePath,
            [1, 2, 3],
            testContext.CancellationToken);

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.ImportImageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                sourcePath,
                testContext.CancellationToken));

        Assert.AreEqual(
            "Team and player images must be BMP, GIF, JPEG, or PNG files.",
            exception.Message);
        Assert.IsFalse(Directory.Exists(store.TournamentsDirectory));
    }

    [TestMethod]
    public async Task ImportImageAsync_EmptyIdentity_ThrowsExactArgumentError()
    {
        Directory.CreateDirectory(testRoot);
        var sourcePath = Path.Combine(testRoot, "portrait.png");
        await File.WriteAllBytesAsync(
            sourcePath,
            [1, 2, 3],
            testContext.CancellationToken);

        var tournamentException = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.ImportImageAsync(
                Guid.Empty,
                Guid.NewGuid(),
                sourcePath,
                testContext.CancellationToken));
        var subjectException = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.ImportImageAsync(
                Guid.NewGuid(),
                Guid.Empty,
                sourcePath,
                testContext.CancellationToken));

        Assert.AreEqual("tournamentId", tournamentException.ParamName);
        Assert.AreEqual("subjectId", subjectException.ParamName);
        Assert.IsFalse(Directory.Exists(store.TournamentsDirectory));
    }

    [TestMethod]
    public async Task ListAsync_MalformedTournament_DoesNotHideValidTournament()
    {
        var valid = CreateTournament("Valid Cup");
        await store.SaveAsync(valid, testContext.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(store.TournamentsDirectory, "malformed.json"),
            "{ not valid JSON",
            testContext.CancellationToken);
        var duplicateTeamId = Guid.NewGuid();
        var invalidJson = $$"""
            {
              "id": "{{Guid.NewGuid():D}}",
              "name": "Invalid Cup",
              "createdAtUtc": "2026-07-29T10:00:00+00:00",
              "teams": [
                { "id": "{{duplicateTeamId:D}}", "name": "One", "colorHex": "#111111" },
                { "id": "{{duplicateTeamId:D}}", "name": "Two", "colorHex": "#222222" }
              ]
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(store.TournamentsDirectory, "invalid.json"),
            invalidJson,
            testContext.CancellationToken);

        var loaded = await store.ListAsync(testContext.CancellationToken);

        var tournament = Assert.ContainsSingle(loaded);
        Assert.AreEqual(valid.Id, tournament.Id);
        Assert.AreEqual("Valid Cup", tournament.Name);
    }

    [TestMethod]
    public async Task SaveAsync_NullCollections_ThrowsInvalidDataException()
    {
        var nullTeams = CreateTournament("Null Teams") with
        {
            Teams = null!,
        };
        var nullPlayers = CreateTournament("Null Players") with
        {
            Teams =
            [
                new TournamentTeam
                {
                    Id = Guid.NewGuid(),
                    Name = "Team",
                    ColorHex = "#123456",
                    Players = null!,
                },
            ],
        };

        var teamsException = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.SaveAsync(nullTeams, testContext.CancellationToken));
        var playersException = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.SaveAsync(nullPlayers, testContext.CancellationToken));

        Assert.AreEqual("Tournament teams cannot be null.", teamsException.Message);
        Assert.AreEqual("Team players cannot be null.", playersException.Message);
        Assert.IsFalse(Directory.Exists(store.TournamentsDirectory));
    }

    private static Tournament CreateTournament(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAtUtc = new DateTimeOffset(
                2026,
                7,
                29,
                10,
                30,
                0,
                TimeSpan.Zero),
        };
}
