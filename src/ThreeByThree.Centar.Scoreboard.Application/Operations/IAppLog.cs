namespace ThreeByThree.Centar.Scoreboard.Application.Operations;

public interface IAppLog
{
    string LogDirectory { get; }

    void Information(string message);

    void LogError(string message, Exception exception);

    Task StopAsync(CancellationToken cancellationToken = default);
}
