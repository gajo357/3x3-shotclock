using ThreeByThree.Centar.Scoreboard.Domain.Events;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application;

public sealed record CommandResult
{
    private CommandResult(
        bool isAccepted,
        string message,
        MatchState state,
        IReadOnlyList<MatchEvent> events)
    {
        IsAccepted = isAccepted;
        Message = message;
        State = state;
        Events = events;
    }

    public bool IsAccepted { get; }

    public string Message { get; }

    public MatchState State { get; }

    public IReadOnlyList<MatchEvent> Events { get; }

    public static CommandResult Accept(
        MatchState state,
        IReadOnlyList<MatchEvent> events,
        string message = "") =>
        new(true, message, state, events);

    public static CommandResult Reject(MatchState state, string message) =>
        new(false, message, state, []);
}
