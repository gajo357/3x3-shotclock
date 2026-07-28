using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeByThree.Centar.Scoreboard.Application.Display;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Settings;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAudioService audio;
    private readonly AppSettings original;

    public SettingsViewModel(
        AppSettings settings,
        IReadOnlyList<DisplayMonitor> monitors,
        IAudioService audio)
    {
        original = settings;
        this.audio = audio;
        MonitorChoices.Add(new MonitorChoice("Automatic secondary display", string.Empty));
        foreach (var monitor in monitors)
        {
            var role = monitor.IsPrimary ? "primary" : "secondary";
            MonitorChoices.Add(
                new MonitorChoice(
                    $"{monitor.DeviceName} · {monitor.Width}×{monitor.Height} · {role}",
                    monitor.DeviceName));
        }

        AudioEnabled = settings.AudioEnabled;
        VolumePercent = settings.VolumePercent;
        ScoreboardTopmost = settings.ScoreboardTopmost;
        SelectedMonitor = MonitorChoices.FirstOrDefault(
                              choice =>
                                  choice.DeviceName == settings.SelectedMonitorDeviceName)
            ?? MonitorChoices[0];
    }

    public ObservableCollection<MonitorChoice> MonitorChoices { get; } = [];

    [ObservableProperty]
    private bool audioEnabled;

    [ObservableProperty]
    private int volumePercent;

    [ObservableProperty]
    private bool scoreboardTopmost;

    [ObservableProperty]
    private MonitorChoice? selectedMonitor;

    [RelayCommand]
    private void TestSound() => audio.Test(VolumePercent);

    public AppSettings BuildSettings() =>
        original with
        {
            AudioEnabled = AudioEnabled,
            VolumePercent = Math.Clamp(VolumePercent, 0, 100),
            ScoreboardTopmost = ScoreboardTopmost,
            SelectedMonitorDeviceName = SelectedMonitor?.DeviceName ?? string.Empty,
        };

    public sealed record MonitorChoice(string DisplayName, string DeviceName);
}
