using System.Windows;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class GameHistoryWindow : Window
{
    public GameHistoryWindow(IReadOnlyList<string> entries)
    {
        InitializeComponent();
        DataContext = entries;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
