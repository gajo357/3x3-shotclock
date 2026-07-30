using ThreeByThree.Centar.Scoreboard.Application.Presentation;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

internal static class FoulDisplay
{
    public const string DefaultColorHex = FoulDisplayColors.Default;

    public const string PenaltyColorHex = FoulDisplayColors.Penalty;

    public const string DoublePenaltyColorHex = FoulDisplayColors.DoublePenalty;

    public static string GetColorHex(PenaltyState penalty) =>
        FoulDisplayColors.GetColorHex(penalty);
}
