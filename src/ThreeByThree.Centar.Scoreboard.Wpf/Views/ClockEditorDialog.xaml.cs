using System.Windows;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class ClockEditorDialog : Window
{
    public ClockEditorDialog(ClockEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
