using System.Text.Json;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Settings;

public sealed class JsonSettingsStore(GameStoragePaths gameStoragePaths) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string SettingsFilePath => Path.Combine(
        Directory.GetParent(gameStoragePaths.ActiveDirectory)?.FullName
            ?? gameStoragePaths.ActiveDirectory,
        "settings.json");

    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            SettingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (settings is not null &&
            settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported settings schema version {settings.SchemaVersion}.");
        }

        return settings?.Normalize();
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        var directory = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("A settings directory is required.");
        Directory.CreateDirectory(directory);
        var temporaryFile = Path.Combine(directory, $".settings.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryFile,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(temporaryFile, SettingsFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryFile, SettingsFilePath);
            }
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }
}
