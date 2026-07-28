using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Operations;

public sealed class MatchOperationalService : IMatchOperationalService
{
    private readonly MatchSession session;
    private readonly IAudioService audio;
    private readonly IPowerManagementService powerManagement;
    private readonly IAppSettingsService settings;
    private readonly IAppLog log;
    private bool isStarted;
    private bool isStopped;

    public MatchOperationalService(
        MatchSession session,
        IAudioService audio,
        IPowerManagementService powerManagement,
        IAppSettingsService settings,
        IAppLog log)
    {
        this.session = session;
        this.audio = audio;
        this.powerManagement = powerManagement;
        this.settings = settings;
        this.log = log;
    }

    public void Start()
    {
        if (isStarted)
        {
            return;
        }

        isStarted = true;
        audio.ApplySettings(settings.Current);
        settings.SettingsChanged += OnSettingsChanged;
        session.EventsCommitted += OnEventsCommitted;
        session.SnapshotChanged += OnSnapshotChanged;
        session.BackgroundError += OnBackgroundError;
        ApplyPowerState(session.Snapshot);
        log.Information("Application operations started.");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!isStarted || isStopped)
        {
            return;
        }

        isStopped = true;
        settings.SettingsChanged -= OnSettingsChanged;
        session.EventsCommitted -= OnEventsCommitted;
        session.SnapshotChanged -= OnSnapshotChanged;
        session.BackgroundError -= OnBackgroundError;
        powerManagement.SetGameActive(false);
        log.Information("Application operations stopped.");
        await log.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        audio.ApplySettings(settings.Current);
        log.Information(
            $"Settings changed: audio={settings.Current.AudioEnabled}, " +
            $"volume={settings.Current.VolumePercent}, " +
            $"monitor={settings.Current.SelectedMonitorDeviceName}.");
    }

    private void OnEventsCommitted(object? sender, MatchEventsCommittedEventArgs e)
    {
        foreach (var matchEvent in e.Events)
        {
            if (matchEvent is BuzzerTriggeredEvent buzzer)
            {
                audio.Play(buzzer.Buzzer);
            }

            log.Information(
                $"Match event #{matchEvent.Sequence}: {matchEvent.GetType().Name} " +
                $"({matchEvent.Metadata.Source}).");
        }
    }

    private void OnSnapshotChanged(object? sender, MatchSnapshotChangedEventArgs e) =>
        ApplyPowerState(e.Snapshot);

    private void OnBackgroundError(object? sender, MatchSessionErrorEventArgs e) =>
        log.LogError("Background match operation failed.", e.Exception);

    private void ApplyPowerState(MatchState snapshot) =>
        powerManagement.SetGameActive(
            snapshot.IsCreated && snapshot.Stage != MatchStage.Final);
}
