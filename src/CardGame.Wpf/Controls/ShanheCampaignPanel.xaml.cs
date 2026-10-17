using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public partial class ShanheCampaignPanel : UserControl
{
    private IInputElement? _focusBeforeSetup;
    private IInputElement? _focusBeforeRules;
    private IInputElement? _focusBeforeReplacement;
    private IInputElement? _focusBeforeCodexPicker;
    private MainViewModel? _observedModel;
    private bool _wasShopActive;

    public ShanheCampaignPanel()
    {
        InitializeComponent();
        ShanheSettlementHost.OpenCodexRequested += (_, _) =>
        {
            if (DataContext is MainViewModel model) model.CloseShanheSettlementCommand.Execute(null);
            ShanheJourneyTabs.SelectedIndex = 1;
            DescendantButtons(ShanheCodexHost).FirstOrDefault(button => button.IsEnabled)?.Focus();
        };
        DataContextChanged += (_, _) => ObserveModel();
        Loaded += (_, _) => ObserveModel();
        Unloaded += (_, _) => StopObservingModel();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            ShanheSetupOverlay.Visibility = Visibility.Collapsed;
            ShanheRulesOverlay.Visibility = Visibility.Collapsed;
            _focusBeforeSetup = _focusBeforeRules = _focusBeforeReplacement = null;
        };
    }

    private void ObserveModel()
    {
        if (ReferenceEquals(_observedModel, DataContext)) return;
        StopObservingModel();
        _observedModel = DataContext as MainViewModel;
        if (_observedModel is null) return;
        _observedModel.PropertyChanged += CampaignPropertyChanged;
        _wasShopActive = _observedModel.IsShanheShopActive;
        if (_wasShopActive) ShanheJourneyTabs.SelectedIndex = 2;
    }

    private void StopObservingModel()
    {
        if (_observedModel is not null) _observedModel.PropertyChanged -= CampaignPropertyChanged;
        _observedModel = null;
    }

    private void CampaignPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_observedModel is null) return;
        if (e.PropertyName == nameof(MainViewModel.IsShanheCodexPickerOpen))
        {
            if (_observedModel.IsShanheCodexPickerOpen)
            {
                _focusBeforeCodexPicker = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(() => ShanheCodexHost.FocusPicker(), System.Windows.Threading.DispatcherPriority.DataBind);
            }
            else
            {
                var previous = _focusBeforeCodexPicker;
                _focusBeforeCodexPicker = null;
                Dispatcher.BeginInvoke(() => RestoreFocus(previous), System.Windows.Threading.DispatcherPriority.DataBind);
            }
            return;
        }
        if (e.PropertyName == nameof(MainViewModel.IsShanheSkillReplacementOpen))
        {
            if (_observedModel.IsShanheSkillReplacementOpen)
            {
                _focusBeforeReplacement = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(() => FocusCampaign(), System.Windows.Threading.DispatcherPriority.DataBind);
            }
            else
            {
                var previous = _focusBeforeReplacement;
                _focusBeforeReplacement = null;
                Dispatcher.BeginInvoke(() => RestoreFocus(previous), System.Windows.Threading.DispatcherPriority.DataBind);
            }
            return;
        }
        if (e.PropertyName == nameof(MainViewModel.IsShanheSettlementOpen) && _observedModel.IsShanheSettlementOpen)
        {
            ShanheSetupOverlay.Visibility = ShanheRulesOverlay.Visibility = Visibility.Collapsed;
            Dispatcher.BeginInvoke(() => ShanheSettlementHost.FocusSettlement(), System.Windows.Threading.DispatcherPriority.DataBind);
            return;
        }
        if (e.PropertyName is nameof(MainViewModel.IsShanheEncounterPreviewOpen) or nameof(MainViewModel.IsShanheRewardOpen)
            or nameof(MainViewModel.IsShanheBossRewardOpen))
        {
            if (_observedModel.IsShanheEncounterPreviewOpen || _observedModel.IsShanheRewardOpen || _observedModel.IsShanheBossRewardOpen)
            {
                ShanheSetupOverlay.Visibility = Visibility.Collapsed;
                ShanheRulesOverlay.Visibility = Visibility.Collapsed;
                ShanheJourneyTabs.SelectedIndex = 0;
                Dispatcher.BeginInvoke(() => FocusCampaign(), System.Windows.Threading.DispatcherPriority.DataBind);
            }
            return;
        }
        if (e.PropertyName == nameof(MainViewModel.IsShanheRunInProgress) && _observedModel.IsShanheRunInProgress)
            ShanheSetupOverlay.Visibility = Visibility.Collapsed;
        if (e.PropertyName != nameof(MainViewModel.IsShanheShopActive)) return;
        var active = _observedModel.IsShanheShopActive;
        if (active && !_wasShopActive) ShanheJourneyTabs.SelectedIndex = 2;
        else if (!active && _wasShopActive && ShanheJourneyTabs.SelectedIndex == 2) ShanheJourneyTabs.SelectedIndex = 0;
        _wasShopActive = active;
    }

    public bool ShowDetachButton
    {
        get => DetachWindowButton.Visibility == Visibility.Visible;
        set => DetachWindowButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void FocusCampaign()
    {
        if (ShanheSettlementHost.Visibility == Visibility.Visible) { ShanheSettlementHost.FocusSettlement(); return; }
        if (ShanheSkillReplacementOverlay.Visibility == Visibility.Visible)
            (DescendantButtons(ShanheSkillReplacementRows).FirstOrDefault(button => button.IsEnabled) ?? ShanheCancelSkillReplacementButton).Focus();
        else if (ShanheBossRewardOverlay.Visibility == Visibility.Visible)
            (DescendantButtons(ShanheBossRewardGroupRows).FirstOrDefault(button => button.IsEnabled) ?? ShanheConfirmBossRewardButton).Focus();
        else if (ShanheRewardOverlay.Visibility == Visibility.Visible)
            (DescendantButtons(ShanheRewardCardRows).FirstOrDefault(button => button.IsEnabled) ?? ShanheConfirmRewardButton).Focus();
        else if (ShanheEncounterOverlay.Visibility == Visibility.Visible) ShanheChallengeButton.Focus();
        else if (ShanheRulesOverlay.Visibility == Visibility.Visible) ShanheRulesCloseButton.Focus();
        else if (ShanheSetupOverlay.Visibility == Visibility.Visible) ShanheGeneralSearchBox.Focus();
        else ShanheCloseButton.Focus();
    }

    public bool HandleEscape()
    {
        if (ShanheCampaignWindow.HasOpenDropDown(this)) return true;
        if (DataContext is MainViewModel { IsShanheCodexPickerOpen: true } picker)
        { picker.CloseShanheCodexPickerCommand.Execute(null); return true; }
        if (DataContext is MainViewModel { IsShanheSettlementOpen: true } settlement)
        { settlement.CloseShanheSettlementCommand.Execute(null); FocusCampaign(); return true; }
        if (ShanheSkillReplacementOverlay.Visibility == Visibility.Visible)
        {
            if (DataContext is MainViewModel replacementModel && replacementModel.CancelShanheSkillReplacementCommand.CanExecute(null))
                replacementModel.CancelShanheSkillReplacementCommand.Execute(null);
            return true;
        }
        if (ShanheBossRewardOverlay.Visibility == Visibility.Visible) return true;
        if (ShanheRewardOverlay.Visibility == Visibility.Visible)
        {
            if (DataContext is MainViewModel { HasShanheBossRewardGroup: true } rewardModel &&
                rewardModel.CloseShanheBossRewardGroupCommand.CanExecute(null))
                rewardModel.CloseShanheBossRewardGroupCommand.Execute(null);
            return true;
        }
        if (ShanheEncounterOverlay.Visibility == Visibility.Visible)
        {
            if (DataContext is MainViewModel model && model.CloseShanheEncounterPreviewCommand.CanExecute(null))
                model.CloseShanheEncounterPreviewCommand.Execute(null);
            return true;
        }
        if (ShanheRulesOverlay.Visibility == Visibility.Visible) { CloseRules(this, new RoutedEventArgs()); return true; }
        if (ShanheSetupOverlay.Visibility == Visibility.Visible) { CloseSetup(this, new RoutedEventArgs()); return true; }
        if (ShanheJourneyTabs.SelectedIndex != 0) { ShanheJourneyTabs.SelectedIndex = 0; return true; }
        return false;
    }

    private void OpenDetachedWindow(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.OpenShanheWindow();

    private void PrepareExpedition(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanPrepareShanheExpedition: true } model) return;
        _focusBeforeSetup = Keyboard.FocusedElement;
        model.PrepareShanheGeneralSelection();
        ShanheSetupOverlay.Visibility = Visibility.Visible;
        ShanheGeneralSearchBox.Focus();
    }

    private void PrepareCodex(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanPrepareShanheExpedition: true }) return;
        ShanheJourneyTabs.SelectedIndex = 1;
        DescendantButtons(ShanheCodexHost).FirstOrDefault(button => button.IsEnabled)?.Focus();
    }

    private void CloseSetup(object sender, RoutedEventArgs e)
    {
        ShanheSetupOverlay.Visibility = Visibility.Collapsed;
        RestoreFocus(_focusBeforeSetup);
        _focusBeforeSetup = null;
    }

    private void ExpeditionStarted(object sender, RoutedEventArgs e)
    {
        // Button executes its bound command after the Click event handlers.
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is not MainViewModel { HasShanheSession: true }) return;
            CloseSetup(sender, e);
            ShanheJourneyTabs.SelectedIndex = 0;
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private void ContinueExpedition(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShanheJourneyTabs.SelectedIndex = _observedModel?.IsShanheShopActive == true ? 2 : 0;
            if (DataContext is not MainViewModel { HasShanheSession: true } model) return;
            if (model.IsShanheSkillReplacementOpen || model.IsShanheRewardOpen || model.IsShanheBossRewardOpen || model.IsShanheEncounterPreviewOpen) { FocusCampaign(); return; }
            var command = model.ShanheChoices.Count > 0 ? model.ChooseShanheOptionCommand : model.PreviewShanheNodeCommand;
            var target = DescendantButtons(this).FirstOrDefault(button => button.IsEnabled && ReferenceEquals(button.Command, command));
            target?.Focus();
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private static IEnumerable<Button> DescendantButtons(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button button) yield return button;
            foreach (var descendant in DescendantButtons(child)) yield return descendant;
        }
    }

    private void ShowRules(object sender, RoutedEventArgs e)
    {
        _focusBeforeRules = Keyboard.FocusedElement;
        ShanheRulesOverlay.Visibility = Visibility.Visible;
        ShanheRulesCloseButton.Focus();
    }

    private void CloseRules(object sender, RoutedEventArgs e)
    {
        ShanheRulesOverlay.Visibility = Visibility.Collapsed;
        RestoreFocus(_focusBeforeRules);
        _focusBeforeRules = null;
    }

    private void RestoreFocus(IInputElement? target)
    {
        if (target is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
        else FocusCampaign();
    }

    private void Campaign_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None &&
            !ShanheCampaignWindow.HasOpenDropDown(this)) e.Handled = HandleEscape();
    }
}
