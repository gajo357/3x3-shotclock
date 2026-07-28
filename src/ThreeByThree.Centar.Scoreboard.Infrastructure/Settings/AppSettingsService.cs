using System.Text.Json;
using ThreeByThree.Centar.Scoreboard.Application.Settings;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Settings;

public sealed class AppSettingsService(ISettingsStore store) : IAppSettingsService
{
    public event EventHandler? SettingsChanged;

    public AppSettings Current { get; private set; } = new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Current = await store.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            Current = new AppSettings();
        }
    }

    public async Task UpdateAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        await store.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        Current = normalized;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
