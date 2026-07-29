using System.Windows;
using Microsoft.Win32;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class TournamentManagerWindow : Window
{
    private readonly TournamentManagerViewModel viewModel;

    public TournamentManagerWindow(TournamentManagerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await viewModel.LoadAsync();

    private void OnChooseTeamImage(object sender, RoutedEventArgs e)
    {
        var path = ChooseImage();
        if (path is not null)
        {
            viewModel.NewTeamImagePath = path;
        }
    }

    private void OnChoosePlayerImage(object sender, RoutedEventArgs e)
    {
        var path = ChooseImage();
        if (path is not null)
        {
            viewModel.NewPlayerImagePath = path;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private string? ChooseImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter =
                "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
}
