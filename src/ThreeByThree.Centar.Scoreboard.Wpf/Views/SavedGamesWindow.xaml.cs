using System.Windows;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class SavedGamesWindow : Window
{
    private readonly SavedGamesViewModel viewModel;

    public SavedGamesWindow(SavedGamesViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.OpenRequested += OnOpenRequested;
        Closed += OnClosed;
    }

    private void OnOpenRequested(object? sender, EventArgs e) => DialogResult = true;

    private void OnClosed(object? sender, EventArgs e)
    {
        viewModel.OpenRequested -= OnOpenRequested;
        Closed -= OnClosed;
    }
}
