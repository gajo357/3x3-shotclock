using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Application.Display;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.Services;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class ControllerViewModel : ObservableObject, IDisposable
{
    private readonly MatchSession session;
    private readonly MatchPresentationTicker ticker;
    private readonly IControllerDialogService dialogs;
    private readonly IMatchPersistenceService persistence;
    private readonly IAppSettingsService settings;
    private readonly IMonitorService monitors;
    private bool isDisposed;

    public ControllerViewModel(
        MatchSession session,
        MatchPresentationTicker ticker,
        IControllerDialogService dialogs,
        IMatchPersistenceService persistence,
        IAppSettingsService settings,
        IMonitorService monitors)
    {
        this.session = session;
        this.ticker = ticker;
        this.dialogs = dialogs;
        this.persistence = persistence;
        this.settings = settings;
        this.monitors = monitors;
        session.SnapshotChanged += OnSnapshotChanged;
        ticker.Tick += OnPresentationTick;
        persistence.StatusChanged += OnPersistenceStatusChanged;
        settings.SettingsChanged += OnSettingsChanged;
        ApplySnapshot(session.Snapshot);
        ApplyPersistenceStatus();
        ApplySettings();
    }

    public event EventHandler? ToggleScoreboardFullScreenRequested;

    public event EventHandler? ToggleBlackoutRequested;

    public ObservableCollection<string> RecentEvents { get; } = [];

    public string ApplicationVersion { get; } =
        $"v{Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}";

    [ObservableProperty]
    private string status = "SETUP";

    [ObservableProperty]
    private string statusMessage = "Ready.";

    [ObservableProperty]
    private string saveStatus = "Not saved";

    [ObservableProperty]
    private string currentFile = string.Empty;

    [ObservableProperty]
    private string audioStatus = "AUDIO 80%";

    [ObservableProperty]
    private string tournamentName = string.Empty;

    [ObservableProperty]
    private string homeName = string.Empty;

    [ObservableProperty]
    private string awayName = string.Empty;

    [ObservableProperty]
    private string homeColorHex = "#FFFFFF";

    [ObservableProperty]
    private string awayColorHex = "#FF5252";

    [ObservableProperty]
    private string homeScore = "0";

    [ObservableProperty]
    private string awayScore = "0";

    [ObservableProperty]
    private string homeFouls = "0";

    [ObservableProperty]
    private string awayFouls = "0";

    [ObservableProperty]
    private string homeFoulColorHex = FoulDisplay.DefaultColorHex;

    [ObservableProperty]
    private string awayFoulColorHex = FoulDisplay.DefaultColorHex;

    [ObservableProperty]
    private string gameClock = "10:00";

    [ObservableProperty]
    private string shotClock = "12";

    [ObservableProperty]
    private string shotClockBackgroundHex = "#FF5252";

    [ObservableProperty]
    private string linkedClockButton = "START CLOCKS";

    [ObservableProperty]
    private string possession = string.Empty;

    [ObservableProperty]
    private string pendingAlert = string.Empty;

    [ObservableProperty]
    private Visibility pendingAlertVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private bool canStartOvertime;

    [RelayCommand]
    private void HomeAddOne(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Home, 1, source));

    [RelayCommand]
    private void HomeAddTwo(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Home, 2, source));

    [RelayCommand]
    private void HomeSubtractOne(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Home, -1, source));

    [RelayCommand]
    private void HomeSubtractTwo(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Home, -2, source));

    [RelayCommand]
    private void AwayAddOne(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Away, 1, source));

    [RelayCommand]
    private void AwayAddTwo(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Away, 2, source));

    [RelayCommand]
    private void AwaySubtractOne(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Away, -1, source));

    [RelayCommand]
    private void AwaySubtractTwo(CommandSource source) =>
        Execute(new AdjustScoreCommand(TeamSide.Away, -2, source));

    [RelayCommand]
    private void HomeFoulAdd(CommandSource source) =>
        Execute(new AdjustFoulCommand(TeamSide.Home, 1, source));

    [RelayCommand]
    private void HomeFoulSubtract(CommandSource source) =>
        Execute(new AdjustFoulCommand(TeamSide.Home, -1, source));

    [RelayCommand]
    private void AwayFoulAdd(CommandSource source) =>
        Execute(new AdjustFoulCommand(TeamSide.Away, 1, source));

    [RelayCommand]
    private void AwayFoulSubtract(CommandSource source) =>
        Execute(new AdjustFoulCommand(TeamSide.Away, -1, source));

    [RelayCommand]
    private void ToggleLinkedClocks(CommandSource source)
    {
        var snapshot = session.Snapshot;
        var shouldRun = !snapshot.GameClock.IsRunning && !snapshot.ShotClock.IsRunning;
        Execute(new SetLinkedClocksRunningCommand(shouldRun, source));
    }

    [RelayCommand]
    private void ResetShotClock(CommandSource source) =>
        Execute(new ResetClockCommand(ClockKind.Shot, Stop: false, Source: source));

    [RelayCommand]
    private void ResetAndPauseClocks(CommandSource source) =>
        Execute(new ResetClockCommand(ClockKind.Shot, Stop: true, Source: source));

    [RelayCommand]
    private void AdjustGameMinusOne(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Game, TimeSpan.FromSeconds(-1), source));

    [RelayCommand]
    private void AdjustGamePlusOne(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Game, TimeSpan.FromSeconds(1), source));

    [RelayCommand]
    private void AdjustGameMinusTen(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Game, TimeSpan.FromSeconds(-10), source));

    [RelayCommand]
    private void AdjustGamePlusTen(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Game, TimeSpan.FromSeconds(10), source));

    [RelayCommand]
    private void AdjustGameMinusMinute(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Game, TimeSpan.FromMinutes(-1), source));

    [RelayCommand]
    private void AdjustGamePlusMinute(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Game, TimeSpan.FromMinutes(1), source));

    [RelayCommand]
    private void AdjustShotMinusOne(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(-1), source));

    [RelayCommand]
    private void AdjustShotPlusOne(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(1), source));

    [RelayCommand]
    private void AdjustShotMinusFive(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(-5), source));

    [RelayCommand]
    private void AdjustShotPlusFive(CommandSource source) =>
        Execute(new AdjustClockCommand(ClockKind.Shot, TimeSpan.FromSeconds(5), source));

    [RelayCommand]
    private void ManualBuzzer(CommandSource source) =>
        Execute(new TriggerBuzzerCommand(source));

    [RelayCommand]
    private void Undo(CommandSource source) =>
        Execute(new UndoLastActionCommand(source));

    [RelayCommand]
    private void ApplyTeams()
    {
        Execute(new ChangeTeamNameCommand(TeamSide.Home, HomeName));
        Execute(new ChangeTeamColorCommand(TeamSide.Home, HomeColorHex));
        Execute(new ChangeTeamNameCommand(TeamSide.Away, AwayName));
        Execute(new ChangeTeamColorCommand(TeamSide.Away, AwayColorHex));
    }

    [RelayCommand]
    private void SwapTeams() => Execute(new SwapTeamsCommand());

    [RelayCommand]
    private void NewGame()
    {
        var command = dialogs.ShowNewGame();
        if (command is null)
        {
            return;
        }

        Execute(command);
    }

    [RelayCommand]
    private async Task OpenSavedGame()
    {
        var current = session.Snapshot;
        if (current.GameClock.IsRunning || current.ShotClock.IsRunning)
        {
            Execute(new SetLinkedClocksRunningCommand(false));
            current = session.Snapshot;
        }

        try
        {
            var games = await persistence.ListGamesAsync();
            if (games.Count == 0)
            {
                dialogs.ShowError("Saved games", "No saved games were found.");
                return;
            }

            var selected = dialogs.ShowSavedGames(games);
            if (selected is null)
            {
                return;
            }

            if (current.IsCreated &&
                current.GameId != selected.GameId &&
                current.Stage != MatchStage.Final &&
                !dialogs.Confirm(
                    "Open saved game",
                    "The current game is saved and will remain available. " +
                    "Open the selected game instead?"))
            {
                return;
            }

            var result = await persistence.OpenGameAsync(selected.GameId);
            StatusMessage = result.Message;
            ApplySnapshot(result.State);
            if (!result.IsAccepted)
            {
                dialogs.ShowError("Open saved game", result.Message);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            dialogs.ShowError("Open saved game", exception.Message);
        }
    }

    [RelayCommand]
    private void EndGame()
    {
        Execute(new SetLinkedClocksRunningCommand(false));
        if (dialogs.Confirm(
                "End game",
                "Finalize this game? The final score will remain on the public display."))
        {
            Execute(new EndGameCommand());
        }
    }

    [RelayCommand]
    private void SetGameClock()
    {
        var snapshot = session.Snapshot;
        var value = dialogs.ShowClockEditor(
            ClockKind.Game,
            snapshot.GameClock.Remaining,
            TimeSpan.FromMinutes(100) - TimeSpan.FromMilliseconds(100));
        if (value.HasValue)
        {
            Execute(new SetClockCommand(ClockKind.Game, value.Value, Stop: false));
        }
    }

    [RelayCommand]
    private void SetShotClock()
    {
        var snapshot = session.Snapshot;
        var value = dialogs.ShowClockEditor(
            ClockKind.Shot,
            snapshot.ShotClock.Remaining,
            snapshot.Rules.ShotClockDuration);
        if (value.HasValue)
        {
            Execute(new SetClockCommand(ClockKind.Shot, value.Value, Stop: false));
        }
    }

    [RelayCommand]
    private void StartOvertime() => Execute(new StartOvertimeCommand());

    [RelayCommand]
    private void ClearAlert() => Execute(new ClearPendingDecisionCommand());

    [RelayCommand]
    private void ToggleScoreboardFullScreen() =>
        ToggleScoreboardFullScreenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleBlackout() =>
        ToggleBlackoutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task OpenSettings()
    {
        monitors.Refresh();
        var updated = dialogs.ShowSettings(settings.Current, monitors.Monitors);
        if (updated is null)
        {
            return;
        }

        try
        {
            await settings.UpdateAsync(updated);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Settings", $"Settings could not be saved.\n\n{exception.Message}");
        }
    }

    [RelayCommand]
    private void ShowHistory() =>
        dialogs.ShowHistory(
            session.History
                .Reverse()
                .Select(DescribeEvent)
                .ToArray());

    [RelayCommand]
    private async Task ExportGame()
    {
        var snapshot = session.Snapshot;
        if (!snapshot.IsCreated)
        {
            dialogs.ShowError("Export game", "Create or recover a game first.");
            return;
        }

        var suggestedName =
            $"{snapshot.Home.Name}_vs_{snapshot.Away.Name}_{snapshot.GameId:N}.json";
        var destination = dialogs.ChooseExportFile(suggestedName);
        if (destination is null)
        {
            return;
        }

        try
        {
            await persistence.ExportCurrentAsync(destination);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            dialogs.ShowError("Export game", exception.Message);
        }
    }

    [RelayCommand]
    private void OpenGamesFolder() =>
        dialogs.OpenFolder(persistence.CompletedGamesDirectory);

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        session.SnapshotChanged -= OnSnapshotChanged;
        ticker.Tick -= OnPresentationTick;
        persistence.StatusChanged -= OnPersistenceStatusChanged;
        settings.SettingsChanged -= OnSettingsChanged;
        GC.SuppressFinalize(this);
    }

    private void Execute(MatchCommand command)
    {
        var result = session.Execute(command);
        StatusMessage = result.IsAccepted
            ? DescribeResult(result)
            : result.Message;
        ApplySnapshot(result.State);
    }

    private void OnPresentationTick(object? sender, EventArgs e) =>
        ApplySnapshot(session.Snapshot, updateEditableTeams: false);

    private void OnSnapshotChanged(object? sender, MatchSnapshotChangedEventArgs e)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            ApplySnapshot(e.Snapshot);
            return;
        }

        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(
            () => ApplySnapshot(e.Snapshot));
    }

    private void OnPersistenceStatusChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            ApplyPersistenceStatus();
            return;
        }

        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(ApplyPersistenceStatus);
    }

    private void ApplyPersistenceStatus()
    {
        var persistenceStatus = persistence.Status;
        SaveStatus = persistenceStatus.Message;
        CurrentFile = persistenceStatus.CurrentFile is null
            ? string.Empty
            : Path.GetFileName(persistenceStatus.CurrentFile);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            ApplySettings();
            return;
        }

        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(ApplySettings);
    }

    private void ApplySettings()
    {
        AudioStatus = settings.Current.AudioEnabled
            ? $"AUDIO {settings.Current.VolumePercent}%"
            : "AUDIO MUTED";
    }

    private void ApplySnapshot(MatchState snapshot, bool updateEditableTeams = true)
    {
        Status = snapshot.Status.ToString().ToUpperInvariant();
        TournamentName = snapshot.Metadata.TournamentName;
        HomeScore = snapshot.Home.Score.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AwayScore = snapshot.Away.Score.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HomeFouls = snapshot.Home.Fouls.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AwayFouls = snapshot.Away.Fouls.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HomeFoulColorHex = FoulDisplay.GetColorHex(snapshot.HomePenalty);
        AwayFoulColorHex = FoulDisplay.GetColorHex(snapshot.AwayPenalty);
        GameClock = snapshot.Stage == MatchStage.Overtime
            ? "OT"
            : ClockDisplayFormatter.FormatGameClock(
                snapshot.GameClock.Remaining,
                snapshot.Rules.GameClockTenthsThreshold);
        ShotClock = ClockDisplayFormatter.FormatShotClock(
            snapshot.ShotClock.Remaining,
            snapshot.Rules.ShotClockTenthsThreshold);
        ShotClockBackgroundHex = GetShotClockBackgroundHex(snapshot);
        LinkedClockButton =
            snapshot.GameClock.IsRunning || snapshot.ShotClock.IsRunning
                ? "PAUSE CLOCKS"
                : "START CLOCKS";
        Possession = FormatPossession(snapshot);
        CanStartOvertime = snapshot.PendingDecision == PendingDecision.StartOvertime;
        PendingAlert = FormatDecision(snapshot.PendingDecision);
        PendingAlertVisibility = snapshot.PendingDecision == PendingDecision.None
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (updateEditableTeams)
        {
            HomeName = snapshot.Home.Name;
            AwayName = snapshot.Away.Name;
            HomeColorHex = snapshot.Home.ColorHex;
            AwayColorHex = snapshot.Away.ColorHex;
            RefreshHistory();
        }
    }

    private void RefreshHistory()
    {
        RecentEvents.Clear();
        foreach (var matchEvent in session.History.Reverse().Take(20))
        {
            RecentEvents.Add(DescribeEvent(matchEvent));
        }
    }

    private static string DescribeResult(CommandResult result) =>
        result.Events.Count == 0
            ? "Ready."
            : DescribeEvent(result.Events[^1]);

    private static string DescribeEvent(MatchEvent matchEvent)
    {
        var time = matchEvent.Metadata.RecordedAtUtc
            .ToLocalTime()
            .ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
        var description = matchEvent switch
        {
            GameCreatedEvent created =>
                $"{created.HomeName} vs {created.AwayName}",
            ScoreAdjustedEvent score =>
                $"{score.Team} score {score.Before} → {score.After}",
            FoulAdjustedEvent foul =>
                $"{foul.Team} fouls {foul.Before} → {foul.After}",
            TeamNameChangedEvent name =>
                $"{name.Team} renamed to {name.After}",
            TeamColorChangedEvent color =>
                $"{color.Team} color {color.After}",
            TeamsSwappedEvent => "Teams swapped",
            ClockChangedEvent clock =>
                $"{clock.Clock} clock {clock.Operation.ToString().ToLowerInvariant()}",
            ClockExpiredEvent clock =>
                $"{clock.Clock} clock expired",
            BuzzerTriggeredEvent buzzer =>
                $"{buzzer.Buzzer} buzzer",
            OvertimeStartedEvent => "Overtime started",
            PendingDecisionClearedEvent => "Alert cleared",
            EventRevertedEvent => "Last action reverted",
            GameEndedEvent ended =>
                $"Game ended {ended.HomeScore}–{ended.AwayScore}",
            _ => matchEvent.GetType().Name,
        };

        return $"{time}  {description}";
    }

    private static string FormatPossession(MatchState snapshot)
    {
        if (!snapshot.IsCreated)
        {
            return string.Empty;
        }

        var teamName = snapshot.GetTeam(snapshot.StartingPossession).Name;
        return snapshot.Stage == MatchStage.Overtime
            ? $"OVERTIME BALL · {teamName}"
            : $"OPENING BALL · {teamName}";
    }

    private static string GetShotClockBackgroundHex(MatchState snapshot)
    {
        var isWarning =
            snapshot.ShotClock.Remaining > TimeSpan.Zero &&
            snapshot.ShotClock.Remaining < snapshot.Rules.ShotClockTenthsThreshold;
        if (!isWarning && !snapshot.ShotClock.HasExpired)
        {
            return "#FF5252";
        }

        return (Environment.TickCount64 / 250) % 2 == 0
            ? "#FFFFFF"
            : "#FF5252";
    }

    private static string FormatDecision(PendingDecision decision) => decision switch
    {
        PendingDecision.ConfirmWinningScore => "WINNING SCORE — confirm or correct the score",
        PendingDecision.StartOvertime => "REGULATION TIED — start overtime",
        PendingDecision.ConfirmFinalScore => "TIME EXPIRED — confirm final score",
        _ => string.Empty,
    };
}
