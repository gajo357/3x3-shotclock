using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
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

    private void OnPublisherLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Windows could not open {e.Uri}.",
                "3x3 Centar Scoreboard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
