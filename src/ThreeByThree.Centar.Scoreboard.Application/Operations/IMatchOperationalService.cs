namespace ThreeByThree.Centar.Scoreboard.Application.Operations;

public interface IMatchOperationalService
{
    void Start();

    Task StopAsync(CancellationToken cancellationToken = default);
}
