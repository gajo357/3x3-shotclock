using System.Collections.ObjectModel;
using System.Globalization;

namespace ThreeByThree.Centar.Scoreboard.Domain.Models;

public static class GameGroups
{
    public static ReadOnlyCollection<string> All { get; } =
        Array.AsReadOnly(
        [
            .. Enumerable.Range(1, 20)
                .Select(number => number.ToString(CultureInfo.InvariantCulture)),
            .. Enumerable.Range('A', 26)
                .Select(character => ((char)character).ToString()),
        ]);

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}
