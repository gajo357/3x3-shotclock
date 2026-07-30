namespace ThreeByThree.Centar.Scoreboard.Application.Overlay;

public interface ILocalOverlayServer
{
    void Start();

    Task StopAsync(CancellationToken cancellationToken = default);
}
