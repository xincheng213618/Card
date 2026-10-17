using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Wpf.ViewModels;
namespace CardGame.Wpf.Controls;
public partial class ShanheCodexPanel : UserControl
{
    public void FocusPicker() => (PickerEquipButton.IsEnabled ? PickerEquipButton : PickerCloseButton).Focus();
    public ShanheCodexPanel()
    {
        InitializeComponent();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || DataContext is not MainViewModel { IsShanheCodexPickerOpen: true } model) return;
            model.CloseShanheCodexPickerCommand.Execute(null); args.Handled = true;
        };
        CodexTreeScroll.PreviewMouseWheel += (_, args) =>
        {
            CodexTreeScroll.ScrollToHorizontalOffset(CodexTreeScroll.HorizontalOffset - args.Delta * 1.5);
            args.Handled = true;
        };
    }
}
