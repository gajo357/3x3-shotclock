namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public sealed record TeamState
{
    public string Name { get; init; } = string.Empty;

    public string ColorHex { get; init; } = "#FFFFFF";

    public int Score { get; init; }

    public int Fouls { get; init; }

    public int OvertimePoints { get; init; }
}
