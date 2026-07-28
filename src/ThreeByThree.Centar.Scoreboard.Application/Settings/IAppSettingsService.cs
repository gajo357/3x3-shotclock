namespace ThreeByThree.Centar.Scoreboard.Application.Settings;

public interface IAppSettingsService
{
    event EventHandler? SettingsChanged;

    AppSettings Current { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}
