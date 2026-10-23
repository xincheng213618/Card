using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;
namespace CardGame.Wpf.Controls;
public partial class ShanheCodexPanel : UserControl
{
    private MainViewModel? _observedModel;

    public void FocusPicker()
    {
        if (DataContext is MainViewModel { IsShanheCodexLoadoutManagerOpen: true }) { FocusLoadoutManager(); return; }
        (PickerEquipButton.IsEnabled ? PickerEquipButton : PickerCloseButton).Focus();
    }

    public void FocusLoadoutManager() => CodexLoadoutManagerCloseButton.Focus();

    public bool HandleEscape()
    {
        if (DataContext is not MainViewModel model) return false;
        if (model.IsShanheCodexLoadoutManagerOpen)
        {
            if (model.CloseShanheCodexLoadoutManagerCommand.CanExecute(null)) model.CloseShanheCodexLoadoutManagerCommand.Execute(null);
            return true;
        }
        if (!model.IsShanheCodexPickerOpen) return false;
        if (model.CloseShanheCodexPickerCommand.CanExecute(null)) model.CloseShanheCodexPickerCommand.Execute(null);
        return true;
    }

    public ShanheCodexPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveModel();
        Loaded += (_, _) =>
        {
            ObserveModel();
            if (DataContext is MainViewModel { IsShanheCodexLoadoutManagerOpen: true }) QueueManagerFocus();
        };
        Unloaded += (_, _) => StopObservingModel();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) args.Handled = HandleEscape();
        };
        CodexTreeScroll.PreviewMouseWheel += (_, args) =>
        {
            CodexTreeScroll.ScrollToHorizontalOffset(CodexTreeScroll.HorizontalOffset - args.Delta * 1.5);
            args.Handled = true;
        };
    }

    private void ObserveModel()
    {
        if (ReferenceEquals(_observedModel, DataContext)) return;
        StopObservingModel();
        _observedModel = DataContext as MainViewModel;
        if (_observedModel is null) return;
        _observedModel.PropertyChanged += ModelPropertyChanged;
        if (_observedModel.IsShanheCodexLoadoutManagerOpen) QueueManagerFocus();
    }

    private void StopObservingModel()
    {
        if (_observedModel is not null) _observedModel.PropertyChanged -= ModelPropertyChanged;
        _observedModel = null;
    }

    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShanheCodexSelectedLoadoutName))
        {
            if (_observedModel?.IsShanheCodexLoadoutManagerOpen == true) QueueManagerFocus(focusName: true);
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.IsShanheCodexLoadoutManagerOpen) || _observedModel is null) return;
        if (_observedModel.IsShanheCodexLoadoutManagerOpen) QueueManagerFocus();
        else Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && DataContext is MainViewModel { IsShanheCodexLoadoutManagerOpen: false } && CodexManageLoadoutsButton.IsEnabled)
                CodexManageLoadoutsButton.Focus();
        }, DispatcherPriority.DataBind);
    }

    private void QueueManagerFocus(bool focusName = false) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsVisible || DataContext is not MainViewModel { IsShanheCodexLoadoutManagerOpen: true }) return;
        if (focusName) CodexLoadoutNameBox.Focus();
        else FocusLoadoutManager();
    }, DispatcherPriority.DataBind);
}
