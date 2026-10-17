using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class ShanheCampaignWindow : Window
{
    private readonly MainViewModel _viewModel;

    public ShanheCampaignWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        CampaignPanel.ShowDetachButton = false;
        Loaded += (_, _) => FocusCampaign();
    }

    public ShanheCampaignPanel CampaignPanel => CampaignPanelHost;

    public void FocusCampaign()
    {
        if (!IsVisible || !IsEnabled || CampaignPanel.IsKeyboardFocusWithin) return;
        CampaignPanel.FocusCampaign();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Enter && modifiers == ModifierKeys.Control ||
            e.IsRepeat && key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            // Let the open selector consume Escape before dismissing its page.
            if (HasOpenDropDown(CampaignPanel)) return;
            e.Handled = true;
            if (!CampaignPanel.HandleEscape()) Close();
        }
        else if (key == Key.F5 && modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (!e.IsRepeat) _viewModel.FlushShanheProgress();
        }
    }

    internal static bool HasOpenDropDown(DependencyObject root)
    {
        if (root is ComboBox { IsDropDownOpen: true }) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (HasOpenDropDown(VisualTreeHelper.GetChild(root, index))) return true;
        return false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_viewModel.IsShanheOpen && !_viewModel.FlushShanheProgress()) e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Release the owner's reference before changing the shared navigation
        // state, which can synchronously ask the owner to close this window.
        base.OnClosed(e);
        if (_viewModel.IsShanheOpen && _viewModel.CloseShanheCommand.CanExecute(null))
            _viewModel.CloseShanheCommand.Execute(null);
        DataContext = null;
    }
}
