using ThreeByThree.Centar.Scoreboard.Application.Display;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Services;

public interface IControllerDialogService
{
    CreateGameCommand? ShowNewGame();

    SavedGameInfo? ShowSavedGames(IReadOnlyList<SavedGameInfo> games);

    TimeSpan? ShowClockEditor(ClockKind clock, TimeSpan current, TimeSpan maximum);

    AppSettings? ShowSettings(
        AppSettings current,
        IReadOnlyList<DisplayMonitor> monitors);

    bool Confirm(string title, string message);

    void ShowHistory(IReadOnlyList<string> entries);

    string? ChooseExportFile(string suggestedFileName);

    void OpenFolder(string path);

    void ShowError(string title, string message);
}
