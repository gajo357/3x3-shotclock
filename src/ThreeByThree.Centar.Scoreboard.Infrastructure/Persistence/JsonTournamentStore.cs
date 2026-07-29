using System.Text.Json;
using System.Text.Json.Serialization;
using ThreeByThree.Centar.Scoreboard.Application.Tournaments;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

public sealed class JsonTournamentStore : ITournamentStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions =
        CreateSerializerOptions();
    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".gif",
            ".jpeg",
            ".jpg",
            ".png",
        };
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonTournamentStore(GameStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        TournamentsDirectory = paths.TournamentsDirectory ??
            Path.Combine(
                Path.GetDirectoryName(paths.ActiveDirectory) ??
                    paths.ActiveDirectory,
                "Tournaments");
    }

    public string TournamentsDirectory { get; }

    public void Dispose()
    {
        gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<IReadOnlyList<Tournament>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(TournamentsDirectory))
            {
                return [];
            }

            var tournaments = new List<Tournament>();
            foreach (var filePath in Directory.EnumerateFiles(
                         TournamentsDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var tournament = await JsonSerializer.DeserializeAsync<Tournament>(
                        stream,
                        SerializerOptions,
                        cancellationToken);
                    if (tournament is not null)
                    {
                        Validate(tournament);
                        tournaments.Add(tournament);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or
                    JsonException or
                    InvalidDataException)
                {
                    // One damaged tournament must not hide the rest of the catalog.
                }
            }

            return tournaments
                .OrderByDescending(tournament => tournament.CreatedAtUtc)
                .ThenBy(tournament => tournament.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        Tournament tournament,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tournament);
        Validate(tournament);

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(TournamentsDirectory);
            var destinationPath = GetTournamentPath(tournament.Id);
            var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 4096,
                                 FileOptions.Asynchronous |
                                 FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        tournament,
                        SerializerOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> ImportImageAsync(
        Guid tournamentId,
        Guid subjectId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (tournamentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A tournament must be selected before importing an image.",
                nameof(tournamentId));
        }

        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "The team or player must have an ID before importing an image.",
                nameof(subjectId));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected image was not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedImageExtensions.Contains(extension))
        {
            throw new InvalidDataException(
                "Team and player images must be BMP, GIF, JPEG, or PNG files.");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var assetDirectory = Path.Combine(
                TournamentsDirectory,
                "assets",
                tournamentId.ToString("N"));
            Directory.CreateDirectory(assetDirectory);
            var destinationPath = Path.Combine(
                assetDirectory,
                subjectId.ToString("N") + extension.ToLowerInvariant());
            var temporaryPath =
                destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using var source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using (var destination = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return destinationPath;
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetTournamentPath(Guid tournamentId) =>
        Path.Combine(TournamentsDirectory, tournamentId.ToString("D") + ".json");

    private static void Validate(Tournament tournament)
    {
        if (tournament.Id == Guid.Empty)
        {
            throw new InvalidDataException("A tournament ID is required.");
        }

        ValidateName(tournament.Name, 80, "Tournament");
        if (tournament.CreatedAtUtc == default)
        {
            throw new InvalidDataException("A tournament creation time is required.");
        }

        if (tournament.Teams is null)
        {
            throw new InvalidDataException("Tournament teams cannot be null.");
        }

        if (tournament.Teams.Select(team => team.Id).Distinct().Count() !=
            tournament.Teams.Count)
        {
            throw new InvalidDataException("Team IDs must be unique within a tournament.");
        }

        foreach (var team in tournament.Teams)
        {
            if (team.Id == Guid.Empty)
            {
                throw new InvalidDataException("Every team must have an ID.");
            }

            ValidateName(team.Name, 32, "Team");
            ValidateColor(team.ColorHex);
            if (team.Players is null)
            {
                throw new InvalidDataException("Team players cannot be null.");
            }

            if (team.Players.Select(player => player.Id).Distinct().Count() !=
                team.Players.Count)
            {
                throw new InvalidDataException("Player IDs must be unique within a team.");
            }

            foreach (var player in team.Players)
            {
                if (player.Id == Guid.Empty)
                {
                    throw new InvalidDataException("Every player must have an ID.");
                }

                ValidateName(player.Name, 64, "Player");
            }
        }
    }

    private static void ValidateName(string value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw new InvalidDataException(
                $"{label} names must contain between 1 and {maximumLength} characters.");
        }
    }

    private static void ValidateColor(string colorHex)
    {
        if (colorHex.Length != 7 ||
            colorHex[0] != '#' ||
            colorHex.Skip(1).Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "Team colors must use six-digit hexadecimal notation, for example #FF5252.");
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
