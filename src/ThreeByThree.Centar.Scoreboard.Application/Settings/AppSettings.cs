namespace ThreeByThree.Centar.Scoreboard.Application.Settings;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool AudioEnabled { get; init; } = true;

    public int VolumePercent { get; init; } = 80;

    public bool ScoreboardTopmost { get; init; } = true;

    public string SelectedMonitorDeviceName { get; init; } = string.Empty;

    public AppSettings Normalize() =>
        this with
        {
            SchemaVersion = CurrentSchemaVersion,
            VolumePercent = Math.Clamp(VolumePercent, 0, 100),
            SelectedMonitorDeviceName = SelectedMonitorDeviceName.Trim(),
        };
}
