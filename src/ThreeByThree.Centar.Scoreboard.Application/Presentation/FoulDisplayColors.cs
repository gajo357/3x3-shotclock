using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Presentation;

public static class FoulDisplayColors
{
    public const string Default = "#FFFFFF";

    public const string Penalty = "#FF9800";

    public const string DoublePenalty = "#FF5252";

    public static string GetColorHex(PenaltyState penalty) => penalty switch
    {
        PenaltyState.Penalty => Penalty,
        PenaltyState.DoublePenalty => DoublePenalty,
        _ => Default,
    };
}
