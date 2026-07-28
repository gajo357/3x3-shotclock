using System.Windows.Threading;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Services;

public sealed class MatchPresentationTicker : IDisposable
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromMilliseconds(40),
    };

    public MatchPresentationTicker()
    {
        timer.Tick += OnTick;
        timer.Start();
    }

    public event EventHandler? Tick;

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e) => Tick?.Invoke(this, EventArgs.Empty);
}
