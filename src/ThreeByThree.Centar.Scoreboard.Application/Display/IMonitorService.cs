namespace ThreeByThree.Centar.Scoreboard.Application.Display;

public interface IMonitorService
{
    event EventHandler? DisplaysChanged;

    IReadOnlyList<DisplayMonitor> Monitors { get; }

    void Refresh();
}
