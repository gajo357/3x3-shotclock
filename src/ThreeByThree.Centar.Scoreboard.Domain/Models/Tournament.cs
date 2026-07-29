namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record Tournament
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public IReadOnlyList<TournamentTeam> Teams { get; init; } = [];
}

public sealed record TournamentTeam
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ColorHex { get; init; } = "#FFFFFF";

    public string? ImagePath { get; init; }

    public IReadOnlyList<TournamentPlayer> Players { get; init; } = [];
}

public sealed record TournamentPlayer
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? ImagePath { get; init; }
}
