using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;

namespace ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

public partial class SavedGamesViewModel : ObservableObject
{
    public SavedGamesViewModel(IReadOnlyList<SavedGameInfo> games)
    {
        Games = new ObservableCollection<SavedGameItemViewModel>(
            games.Select(game => new SavedGameItemViewModel(game)));
        SelectedGame = Games.FirstOrDefault();
    }

    public event EventHandler? OpenRequested;

    public ObservableCollection<SavedGameItemViewModel> Games { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private SavedGameItemViewModel? selectedGame;

    private bool CanOpen() => SelectedGame is not null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);
}

public sealed class SavedGameItemViewModel
{
    public SavedGameItemViewModel(SavedGameInfo game)
    {
        Game = game;
    }

    public SavedGameInfo Game { get; }

    public string Date => Game.CreatedAtUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public string MatchId => Game.GameId.ToString("D", CultureInfo.InvariantCulture);

    public string Tournament => string.IsNullOrWhiteSpace(Game.TournamentName)
        ? "3x3 Centar"
        : Game.TournamentName;

    public string Match => $"{Game.HomeName} vs {Game.AwayName}";

    public string Score => $"{Game.HomeScore} – {Game.AwayScore}";

    public string Status => Game.IsFinished ? "FINAL" : "CAN CONTINUE";
}
