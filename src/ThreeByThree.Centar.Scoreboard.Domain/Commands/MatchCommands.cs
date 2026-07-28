using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Commands;

public abstract record MatchCommand(CommandSource Source);

public sealed record CreateGameCommand(
    MatchMetadata Metadata,
    MatchRules Rules,
    string HomeName,
    string AwayName,
    string HomeColorHex,
    string AwayColorHex,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record AdjustScoreCommand(
    TeamSide Team,
    int Delta,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record AdjustFoulCommand(
    TeamSide Team,
    int Delta,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record ChangeTeamNameCommand(
    TeamSide Team,
    string Name,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record ChangeTeamColorCommand(
    TeamSide Team,
    string ColorHex,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record SwapTeamsCommand(
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record SetLinkedClocksRunningCommand(
    bool IsRunning,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record SetClockRunningCommand(
    ClockKind Clock,
    bool IsRunning,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record AdjustClockCommand(
    ClockKind Clock,
    TimeSpan Delta,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record SetClockCommand(
    ClockKind Clock,
    TimeSpan Remaining,
    bool Stop,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record ResetClockCommand(
    ClockKind Clock,
    bool Stop,
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record ExpireClockCommand(
    ClockKind Clock,
    CommandSource Source = CommandSource.ClockScheduler)
    : MatchCommand(Source);

public sealed record TriggerBuzzerCommand(
    CommandSource Source = CommandSource.ControllerButton,
    BuzzerKind Buzzer = BuzzerKind.Manual)
    : MatchCommand(Source);

public sealed record StartOvertimeCommand(
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record ClearPendingDecisionCommand(
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record EndGameCommand(
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);

public sealed record UndoLastActionCommand(
    CommandSource Source = CommandSource.ControllerButton)
    : MatchCommand(Source);
