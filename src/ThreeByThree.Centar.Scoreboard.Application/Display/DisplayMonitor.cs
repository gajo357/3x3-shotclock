namespace ThreeByThree.Centar.Scoreboard.Application.Display;

public sealed record DisplayMonitor(
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary);
