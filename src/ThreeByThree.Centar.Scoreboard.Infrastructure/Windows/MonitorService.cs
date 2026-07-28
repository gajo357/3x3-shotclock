using System.Windows.Forms;
using ThreeByThree.Centar.Scoreboard.Application.Display;

namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Windows;

public sealed class MonitorService : IMonitorService
{
    private IReadOnlyList<DisplayMonitor> monitors = [];

    public MonitorService()
    {
        Refresh();
    }

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<DisplayMonitor> Monitors => monitors;

    public void Refresh()
    {
        var refreshed = Screen.AllScreens
            .Select(screen => new DisplayMonitor(
                screen.DeviceName,
                screen.Bounds.Left,
                screen.Bounds.Top,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Primary))
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Left)
            .ThenBy(monitor => monitor.Top)
            .ToArray();

        if (monitors.SequenceEqual(refreshed))
        {
            return;
        }

        monitors = refreshed;
        DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }
}
