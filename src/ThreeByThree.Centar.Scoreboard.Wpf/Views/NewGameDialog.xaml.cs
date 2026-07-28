using System.Windows;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class NewGameDialog : Window
{
    public NewGameDialog(NewGameDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCreate(object sender, RoutedEventArgs e) => DialogResult = true;
}
