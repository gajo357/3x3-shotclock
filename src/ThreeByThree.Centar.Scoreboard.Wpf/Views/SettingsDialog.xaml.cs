using System.Windows;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
