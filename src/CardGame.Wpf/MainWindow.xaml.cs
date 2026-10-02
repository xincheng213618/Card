using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Controls;

namespace CardGame.Wpf;

public partial class MainWindow : Window
{
    private readonly GameAudioController _audio;
    private IInputElement? _focusBeforeGuide;
    private IInputElement? _focusBeforeHistory;
    private IInputElement? _focusBeforeGeneralGallery;
    private IInputElement? _focusBeforeSettings;
    private IInputElement? _focusBeforeIdentityReveal;
    public MainWindow() : this(new MainViewModel(useExpandedContent: true, historyStore: new FileMatchHistoryStore(),
        preferencesStore: new FilePlayerPreferencesStore()))
    { }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _audio = new GameAudioController(viewModel, () => new MediaPlayerAudioOutput());
        Activated += (_, _) => _audio.SetActive(true);
        Deactivated += (_, _) => _audio.SetActive(false);
        PreviewMouseDown += TableMouseDown;
    }

    private void Battlefield_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        // Match all three sides to the space available for the side portraits.
        var rows = Math.Max(1, Math.Max(vm.LeftSeats.Count, vm.RightSeats.Count));
        TopSeatRow.MaxHeight = Math.Clamp((e.NewSize.Height - 68) / rows, 90, 210);
    }

    private void TableMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || IsTableModalOpen(vm)) return;
        var source = e.OriginalSource as DependencyObject;
        Button? button = null;
        HandPanel? hand = null;
        var onPlaySurface = false;
        while (source is not null)
        {
            button ??= source as Button;
            hand ??= source as HandPanel;
            onPlaySurface |= ReferenceEquals(source, Battlefield) || ReferenceEquals(source, HandDock);
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (hand is not null) button = hand.CardButtonAt(e.GetPosition(hand));
        if (e.ChangedButton == MouseButton.Right && vm.HasSelection && onPlaySurface)
        {
            vm.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 &&
                 button?.DataContext is { } item)
            e.Handled = TryQuickConfirm(item);
    }

    private static bool IsBlockingTableOverlayOpen(MainViewModel vm) => vm.IsNewGameSetupOpen || vm.IsHelpOpen ||
        vm.IsHistoryOpen || vm.IsGeneralGalleryOpen || vm.IsSettingsOpen || vm.IsIdentityRevealOpen ||
        vm.IsOpeningDealVisible || vm.HasGameOver || vm.IsLogOpen;

    private static bool IsTableModalOpen(MainViewModel vm) =>
        IsBlockingTableOverlayOpen(vm) || vm.IsGeneralSelectionPending;

    private void GeneralCandidateCards_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || DataContext is not MainViewModel vm ||
            !vm.IsGeneralSelectionPending || IsBlockingTableOverlayOpen(vm)) return;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, GeneralCandidateCards))
        {
            if (source is Button button)
            {
                // Confirm only the current prompt's preview. The first click stays reversible;
                // a stale event from a primary general cannot select the secondary slot.
                if (button.IsEnabled && button.Command == vm.PreviewGeneralChoiceCommand &&
                    button.DataContext is GeneralChoiceViewModel choice &&
                    ReferenceEquals(choice, vm.SelectedGeneralChoice) && vm.GeneralChoices.Contains(choice) &&
                    vm.ConfirmGeneralChoiceCommand.CanExecute(null))
                {
                    e.Handled = true;
                    vm.ConfirmGeneralChoiceCommand.Execute(null);
                }
                return;
            }
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
    }

    private bool TryQuickConfirm(object item)
    {
        if (DataContext is not MainViewModel vm || IsTableModalOpen(vm)) return false;
        // The first click selects; the second confirms only an already complete
        // legal action. Incomplete or multi-selection decisions remain explicit.
        if (vm.IsDiscardSelectionPending || vm.IsActiveSkillSelectionPending || vm.IsMultiTargetCardSelected) return false;
        if (item is CardViewModel { IsSelected: true, IsPlayable: true } card && vm.Hand.Contains(card) ||
            item is SeatViewModel { IsSelectedTarget: true, IsLegalTarget: true } seat && vm.Seats.Contains(seat))
        {
            if (vm.CanConfirmSelected) vm.ConfirmSelectedCommand.Execute(null);
            return true;
        }
        return false;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _audio.Dispose();
        (DataContext as MainViewModel)?.Dispose();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.FlushPendingSave()) return;
        viewModel.IsAutoAdvance = false;
        e.Cancel = MessageBox.Show(this, $"{viewModel.SaveStatus}\n\n仍然退出吗？选择“否”可返回牌桌重试保存。",
            "退出前保存失败", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes;
    }

    private void NewGameSetupPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (NewGameSetupPanel.IsVisible)
            {
                if (DataContext is MainViewModel { IsLobbyConfigurationOpen: false })
                {
                    LobbyHome.FocusPrimary();
                    return;
                }
                if (TableModeChoices.ItemContainerGenerator.ContainerFromItem(TableModeChoices.SelectedItem) is UIElement choice)
                    choice.Focus();
                else
                    TableModeChoices.Focus();
            }
            else
            {
                TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        }));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        NewGameSetupPanel_IsVisibleChanged(NewGameSetupPanel, default);

    private void FocusLobby()
    {
        if (DataContext is MainViewModel { IsLobbyConfigurationOpen: true }) TableModeChoices.Focus();
        else LobbyHome.FocusPrimary();
    }

    private void PlayerGuideOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PlayerGuideOverlay.IsVisible) _focusBeforeGuide = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (PlayerGuideOverlay.IsVisible) PlayerGuide.FocusGuide();
            else if (_focusBeforeGuide is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else if (NewGameSetupPanel.IsVisible) FocusLobby();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (DataContext is not MainViewModel viewModel) return false;
        if (viewModel.IsOpeningDealVisible)
        {
            if (key is Key.Enter or Key.Space or Key.Escape)
            {
                viewModel.DismissOpeningDealCommand.Execute(null);
                return true;
            }
            return true;
        }
        if (viewModel.IsIdentityRevealOpen)
        {
            if (key is Key.Enter or Key.Space)
            {
                viewModel.ContinueFromIdentityRevealCommand.Execute(null);
                return true;
            }
            return false;
        }
        if (viewModel.IsGeneralGalleryOpen)
        {
            if (key != Key.Escape) return false;
            if (viewModel.HasGeneralGallerySelection) viewModel.CloseGeneralGalleryDetailsCommand.Execute(null);
            else viewModel.IsGeneralGalleryOpen = false;
            return true;
        }
        if (viewModel.IsSettingsOpen && !(key == Key.M && modifiers == ModifierKeys.Control))
        {
            if (key != Key.Escape) return false;
            viewModel.IsSettingsOpen = false;
            return true;
        }
        if (viewModel.IsHistoryOpen && !(key == Key.M && modifiers == ModifierKeys.Control))
        {
            if (key != Key.Escape) return false;
            viewModel.IsHistoryOpen = false;
            return true;
        }
        if (key == Key.F1 && modifiers == ModifierKeys.None)
        {
            viewModel.ToggleHelpCommand.Execute(null);
            return true;
        }
        if (key == Key.M && modifiers == ModifierKeys.Control)
        {
            viewModel.IsSoundEnabled = !viewModel.IsSoundEnabled;
            return true;
        }
        if (key == Key.Escape)
        {
            if (viewModel.IsHelpOpen) viewModel.IsHelpOpen = false;
            else if (viewModel.IsNewGameSetupOpen && viewModel.IsLobbyConfigurationOpen) viewModel.BackToLobbyCommand.Execute(null);
            else if (viewModel.IsNewGameSetupOpen) viewModel.IsNewGameSetupOpen = false;
            else if (viewModel.IsLogOpen) viewModel.IsLogOpen = false;
            else viewModel.ClearSelectionCommand.Execute(null);
            return true;
        }
        if (viewModel.IsGeneralSelectionPending && !IsBlockingTableOverlayOpen(viewModel) &&
            key == Key.Enter && modifiers == ModifierKeys.None && viewModel.CanConfirmGeneralChoice)
        {
            viewModel.ConfirmGeneralChoiceCommand.Execute(null);
            return true;
        }
        if (viewModel.IsGeneralSelectionPending && !IsBlockingTableOverlayOpen(viewModel) &&
            key is >= Key.D1 and <= Key.D9 && modifiers == ModifierKeys.None)
        {
            var generalIndex = key - Key.D1;
            if (generalIndex < viewModel.GeneralChoices.Count)
            {
                viewModel.PreviewGeneralChoiceCommand.Execute(viewModel.GeneralChoices[generalIndex]);
                return true;
            }
        }
        if (key == Key.F5 && modifiers == ModifierKeys.None && !viewModel.IsNewGameSetupOpen)
        {
            if (viewModel.SaveGameCommand.CanExecute(null))
            {
                viewModel.SaveGameCommand.Execute(null);
                return true;
            }
            return false;
        }
        if (viewModel.IsHelpOpen || viewModel.IsLogOpen || viewModel.IsNewGameSetupOpen || viewModel.IsGeneralGalleryOpen || viewModel.IsSettingsOpen || viewModel.HasGameOver || viewModel.IsGeneralSelectionPending) return false;
        if (key == Key.Enter && modifiers == ModifierKeys.Control && viewModel.CanEndTurn)
        {
            viewModel.EndTurnCommand.Execute(null);
            return true;
        }
        if (key == Key.Enter && modifiers == ModifierKeys.None && viewModel.CanConfirmSelected)
        {
            viewModel.ConfirmSelectedCommand.Execute(null);
            return true;
        }
        if (key is >= Key.D1 and <= Key.D9 && modifiers == ModifierKeys.None)
        {
            var index = key - Key.D1;
            if (index < viewModel.Hand.Count && viewModel.Hand[index].IsPlayable)
            {
                viewModel.SelectCardCommand.Execute(viewModel.Hand[index]);
                return true;
            }
        }
        return false;
    }

    private void HistoryOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (HistoryOverlay.IsVisible) _focusBeforeHistory = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (HistoryOverlay.IsVisible) HistoryPanel.FocusHistory();
            else if (_focusBeforeHistory is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else if (NewGameSetupPanel.IsVisible) FocusLobby();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void GeneralGalleryOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (GeneralGalleryOverlay.IsVisible) _focusBeforeGeneralGallery = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (GeneralGalleryOverlay.IsVisible) GeneralGallery.FocusSearch();
            else if (_focusBeforeGeneralGallery is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else if (NewGameSetupPanel.IsVisible) FocusLobby();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void IdentityRevealOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (IdentityRevealOverlay.IsVisible) _focusBeforeIdentityReveal = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IdentityRevealOverlay.IsVisible) ContinueFromIdentityRevealButton.Focus();
            else if (_focusBeforeIdentityReveal is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void SettingsOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (SettingsOverlay.IsVisible) _focusBeforeSettings = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (SettingsOverlay.IsVisible) SettingsSoundToggle.Focus();
            else if (_focusBeforeSettings is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
            else if (NewGameSetupPanel.IsVisible) FocusLobby();
            else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }
}
