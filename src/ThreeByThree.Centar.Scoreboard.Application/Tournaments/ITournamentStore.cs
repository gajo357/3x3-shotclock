using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Application.Tournaments;

public interface ITournamentStore
{
    string TournamentsDirectory { get; }

    Task<IReadOnlyList<Tournament>> ListAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Tournament tournament,
        CancellationToken cancellationToken = default);

    Task<string> ImportImageAsync(
        Guid tournamentId,
        Guid subjectId,
        string sourcePath,
        CancellationToken cancellationToken = default);
}
