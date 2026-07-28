namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public enum TeamSide
{
    Home,
    Away,
}

public enum CoinTossChoice
{
    OpeningPossession,
    OvertimePossession,
}

public enum MatchStage
{
    Setup,
    Regular,
    Overtime,
    Final,
}

public enum MatchStatus
{
    Setup,
    Ready,
    Live,
    Paused,
    Overtime,
    Final,
}

public enum PendingDecision
{
    None,
    ConfirmWinningScore,
    StartOvertime,
    ConfirmFinalScore,
}

public enum PenaltyState
{
    None,
    Penalty,
    DoublePenalty,
}

public enum ClockKind
{
    Game,
    Shot,
}

public enum ClockOperation
{
    Started,
    Paused,
    Adjusted,
    Set,
    Reset,
}

public enum CommandSource
{
    ControllerButton,
    Keyboard,
    ClockScheduler,
    Recovery,
    System,
}

public enum BuzzerKind
{
    Manual,
    GameClock,
    ShotClock,
    ShotClockWarning,
}
