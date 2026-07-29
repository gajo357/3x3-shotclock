using System.Windows;
using ThreeByThree.Centar.Scoreboard.Domain.Commands;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class NewGameDialog : Window
{
    private readonly NewGameDialogViewModel viewModel;

    public NewGameDialog(NewGameDialogViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    public CreateGameCommand? Command { get; private set; }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!viewModel.TryBuildCommand(out var command, out var validationMessage))
        {
            MessageBox.Show(
                this,
                validationMessage,
                "New game",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Command = command;
        DialogResult = true;
    }
}
