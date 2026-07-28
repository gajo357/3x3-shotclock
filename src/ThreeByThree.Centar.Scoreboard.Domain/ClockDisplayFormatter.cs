using System.Globalization;

namespace ThreeByThree.Centar.Scoreboard.Domain;

public static class ClockDisplayFormatter
{
    public static string FormatGameClock(TimeSpan remaining, TimeSpan tenthsThreshold)
    {
        var clamped = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        return clamped < tenthsThreshold
            ? FormatWithTenths(clamped, includeMinutes: true)
            : FormatWholeSeconds(clamped, includeMinutes: true);
    }

    public static string FormatShotClock(TimeSpan remaining, TimeSpan tenthsThreshold)
    {
        var clamped = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        return clamped < tenthsThreshold
            ? FormatWithTenths(clamped, includeMinutes: false)
            : FormatWholeSeconds(clamped, includeMinutes: false);
    }

    private static string FormatWholeSeconds(TimeSpan remaining, bool includeMinutes)
    {
        var totalSeconds = (long)Math.Ceiling(remaining.TotalSeconds);
        if (!includeMinutes)
        {
            return totalSeconds.ToString("00", CultureInfo.InvariantCulture);
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{minutes:00}:{seconds:00}");
    }

    private static string FormatWithTenths(TimeSpan remaining, bool includeMinutes)
    {
        var totalTenths = (long)Math.Ceiling(remaining.TotalMilliseconds / 100);
        var tenths = totalTenths % 10;
        var totalSeconds = totalTenths / 10;
        var seconds = includeMinutes ? totalSeconds % 60 : totalSeconds;

        if (!includeMinutes)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{seconds}.{tenths}");
        }

        var minutes = totalSeconds / 60;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{minutes:00}:{seconds:00}.{tenths}");
    }
}
