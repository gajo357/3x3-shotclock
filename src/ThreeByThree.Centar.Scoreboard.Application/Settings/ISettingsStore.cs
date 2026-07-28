namespace ThreeByThree.Centar.Scoreboard.Application.Settings;

public interface ISettingsStore
{
    string SettingsFilePath { get; }

    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}
