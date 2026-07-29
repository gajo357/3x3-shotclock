using System.Text.Json;
using System.Text.Json.Serialization;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

public sealed class JsonGameStore(GameStoragePaths paths) : IGameStore
{
    private const string LegacyActiveFileName = "active-game.json";
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public string ActiveFilePath => Path.Combine(paths.ActiveDirectory, LegacyActiveFileName);

    public string CompletedGamesDirectory => paths.CompletedGamesDirectory;

    public async Task<IReadOnlyList<SavedGameInfo>> ListGamesAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.CompletedGamesDirectory);
        var filePaths = Directory
            .EnumerateFiles(
                paths.CompletedGamesDirectory,
                "*.json",
                SearchOption.AllDirectories)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(ActiveFilePath))
        {
            filePaths.Add(ActiveFilePath);
        }

        var games = new List<SavedGameInfo>(filePaths.Count);
        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var document = await ReadDocumentAsync(
                        filePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                games.Add(ToSavedGameInfo(document, filePath));
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException)
            {
                // A damaged or inaccessible game must not hide the rest of the library.
            }
        }

        return games
            .GroupBy(game => game.GameId)
            .Select(group => group.MaxBy(game => game.SavedAtUtc)!)
            .OrderByDescending(game => game.CreatedAtUtc)
            .ThenByDescending(game => game.SavedAtUtc)
            .ThenByDescending(game => game.GameId)
            .ToArray();
    }

    public async Task<GameDocument?> LoadGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        if (gameId == Guid.Empty)
        {
            return null;
        }

        var game = (await ListGamesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.GameId == gameId);
        if (game is null)
        {
            return null;
        }

        try
        {
            return await ReadDocumentAsync(game.FilePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task<string> SaveActiveAsync(
        GameDocument document,
        CancellationToken cancellationToken = default)
    {
        ValidateForWrite(document);
        Directory.CreateDirectory(paths.CompletedGamesDirectory);
        var destination = GetGameFilePath(document);
        await WriteAtomicallyAsync(
            destination,
            document,
            replaceExisting: true,
            cancellationToken).ConfigureAwait(false);
        return destination;
    }

    public async Task<GameDocument?> LoadActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var latestUnfinished = (await ListGamesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(game => !game.IsFinished);
        return latestUnfinished is null
            ? null
            : await LoadGameAsync(latestUnfinished.GameId, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<string> ArchiveCompletedAsync(
        GameDocument document,
        CancellationToken cancellationToken = default)
    {
        ValidateForWrite(document);
        if (document.Snapshot.Stage != MatchStage.Final || document.EndedAtUtc is null)
        {
            throw new InvalidOperationException("Only a completed game can be archived.");
        }

        Directory.CreateDirectory(paths.CompletedGamesDirectory);
        var destination = GetGameFilePath(document);
        await WriteAtomicallyAsync(
            destination,
            document,
            replaceExisting: true,
            cancellationToken).ConfigureAwait(false);
        return destination;
    }

    public async Task<string> ExportAsync(
        GameDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ValidateForWrite(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("An export directory is required.");
        Directory.CreateDirectory(directory);
        await WriteAtomicallyAsync(
            fullPath,
            document,
            replaceExisting: true,
            cancellationToken).ConfigureAwait(false);
        return fullPath;
    }

    public Task DeleteActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // This only removes the legacy single-recovery file. Library games are retained.
        File.Delete(ActiveFilePath);
        return Task.CompletedTask;
    }

    private static async Task<GameDocument> ReadDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<GameDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            throw new InvalidDataException("The game file is empty.");
        }

        ValidateLoaded(document);
        return document;
    }

    private string GetGameFilePath(GameDocument document)
    {
        var localDate = document.CreatedAtUtc.ToLocalTime().ToString(
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
        var gameId = document.GameId.ToString(
            "D",
            System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(
            paths.CompletedGamesDirectory,
            $"{localDate}_{gameId}.json");
    }

    private static SavedGameInfo ToSavedGameInfo(
        GameDocument document,
        string filePath) =>
        new(
            document.GameId,
            document.CreatedAtUtc,
            document.SavedAtUtc,
            document.Snapshot.Stage,
            document.Snapshot.Metadata.TournamentName,
            document.Snapshot.Home.Name,
            document.Snapshot.Home.Score,
            document.Snapshot.Away.Name,
            document.Snapshot.Away.Score,
            filePath);

    private static async Task WriteAtomicallyAsync(
        string destination,
        GameDocument document,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("A destination directory is required.");
        var temporaryFile = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryFile,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (replaceExisting && File.Exists(destination))
            {
                File.Replace(temporaryFile, destination, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryFile, destination);
            }
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    private static void ValidateForWrite(GameDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != GameDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported game schema version {document.SchemaVersion}.");
        }

        if (document.GameId == Guid.Empty ||
            document.Snapshot.GameId != document.GameId ||
            document.Events.Count == 0)
        {
            throw new InvalidDataException("The game document is incomplete.");
        }
    }

    private static void ValidateLoaded(GameDocument document)
    {
        ValidateForWrite(document);

        var ordered = document.Events.OrderBy(matchEvent => matchEvent.Sequence).ToArray();
        if (ordered[0] is not GameCreatedEvent ||
            ordered.Select(matchEvent => matchEvent.EventId).Distinct().Count() != ordered.Length ||
            ordered.Where((matchEvent, index) => matchEvent.Sequence != index + 1L).Any() ||
            document.Snapshot.LastEventSequence != ordered[^1].Sequence)
        {
            throw new InvalidDataException("The active game event stream is invalid.");
        }

        var replayed = MatchReducer.Replay(ordered);
        if (replayed.GameId != document.GameId ||
            replayed.Home.Score != document.Snapshot.Home.Score ||
            replayed.Away.Score != document.Snapshot.Away.Score ||
            replayed.Home.Fouls != document.Snapshot.Home.Fouls ||
            replayed.Away.Fouls != document.Snapshot.Away.Fouls ||
            replayed.Stage != document.Snapshot.Stage ||
            replayed.StartingPossession != document.Snapshot.StartingPossession ||
            replayed.Metadata != document.Snapshot.Metadata)
        {
            throw new InvalidDataException(
                "The active game snapshot does not match its event stream.");
        }

        ValidateClock(document.Snapshot.GameClock, TimeSpan.FromMinutes(100));
        ValidateClock(document.Snapshot.ShotClock, document.Snapshot.Rules.ShotClockDuration);
    }

    private static void ValidateClock(ClockState clock, TimeSpan maximum)
    {
        if (clock.Remaining < TimeSpan.Zero || clock.Remaining > maximum)
        {
            throw new InvalidDataException("A recovered clock value is outside its allowed range.");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
