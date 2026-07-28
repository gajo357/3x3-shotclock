using CommunityToolkit.Mvvm.ComponentModel;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class ClockEditorViewModel : ObservableObject
{
    private readonly TimeSpan maximum;

    public ClockEditorViewModel(ClockKind clock, TimeSpan current, TimeSpan maximum)
    {
        Clock = clock;
        this.maximum = maximum;
        Minutes = (int)current.TotalMinutes;
        Seconds = current.Seconds;
        Tenths = current.Milliseconds / 100;
    }

    public ClockKind Clock { get; }

    public string Title => Clock == ClockKind.Game ? "Set game clock" : "Set shot clock";

    [ObservableProperty]
    private int minutes;

    [ObservableProperty]
    private int seconds;

    [ObservableProperty]
    private int tenths;

    public TimeSpan Value
    {
        get
        {
            var proposed = TimeSpan.FromMinutes(Math.Max(0, Minutes)) +
                           TimeSpan.FromSeconds(Math.Clamp(Seconds, 0, 59)) +
                           TimeSpan.FromMilliseconds(Math.Clamp(Tenths, 0, 9) * 100);
            return proposed > maximum ? maximum : proposed;
        }
    }
}
