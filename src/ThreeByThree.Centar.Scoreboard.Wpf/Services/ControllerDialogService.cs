using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ThreeByThree.Centar.Scoreboard.Application.Display;
using ThreeByThree.Centar.Scoreboard.Application.Operations;
using ThreeByThree.Centar.Scoreboard.Application.Persistence;
using ThreeByThree.Centar.Scoreboard.Application.Settings;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;
using ThreeByThree.Centar.Scoreboard.Wpf.Views;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Services;

public sealed class ControllerDialogService(IAudioService audio) : IControllerDialogService
{
    public CreateGameCommand? ShowNewGame()
    {
        var viewModel = new NewGameDialogViewModel();
        var dialog = new NewGameDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true ? viewModel.BuildCommand() : null;
    }

    public SavedGameInfo? ShowSavedGames(IReadOnlyList<SavedGameInfo> games)
    {
        var viewModel = new SavedGamesViewModel(games);
        var dialog = new SavedGamesWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true
            ? viewModel.SelectedGame?.Game
            : null;
    }

    public TimeSpan? ShowClockEditor(
        ClockKind clock,
        TimeSpan current,
        TimeSpan maximum)
    {
        var viewModel = new ClockEditorViewModel(clock, current, maximum);
        var dialog = new ClockEditorDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true ? viewModel.Value : null;
    }

    public AppSettings? ShowSettings(
        AppSettings current,
        IReadOnlyList<DisplayMonitor> monitors)
    {
        var viewModel = new SettingsViewModel(current, monitors, audio);
        var dialog = new SettingsDialog(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true ? viewModel.BuildSettings() : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowHistory(IReadOnlyList<string> entries)
    {
        var dialog = new GameHistoryWindow(entries)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        dialog.ShowDialog();
    }

    public string? ChooseExportFile(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export current game",
            FileName = suggestedFileName,
            DefaultExt = ".json",
            Filter = "JSON game files (*.json)|*.json|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            _ = Process.Start(
                new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            ShowError("Open folder", exception.Message);
        }
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
