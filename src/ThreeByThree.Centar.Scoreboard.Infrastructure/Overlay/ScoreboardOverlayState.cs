using ThreeByThree.Centar.Scoreboard.Domain;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Application.Presentation;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Overlay;

internal sealed record ScoreboardOverlayState(
    string HomeTeam,
    string AwayTeam,
    int HomeScore,
    int AwayScore,
    int HomeFouls,
    int AwayFouls,
    string HomeFoulColorHex,
    string AwayFoulColorHex,
    string GameClock,
    string ShotClock,
    bool GameClockRunning,
    bool ShotClockRunning)
{
    public static ScoreboardOverlayState FromSnapshot(MatchState snapshot) =>
        new(
            snapshot.Home.Name,
            snapshot.Away.Name,
            snapshot.Home.Score,
            snapshot.Away.Score,
            snapshot.Home.Fouls,
            snapshot.Away.Fouls,
            FoulDisplayColors.GetColorHex(snapshot.HomePenalty),
            FoulDisplayColors.GetColorHex(snapshot.AwayPenalty),
            snapshot.Stage == MatchStage.Overtime
                ? "OT"
                : ClockDisplayFormatter.FormatGameClock(
                    snapshot.GameClock.Remaining,
                    snapshot.Rules.GameClockTenthsThreshold),
            ClockDisplayFormatter.FormatShotClock(
                snapshot.ShotClock.Remaining,
                snapshot.Rules.ShotClockTenthsThreshold),
            snapshot.GameClock.IsRunning,
            snapshot.ShotClock.IsRunning);
}
