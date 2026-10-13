using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public partial class MatchHistoryPanel : UserControl
{
    private int _emptyFocusRequest;

    public MatchHistoryPanel() => InitializeComponent();
    public void FocusHistory()
    {
        if (HistoryList.Items.Count > 0) HistoryList.Focus();
        else HistoryCloseButton.Focus();
    }

    private void HistoryContent_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var request = ++_emptyFocusRequest;
        if (e.NewValue is not false || !IsLoaded || !IsKeyboardFocusWithin ||
            DataContext is not MainViewModel viewModel || viewModel.HasMatchHistory) return;
        // A refresh can retire the focused list item before disabling the empty
        // body. WPF may then focus this container rather than a visible action.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _emptyFocusRequest || !IsLoaded || !IsVisible || !IsEnabled ||
                !ReferenceEquals(DataContext, viewModel) || viewModel.HasMatchHistory ||
                !viewModel.IsHistoryOpen || viewModel.IsSettingsOpen) return;
            // Preserve an action the user already chose, or focus in another panel.
            if (Keyboard.FocusedElement is UIElement focused &&
                (ReferenceEquals(focused, this) || focused.IsDescendantOf(HistoryContent))) HistoryCloseButton.Focus();
        }));
    }
}
