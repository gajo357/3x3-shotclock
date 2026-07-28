using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ThreeByThree.Centar.Scoreboard.Domain.Models;
using ThreeByThree.Centar.Scoreboard.Wpf.ViewModels;

namespace ThreeByThree.Centar.Scoreboard.Wpf.Views;

public partial class ControllerWindow : Window
{
    private readonly ControllerViewModel viewModel;

    public ControllerWindow(ControllerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var control = modifiers.HasFlag(ModifierKeys.Control);
        var handled = true;

        if (control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    viewModel.UndoCommand.Execute(CommandSource.Keyboard);
                    break;
                case Key.N:
                    viewModel.NewGameCommand.Execute(null);
                    break;
                case Key.O:
                    viewModel.OpenSavedGameCommand.Execute(null);
                    break;
                case Key.B:
                    viewModel.ToggleBlackoutCommand.Execute(null);
                    break;
                case Key.E when shift:
                    viewModel.EndGameCommand.Execute(null);
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        else
        {
            handled = ExecuteGameKey(e.Key, shift);
        }

        e.Handled = handled;
    }

    private bool ExecuteGameKey(Key key, bool shift)
    {
        var source = CommandSource.Keyboard;
        switch (key)
        {
            case Key.Space:
                viewModel.ToggleLinkedClocksCommand.Execute(source);
                break;
            case Key.G:
            case Key.C:
                viewModel.ToggleLinkedClocksCommand.Execute(source);
                break;
            case Key.R when shift:
                viewModel.ResetAndPauseClocksCommand.Execute(source);
                break;
            case Key.R:
                viewModel.ResetShotClockCommand.Execute(source);
                break;
            case Key.OemOpenBrackets when shift:
                viewModel.AdjustGameMinusTenCommand.Execute(source);
                break;
            case Key.OemCloseBrackets when shift:
                viewModel.AdjustGamePlusTenCommand.Execute(source);
                break;
            case Key.OemOpenBrackets:
                viewModel.AdjustGameMinusOneCommand.Execute(source);
                break;
            case Key.OemCloseBrackets:
                viewModel.AdjustGamePlusOneCommand.Execute(source);
                break;
            case Key.OemComma when shift:
                viewModel.AdjustShotMinusFiveCommand.Execute(source);
                break;
            case Key.OemPeriod when shift:
                viewModel.AdjustShotPlusFiveCommand.Execute(source);
                break;
            case Key.OemComma:
                viewModel.AdjustShotMinusOneCommand.Execute(source);
                break;
            case Key.OemPeriod:
                viewModel.AdjustShotPlusOneCommand.Execute(source);
                break;
            case Key.B:
                viewModel.ManualBuzzerCommand.Execute(source);
                break;
            case Key.Q:
                viewModel.HomeAddOneCommand.Execute(source);
                break;
            case Key.W:
                viewModel.HomeAddTwoCommand.Execute(source);
                break;
            case Key.A:
                viewModel.HomeSubtractOneCommand.Execute(source);
                break;
            case Key.S:
                viewModel.HomeSubtractTwoCommand.Execute(source);
                break;
            case Key.E:
                viewModel.HomeFoulAddCommand.Execute(source);
                break;
            case Key.D:
                viewModel.HomeFoulSubtractCommand.Execute(source);
                break;
            case Key.O:
                viewModel.AwayAddOneCommand.Execute(source);
                break;
            case Key.P:
                viewModel.AwayAddTwoCommand.Execute(source);
                break;
            case Key.K:
                viewModel.AwaySubtractOneCommand.Execute(source);
                break;
            case Key.L:
                viewModel.AwaySubtractTwoCommand.Execute(source);
                break;
            case Key.I:
                viewModel.AwayFoulAddCommand.Execute(source);
                break;
            case Key.J:
                viewModel.AwayFoulSubtractCommand.Execute(source);
                break;
            case Key.F11:
                viewModel.ToggleScoreboardFullScreenCommand.Execute(null);
                break;
            default:
                return false;
        }

        return true;
    }
}
