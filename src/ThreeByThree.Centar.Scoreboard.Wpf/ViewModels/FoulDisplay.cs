using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

internal static class FoulDisplay
{
    public const string DefaultColorHex = "#FFFFFF";

    public const string PenaltyColorHex = "#FF9800";

    public const string DoublePenaltyColorHex = "#FF5252";

    public static string GetColorHex(PenaltyState penalty) => penalty switch
    {
        PenaltyState.Penalty => PenaltyColorHex,
        PenaltyState.DoublePenalty => DoublePenaltyColorHex,
        _ => DefaultColorHex,
    };
}
