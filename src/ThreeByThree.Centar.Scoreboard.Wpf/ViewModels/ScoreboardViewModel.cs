using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ThreeByThree.Centar.Scoreboard.Application;
using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.Services;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class ScoreboardViewModel : ObservableObject, IDisposable
{
    private readonly MatchSession session;
    private readonly MatchPresentationTicker ticker;
    private bool isDisposed;

    public ScoreboardViewModel(MatchSession session, MatchPresentationTicker ticker)
    {
        this.session = session;
        this.ticker = ticker;
        session.SnapshotChanged += OnSnapshotChanged;
        ticker.Tick += OnPresentationTick;
        ApplySnapshot(session.Snapshot);
    }

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private string homeName = "HOME";

    [ObservableProperty]
    private string awayName = "AWAY";

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
    private string statusBanner = "READY FOR NEXT GAME";

    [ObservableProperty]
    private Visibility statusBannerVisibility = Visibility.Visible;

    [ObservableProperty]
    private string decisionMessage = string.Empty;

    [ObservableProperty]
    private Visibility decisionVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility blackoutVisibility = Visibility.Collapsed;

    public void ToggleBlackout() =>
        BlackoutVisibility = BlackoutVisibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        session.SnapshotChanged -= OnSnapshotChanged;
        ticker.Tick -= OnPresentationTick;
        GC.SuppressFinalize(this);
    }

    private void OnPresentationTick(object? sender, EventArgs e) =>
        ApplySnapshot(session.Snapshot);

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

    private void ApplySnapshot(MatchState snapshot)
    {
        Category = snapshot.Metadata.GetGameTypeLabel();
        HomeName = snapshot.Home.Name.ToUpperInvariant();
        AwayName = snapshot.Away.Name.ToUpperInvariant();
        HomeScore = snapshot.Home.Score.ToString(CultureInfo.InvariantCulture);
        AwayScore = snapshot.Away.Score.ToString(CultureInfo.InvariantCulture);
        HomeFouls = snapshot.Home.Fouls.ToString(CultureInfo.InvariantCulture);
        AwayFouls = snapshot.Away.Fouls.ToString(CultureInfo.InvariantCulture);
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
        ShotClockBackgroundHex = snapshot.ShotClock.HasExpired &&
            (Environment.TickCount64 / 250) % 2 == 0
                ? "#FFFFFF"
                : "#FF5252";
        StatusBanner = FormatStatus(snapshot);
        StatusBannerVisibility = snapshot.Status == MatchStatus.Live
            ? Visibility.Collapsed
            : Visibility.Visible;
        DecisionMessage = FormatDecision(snapshot.PendingDecision);
        DecisionVisibility = snapshot.PendingDecision == PendingDecision.None
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string FormatStatus(MatchState snapshot) => snapshot.Status switch
    {
        MatchStatus.Setup => "READY FOR NEXT GAME",
        MatchStatus.Ready => "READY",
        MatchStatus.Paused => "PAUSED",
        MatchStatus.Overtime => snapshot.ShotClock.IsRunning ? "OVERTIME" : "OVERTIME · PAUSED",
        MatchStatus.Final => "FINAL",
        _ => string.Empty,
    };

    private static string FormatDecision(PendingDecision decision) => decision switch
    {
        PendingDecision.ConfirmWinningScore => "WINNING SCORE",
        PendingDecision.StartOvertime => "REGULATION TIED · OVERTIME",
        PendingDecision.ConfirmFinalScore => "TIME EXPIRED",
        _ => string.Empty,
    };
}
