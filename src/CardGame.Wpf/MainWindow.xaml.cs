using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Controls;

namespace CardGame.Wpf;

public partial class MainWindow : Window
{
    private void OpenLanRooms(object sender, RoutedEventArgs e) =>
        new LanRoomWindow(DataContext as MainViewModel) { Owner = this }.Show();
    private readonly GameAudioController _audio;
    private IInputElement? _focusBeforeGuide;
    private IInputElement? _focusBeforeHistory;
    private IInputElement? _focusBeforeGeneralGallery;
    private IInputElement? _focusBeforeSettings;
    private IInputElement? _focusBeforeIdentityReveal;
    private IInputElement? _focusBeforeLobby;
    private int _overlayFocusRequest;
    private double _tableExpansion = double.NaN;
    private double _topSeatHeight = 210;
    private TargetCardFocus? _targetCardFocus;
    private sealed record TargetCardFocus(PendingDecision Prompt, ChoiceId ChoiceId);
    public MainWindow() : this(new MainViewModel(useExpandedContent: true, historyStore: new FileMatchHistoryStore(),
        preferencesStore: new FilePlayerPreferencesStore(), confirmOpeningHand: true))
    { }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        UpdateTableScale(new Size(1120, 812));
        DataContext = viewModel;
        _audio = new GameAudioController(viewModel, () => new MediaPlayerAudioOutput(), feedbackLayer: BattleFeedback);
        Activated += (_, _) => _audio.SetActive(true);
        Deactivated += (_, _) => _audio.SetActive(false);
        PreviewMouseDown += TableMouseDown;
        InitializeCardAim();
        InitializeGeneralPreviewScroll();
        InitializeShanheWindowHost(viewModel);
    }

    private void GameArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTableScale(e.NewSize);

    private void UpdateTableScale(Size available)
    {
        // Grow artwork only when both axes have room. The compact 1440 x 860
        // layout has no spare height: enlarging its hand would shrink the seats.
        var expansion = Math.Clamp(Math.Min((available.Width - 1120) / 800,
            (available.Height - 812) / 220), 0, 1);
        if (Math.Abs(expansion - _tableExpansion) < .0001) return;
        _tableExpansion = expansion;
        _topSeatHeight = 210 + 66 * expansion;
        Resources["RingSeatMaxWidth"] = 164 + 26 * expansion;
        Resources["TableTopSeatHeight"] = new GridLength(_topSeatHeight);
        LeftSeatColumn.Width = RightSeatColumn.Width = new GridLength(164 + 26 * expansion);
        HandDockRow.Height = new GridLength(210 + 46 * expansion);
        EquipmentRailColumn.Width = new GridLength(154 + 36 * expansion);
        Resources["EquipmentControlScale"] = 1 + .25 * expansion;
        Resources["SkillControlScale"] = 1 + .2 * expansion;
        HumanPortraitColumn.Width = new GridLength(142 + 36 * expansion);
        HandPanel.SetCardScale(GameArea, 1 + .25 * expansion);
        UpdateTopSeatLimit(Battlefield.ActualHeight);
    }

    private void Battlefield_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_cardAim is { Started: true }) CancelCardAim();
        UpdateTopSeatLimit(e.NewSize.Height);
    }

    private void UpdateTopSeatLimit(double battlefieldHeight)
    {
        if (DataContext is not MainViewModel vm) return;
        // Match all three sides to the space available for the side portraits.
        var rows = Math.Max(1, Math.Max(vm.LeftSeats.Count, vm.RightSeats.Count));
        TopSeatRow.MaxHeight = Math.Clamp((battlefieldHeight - 68) / rows, 90, _topSeatHeight);
    }

    private void OpenEffectsPreview(object sender, RoutedEventArgs e)
    {
        new BattleEffectsPreviewWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }

    private void TableMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right) CancelCardAim();
        if (DataContext is not MainViewModel vm || IsTableModalOpen(vm)) return;
        var source = e.OriginalSource as DependencyObject;
        Button? button = null;
        HandPanel? hand = null;
        var onPlaySurface = false;
        while (source is not null)
        {
            button ??= source as Button;
            hand ??= source as HandPanel;
            onPlaySurface |= ReferenceEquals(source, Battlefield) || ReferenceEquals(source, HandDock) || ReferenceEquals(source, HumanSkillPanel);
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (hand is not null) button = hand.CardButtonAt(e.GetPosition(hand));
        if (button is IdentityMarkButton) return;
        if (e.ChangedButton == MouseButton.Right && vm.HasSelection && onPlaySurface)
        {
            vm.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 &&
                 button?.DataContext is { } item)
        {
            CancelCardAim();
            e.Handled = TryQuickConfirm(item);
        }
        else if (e.ChangedButton == MouseButton.Left && hand is not null)
            ArmCardAim(hand, e.GetPosition(hand));
        else if (e.ChangedButton == MouseButton.Left &&
                 ReferenceEquals(button?.Command, vm.SelectTargetCommand) &&
                 button.CommandParameter is SeatViewModel { IsLegalTarget: true } seat && vm.Seats.Contains(seat))
        {
            // Resolve the portrait on press, just like the hand. Updating a
            // draft must not depend on a later release over the same visual.
            button.Focus();
            vm.SelectTargetCommand.Execute(seat);
            e.Handled = true;
        }
    }

    private static bool IsNavigationOverlayOpen(MainViewModel vm) => vm.IsNewGameSetupOpen || vm.IsHelpOpen ||
        vm.IsHistoryOpen || vm.IsGeneralGalleryOpen || vm.IsSettingsOpen || vm.IsShanheOpen || vm.IsWushuangShopOpen;

    // Root navigation panels cover both startup overlays; the log and result
    // panels also cover the opening cards inside the table surface.
    private static bool IsStartupOverlayActive(MainViewModel vm) => !IsNavigationOverlayOpen(vm) &&
        (vm.IsIdentityRevealOpen || vm.IsOpeningDealVisible && !vm.IsLogOpen && !vm.HasGameOver);

    private static bool IsBlockingTableOverlayOpen(MainViewModel vm) => IsNavigationOverlayOpen(vm) || vm.IsIdentityRevealOpen ||
        vm.IsOpeningDealVisible || vm.HasGameOver || vm.IsLogOpen || vm.IsReserveDialogOpen;

    private static bool IsTableModalOpen(MainViewModel vm) =>
        IsBlockingTableOverlayOpen(vm) || vm.IsGeneralSelectionPending;

    private void ClearCustomGeneralSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsCustomGeneralSelectionOpen: true } vm ||
            !ReferenceEquals(CurrentFocusSurface(vm), GeneralSelectionOverlay)) return;
        vm.CustomGeneralSearch = string.Empty;
        CustomGeneralSearchBox.Focus();
    }

    private void GeneralChoices_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || DataContext is not MainViewModel vm ||
            !vm.IsGeneralSelectionPending || IsBlockingTableOverlayOpen(vm) || sender is not ItemsControl items ||
            !ReferenceEquals(items, vm.IsCustomGeneralSelectionOpen ? CustomGeneralRoster : GeneralCandidateCards)) return;
        var choices = vm.IsCustomGeneralSelectionOpen ? vm.CustomGeneralChoices : vm.GeneralChoices;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, items))
        {
            if (source is Button button)
            {
                // Confirm only the current prompt's preview. The first click stays reversible;
                // a stale event from a primary general cannot select the secondary slot.
                if (button.IsEnabled && button.IsDescendantOf(items) && button.Command == vm.PreviewGeneralChoiceCommand &&
                    button.DataContext is GeneralChoiceViewModel choice &&
                    ReferenceEquals(choice, vm.SelectedGeneralChoice) && choices.Contains(choice) && items.Items.Contains(choice) &&
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
        if (vm.IsDiscardSelectionPending || vm.IsActiveSkillSelectionPending || vm.IsSkillTargetSelectionPending || vm.IsMultiTargetCardSelected) return false;
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
        DisposeShanheWindowHost();
        ObserveCardAimModel(null);
        _overlayFocusRequest++;
        _targetCardFocus = null;
        _audio.Dispose();
        (DataContext as MainViewModel)?.Dispose();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel { IsShanheOpen: true } campaign && !campaign.FlushShanheProgress())
        {
            e.Cancel = true;
            return;
        }
        if (DataContext is not MainViewModel viewModel || viewModel.FlushPendingSave()) return;
        viewModel.IsAutoAdvance = false;
        e.Cancel = MessageBox.Show(this, $"{viewModel.SaveStatus}\n\n仍然退出吗？选择“否”可返回牌桌重试保存。",
            "退出前保存失败", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes;
    }

    private void NewGameSetupPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => QueueOverlayFocus(NewGameSetupPanel, ref _focusBeforeLobby);

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        NewGameSetupPanel_IsVisibleChanged(NewGameSetupPanel, default);

    private void FocusLobby()
    {
        if (DataContext is MainViewModel { IsLobbyConfigurationOpen: true })
        {
            if (DataContext is MainViewModel { IsModeConfigurationOpen: false } viewModel)
            {
                if (ModeGroupChoices.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedModeGroup) is UIElement group)
                    group.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                else ModeGroupChoices.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            else if (TableModeChoices.IsVisible && TableModeChoices.SelectedItem is { } selected &&
                TableModeChoices.ItemContainerGenerator.ContainerFromItem(selected) is UIElement choice) choice.Focus();
            else ModeConfigurationPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        else LobbyHome.FocusPrimary();
    }

    private void LobbySetupStep_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded || e.NewValue is not true || sender is not FrameworkElement step ||
            DataContext is not MainViewModel viewModel) return;
        var request = _overlayFocusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (request != _overlayFocusRequest || !IsLoaded || !IsVisible || !IsEnabled || !IsKeyboardFocusWithin ||
                !ReferenceEquals(DataContext, viewModel) || !step.IsVisible || !step.IsEnabled ||
                !ReferenceEquals(CurrentFocusSurface(viewModel), NewGameSetupPanel)) return;
            // The new step may already have received a click or Tab before this
            // callback. Keep that live action and its current reading position.
            if (Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } focused &&
                focused.IsDescendantOf(LobbyConfigurationOverlay)) return;
            ModeSetupScroll.ScrollToTop();
            FocusLobby();
        });
    }

    private void RequiredSetupChoice_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 ||
            SelectedSetupChoiceItem(sender, e.OriginalSource as DependencyObject) is not { } item) return;
        // Ctrl can change a required choice, but cannot clear the current one.
        // Rejecting null in the source alone leaves the clicked row unselected.
        item.Focus();
        e.Handled = true;
    }

    private void RequiredSetupChoice_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0 &&
            SelectedSetupChoiceItem(sender, e.OriginalSource as DependencyObject) is not null)
            e.Handled = true;
    }

    private static ListBoxItem? SelectedSetupChoiceItem(object sender, DependencyObject? source) =>
        sender is ListBox choices && source is not null &&
        ItemsControl.ContainerFromElement(choices, source) is ListBoxItem { IsSelected: true } item ? item : null;

    private void PlayerGuideOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => QueueOverlayFocus(PlayerGuideOverlay, ref _focusBeforeGuide);

    private void QueueOverlayFocus(FrameworkElement overlay, ref IInputElement? returnFocus)
    {
        if (!IsLoaded || DataContext is not MainViewModel viewModel) return;
        // A close/reopen can happen before WPF reevaluates keyboard focus. Keep
        // the external return target rather than remembering this overlay itself.
        if (overlay.IsVisible && Keyboard.FocusedElement is UIElement focused &&
            !focused.IsDescendantOf(overlay)) returnFocus = focused;
        var previous = returnFocus;
        var request = ++_overlayFocusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _overlayFocusRequest || !IsLoaded || !IsVisible ||
                !ReferenceEquals(DataContext, viewModel)) return;
            // Focus is deferred for bindings. Keep a live action the user
            // reached while waiting, excluding controls in the closing layer.
            if (!(Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } focused &&
                  (overlay.IsVisible || !focused.IsDescendantOf(overlay)) &&
                  focused.IsDescendantOf(CurrentFocusSurface(viewModel))))
                FocusCurrentSurface(viewModel, overlay.IsVisible ? null : previous);
            if (!PlayerGuideOverlay.IsVisible) _focusBeforeGuide = null;
            if (!HistoryOverlay.IsVisible) _focusBeforeHistory = null;
            if (!GeneralGalleryOverlay.IsVisible) _focusBeforeGeneralGallery = null;
            if (!SettingsOverlay.IsVisible) _focusBeforeSettings = null;
            if (!IdentityRevealOverlay.IsVisible) _focusBeforeIdentityReveal = null;
            if (!NewGameSetupPanel.IsVisible) _focusBeforeLobby = null;
        }));
    }

    private void TableOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (FrameworkElement)sender;
        // A lower layer can complete while the user is typing or navigating a
        // covering panel. Preserve focus already held by the active surface.
        // Closing parents raise this event before child visibility is updated.
        if (DataContext is MainViewModel viewModel &&
            Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } focused &&
            (overlay.IsVisible || !focused.IsDescendantOf(overlay)) &&
            focused.IsDescendantOf(CurrentFocusSurface(viewModel))) return;
        IInputElement? previous = null;
        QueueOverlayFocus(overlay, ref previous);
    }

    private FrameworkElement CurrentFocusSurface(MainViewModel viewModel) => viewModel.IsWushuangShopOpen ? WushuangShopOverlay : viewModel.IsShanheOpen ? ShanheOverlay :
            viewModel.IsSettingsOpen ? SettingsOverlay :
            viewModel.IsHistoryOpen ? HistoryOverlay :
            viewModel.IsGeneralGalleryOpen ? GeneralGalleryOverlay :
            viewModel.IsHelpOpen ? PlayerGuideOverlay :
            viewModel.IsNewGameSetupOpen ? NewGameSetupPanel :
            viewModel.IsIdentityRevealOpen ? IdentityRevealOverlay :
            viewModel.IsLogOpen ? BattleLogOverlay :
            viewModel.HasGameOver ? MatchResultPanel :
            viewModel.IsGeneralSelectionPending ? GeneralSelectionOverlay :
            viewModel.IsReserveDialogOpen ? PrivateReserveOverlay :
            viewModel.IsOpeningDealVisible ? OpeningDealOverlay : TableSurface;

    private void GeneralSelectionPanel_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not false || !IsLoaded || DataContext is not MainViewModel viewModel ||
            !ReferenceEquals(CurrentFocusSurface(viewModel), GeneralSelectionOverlay)) return;
        var request = _overlayFocusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _overlayFocusRequest || !IsLoaded || !IsVisible ||
                !ReferenceEquals(DataContext, viewModel) ||
                !ReferenceEquals(CurrentFocusSurface(viewModel), GeneralSelectionOverlay)) return;
            // Rebuilding prompt controls can leave only the HWND focused. Wait
            // for binding to finish, and preserve a live action the user chose.
            if (ReferenceEquals(Keyboard.FocusedElement, this) ||
                Keyboard.FocusedElement is UIElement focused && focused.IsDescendantOf(GeneralSelectionPanel) &&
                (!focused.IsVisible || !focused.IsEnabled)) FocusCurrentSurface(viewModel, null);
        }));
    }

    private void TargetCardChoiceCards_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            vm.ChoiceSnapshot.PendingDecision is not { Kind: DecisionKind.SelectTargetCard } prompt ||
            e.NewFocus is not Button { CommandParameter: PromptChoice choice } button ||
            !ReferenceEquals(button.Command, vm.SelectTargetCardChoiceCommand) ||
            !prompt.Choices.Any(current => ReferenceEquals(current, choice))) return;
        _targetCardFocus = new(prompt, choice.Id);
    }

    private void TargetCardChoiceCards_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not false || !IsLoaded || _targetCardFocus is not { } previous ||
            DataContext is not MainViewModel vm || !ReferenceEquals(CurrentFocusSurface(vm), TableSurface)) return;
        var request = _overlayFocusRequest;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _overlayFocusRequest || !IsLoaded || !IsVisible || !IsKeyboardFocusWithin ||
                !ReferenceEquals(DataContext, vm) || !ReferenceEquals(CurrentFocusSurface(vm), TableSurface) ||
                vm.ChoiceSnapshot.PendingDecision is not { Kind: DecisionKind.SelectTargetCard } prompt ||
                !SameTargetCardPrompt(previous.Prompt, prompt)) return;
            // A full snapshot refresh retires parameters. Restore only the current
            // slot, leaving a live control the user reached while waiting alone.
            if (Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } focused &&
                !ReferenceEquals(focused, this) && focused.IsDescendantOf(this)) return;
            var choice = prompt.Choices.SingleOrDefault(current => current.Id == previous.ChoiceId);
            if (choice is not null && TargetCardChoiceCards.ItemContainerGenerator.ContainerFromItem(choice) is UIElement slot)
                slot.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private static bool SameTargetCardPrompt(PendingDecision previous, PendingDecision current) =>
        previous.PromptId == current.PromptId && previous.Kind == current.Kind && previous.PlayerSeat == current.PlayerSeat &&
        previous.Prompt == current.Prompt && previous.Revision == current.Revision && previous.SourceSeat == current.SourceSeat &&
        previous.TargetSeat == current.TargetSeat && previous.IncomingCard == current.IncomingCard &&
        previous.RequiredCardKind == current.RequiredCardKind && previous.RequiredCardCount == current.RequiredCardCount &&
        previous.IsPrivate == current.IsPrivate && previous.SkillPrompt == current.SkillPrompt &&
        previous.ValidCardIds.SequenceEqual(current.ValidCardIds) && previous.ValidTargetSeats.SequenceEqual(current.ValidTargetSeats) &&
        previous.ValidContentIds.SequenceEqual(current.ValidContentIds) && SameTargetCardChoices(previous.Choices, current.Choices);

    private static bool SameTargetCardChoices(IReadOnlyList<PromptChoice> previous, IReadOnlyList<PromptChoice> current)
    {
        if (previous.Count != current.Count) return false;
        for (var index = 0; index < previous.Count; index++)
        {
            var before = previous[index];
            var after = current[index];
            if (before.Id != after.Id || before.Description != after.Description ||
                !before.Cards.SequenceEqual(after.Cards) || !before.Targets.SequenceEqual(after.Targets) ||
                !before.ContentIds.SequenceEqual(after.ContentIds) || before.Parameters.Count != after.Parameters.Count ||
                !before.Parameters.All(pair => after.Parameters.TryGetValue(pair.Key, out var value) && value == pair.Value)) return false;
        }
        return true;
    }

    private void FocusCurrentSurface(MainViewModel viewModel, IInputElement? previous)
    {
        if (viewModel.IsShanheOpen && FocusDetachedShanheWindow()) return;
        var surface = CurrentFocusSurface(viewModel);
        if (!surface.IsVisible || !surface.IsEnabled) return;
        if (previous is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } target &&
            target.IsDescendantOf(surface))
        {
            target.Focus();
            return;
        }
        if (ReferenceEquals(surface, WushuangShopOverlay)) surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        else if (ReferenceEquals(surface, ShanheOverlay)) surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        else if (ReferenceEquals(surface, SettingsOverlay)) SettingsSoundToggle.Focus();
        else if (ReferenceEquals(surface, HistoryOverlay)) HistoryPanel.FocusHistory();
        else if (ReferenceEquals(surface, GeneralGalleryOverlay)) GeneralGallery.FocusCurrentView();
        else if (ReferenceEquals(surface, PlayerGuideOverlay)) PlayerGuide.FocusGuide();
        else if (ReferenceEquals(surface, NewGameSetupPanel)) FocusLobby();
        else if (ReferenceEquals(surface, IdentityRevealOverlay)) ContinueFromIdentityRevealButton.Focus();
        else if (ReferenceEquals(surface, BattleLogOverlay)) CloseBattleLogButton.Focus();
        else if (ReferenceEquals(surface, PrivateReserveOverlay)) CloseReserveDialogButton.Focus();
        else if (ReferenceEquals(surface, MatchResultPanel)) ResultRematchButton.Focus();
        else if (ReferenceEquals(surface, GeneralSelectionOverlay))
        {
            if (viewModel.IsCustomGeneralSelectionOpen)
            {
                if (viewModel.SelectedGeneralChoice is { } selected && viewModel.CustomGeneralChoices.Contains(selected))
                {
                    CustomGeneralRoster.ScrollIntoView(selected);
                    CustomGeneralRoster.UpdateLayout();
                    // First lands on the focusable ListBoxItem itself; its next
                    // tab stop is the actual preview button inside the row.
                    if (CustomGeneralRoster.ItemContainerGenerator.ContainerFromItem(selected) is UIElement row &&
                        row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))) return;
                }
                CustomGeneralSearchBox.Focus();
                return;
            }
            if (viewModel.SelectedGeneralChoice is { } choice &&
                GeneralCandidateCards.ItemContainerGenerator.ContainerFromItem(choice) is UIElement candidate &&
                candidate.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) return;
            ConfirmGeneralChoiceButton.Focus();
        }
        else if (ReferenceEquals(surface, OpeningDealOverlay)) DismissOpeningDealButton.Focus();
        else if (ReferenceEquals(surface, TableSurface) && viewModel.CanRerollOpeningHand) ConfirmOpeningHandButton.Focus();
        else TableSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKeyInput(e.Key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true;
    }

    private bool HandleKeyInput(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (key == Key.F && modifiers == ModifierKeys.Control && DataContext is MainViewModel searchViewModel)
            return HandleSearchShortcut(searchViewModel, isRepeat);
        CancelCardAim();
        if (DataContext is not MainViewModel viewModel) return false;
        // The roster search owns text input, including repeated digits and Enter.
        if (viewModel.CanRerollOpeningHand && ReferenceEquals(CurrentFocusSurface(viewModel), TableSurface) &&
            (ReferenceEquals(Keyboard.FocusedElement, OpeningCardSearchBox) || OpeningCardSelector.IsKeyboardFocusWithin) &&
            modifiers != ModifierKeys.Control && key != Key.Escape) return false;
        if (viewModel.IsCustomGeneralSelectionOpen &&
            ReferenceEquals(CurrentFocusSurface(viewModel), GeneralSelectionOverlay) &&
            ReferenceEquals(Keyboard.FocusedElement, CustomGeneralSearchBox) && modifiers == ModifierKeys.None)
        {
            if (key == Key.Down)
            {
                var result = viewModel.SelectedGeneralChoice is { } selected && viewModel.CustomGeneralChoices.Contains(selected)
                    ? selected : viewModel.CustomGeneralChoices.FirstOrDefault();
                if (result is not null)
                {
                    CustomGeneralRoster.ScrollIntoView(result);
                    CustomGeneralRoster.UpdateLayout();
                    if (CustomGeneralRoster.ItemContainerGenerator.ContainerFromItem(result) is UIElement row)
                        row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
                return true;
            }
            if (key is Key.Enter or Key.Space || key is >= Key.D0 and <= Key.D9 || key is >= Key.NumPad0 and <= Key.NumPad9)
                return false;
        }
        // Consume repeats before routing reaches a focused Button. A held key must
        // not toggle a draft, close the next overlay, or confirm the next prompt.
        if (isRepeat &&
            (key is Key.Enter or Key.Escape ||
             modifiers == ModifierKeys.None && key is Key.F1 or Key.F5 ||
             modifiers == ModifierKeys.Control && key == Key.M ||
             modifiers == ModifierKeys.None && key == Key.Space &&
                 (IsStartupOverlayActive(viewModel) || !IsBlockingTableOverlayOpen(viewModel)) ||
             modifiers == ModifierKeys.None &&
                 (key is >= Key.D1 and <= Key.D9 ||
                  viewModel.IsGeneralSelectionPending && key is >= Key.NumPad1 and <= Key.NumPad9) &&
                 !IsBlockingTableOverlayOpen(viewModel))) return true;
        return HandleShortcut(key, modifiers);
    }

    private bool HandleSearchShortcut(MainViewModel viewModel, bool isRepeat)
    {
        var surface = CurrentFocusSurface(viewModel);
        var gallery = ReferenceEquals(surface, GeneralGalleryOverlay);
        var roster = ReferenceEquals(surface, GeneralSelectionOverlay) && viewModel.IsCustomGeneralSelectionOpen;
        var guide = ReferenceEquals(surface, PlayerGuideOverlay);
        if (!gallery && !roster && !guide) return false;
        if (isRepeat) return true;
        if (gallery)
        {
            if (viewModel.HasGeneralGallerySelection) viewModel.CloseGeneralGalleryDetailsCommand.Execute(null);
            var request = _overlayFocusRequest;
            // Closing details also queues its return focus. Follow that callback
            // after bindings enable browse, while retaining top-surface ownership.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (request != _overlayFocusRequest || !IsLoaded || !IsVisible || !IsEnabled ||
                    !ReferenceEquals(DataContext, viewModel) || !ReferenceEquals(CurrentFocusSurface(viewModel), GeneralGalleryOverlay) ||
                    viewModel.HasGeneralGallerySelection) return;
                GeneralGallery.FocusSearch();
                if (Keyboard.FocusedElement is TextBox search && search.IsDescendantOf(GeneralGallery)) search.SelectAll();
            }));
        }
        else if (roster)
        {
            CustomGeneralSearchBox.Focus();
            CustomGeneralSearchBox.SelectAll();
        }
        else PlayerGuide.FocusCardSearch();
        return true;
    }

    private bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (DataContext is not MainViewModel viewModel) return false;
        // Shortcut ownership follows the same top surface as deferred focus.
        // A retained lower panel must not intercept the visible panel's keys.
        var surface = CurrentFocusSurface(viewModel);
        // Escape belongs to an expanded selector before its containing panel.
        // Repeats have already been consumed, so holding it cannot close both.
        if (key == Key.Escape && modifiers == ModifierKeys.None && ShanheCampaignWindow.HasOpenDropDown(surface))
            return false;
        if (ReferenceEquals(surface, WushuangShopOverlay))
        {
            if (key == Key.Escape && modifiers == ModifierKeys.None) { viewModel.CloseWushuangShopCommand.Execute(null); return true; }
            if (key == Key.F5 && modifiers == ModifierKeys.None) { viewModel.SaveGameCommand.Execute(null); return true; }
            return key == Key.Enter && modifiers == ModifierKeys.Control;
        }
        if (ReferenceEquals(surface, ShanheOverlay))
        {
            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                if (!ShanhePanel.HandleEscape()) viewModel.CloseShanheCommand.Execute(null);
                return true;
            }
            if (key == Key.F5 && modifiers == ModifierKeys.None) { viewModel.FlushShanheProgress(); return true; }
            return key == Key.Enter && modifiers == ModifierKeys.Control;
        }
        if (ReferenceEquals(surface, TableSurface) && key == Key.Enter && modifiers == ModifierKeys.None &&
            Keyboard.FocusedElement is IdentityMarkButton { IsVisible: true, IsEnabled: true } marker && marker.IsDescendantOf(TableSurface))
        {
            marker.OpenIdentityMenu();
            return true;
        }
        if (ReferenceEquals(surface, PrivateReserveOverlay))
        {
            if (key == Key.Enter && modifiers == ModifierKeys.None && Keyboard.FocusedElement is Button { IsVisible: true, IsEnabled: true } reserveButton &&
                reserveButton.IsDescendantOf(PrivateReserveOverlay)) return false;
            if (key == Key.Escape && modifiers == ModifierKeys.None) viewModel.CloseReserveDialogCommand.Execute(null);
            else if (key == Key.Enter && modifiers == ModifierKeys.None && viewModel.CanConfirmReserveExchange)
                viewModel.ConfirmReserveExchangeCommand.Execute(null);
            else if (key == Key.Enter && modifiers == ModifierKeys.None && !viewModel.IsReserveExchange)
                viewModel.CloseReserveDialogCommand.Execute(null);
            return key is Key.Enter or Key.Escape || modifiers == ModifierKeys.None && key is >= Key.D1 and <= Key.D9;
        }
        if (key == Key.Enter && modifiers == ModifierKeys.Control &&
            (ReferenceEquals(surface, TableSurface) || ReferenceEquals(surface, GeneralSelectionOverlay) ||
             ReferenceEquals(surface, IdentityRevealOverlay) || ReferenceEquals(surface, OpeningDealOverlay) ||
             ReferenceEquals(surface, MatchResultPanel)))
        {
            // An unavailable End Turn shortcut must not activate the focused
            // response, general confirmation or startup action instead.
            if (ReferenceEquals(surface, TableSurface) && viewModel.CanEndTurn)
                viewModel.EndTurnCommand.Execute(null);
            return true;
        }
        if (viewModel.CanRerollOpeningHand && ReferenceEquals(surface, TableSurface))
        {
            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                if (viewModel.IsOpeningHandEditorOpen)
                {
                    viewModel.ToggleOpeningHandEditorCommand.Execute(null);
                    ChangeOpeningHandButton.Focus();
                }
                return true;
            }
            if (key is Key.Enter or Key.Space)
                return Keyboard.FocusedElement is not Button { IsVisible: true, IsEnabled: true } openingButton ||
                    !(openingButton.IsDescendantOf(OpeningHandControls) || openingButton.IsDescendantOf(HandViewport));
        }
        if (IsStartupOverlayActive(viewModel))
        {
            // Only unmodified keys activate the overlay's default action.
            // Modified keys belong to the focused control; Ctrl+Enter is
            // deliberately consumed by the End Turn guard above.
            if (modifiers != ModifierKeys.None) return false;
            // Added setup actions keep their native keyboard activation; the
            // default continue/enter action still supports the overlay shortcut.
            var defaultCommand = viewModel.IsIdentityRevealOpen
                ? viewModel.ContinueFromIdentityRevealCommand : viewModel.DismissOpeningDealCommand;
            if (key is Key.Enter or Key.Space && modifiers == ModifierKeys.None &&
                Keyboard.FocusedElement is Button { IsVisible: true, IsEnabled: true } startupButton &&
                startupButton.IsDescendantOf(surface) && !ReferenceEquals(startupButton.Command, defaultCommand))
                return false;
            if (viewModel.IsIdentityRevealOpen)
            {
                if (key is Key.Enter or Key.Space)
                {
                    viewModel.ContinueFromIdentityRevealCommand.Execute(null);
                    return true;
                }
                return false;
            }
            if (key is Key.Enter or Key.Space or Key.Escape)
            {
                viewModel.DismissOpeningDealCommand.Execute(null);
                return true;
            }
            return key != Key.Tab;
        }
        if (ReferenceEquals(surface, GeneralGalleryOverlay) && !(key == Key.M && modifiers == ModifierKeys.Control))
        {
            if (key != Key.Escape) return false;
            if (viewModel.HasGeneralGallerySelection) viewModel.CloseGeneralGalleryDetailsCommand.Execute(null);
            else viewModel.IsGeneralGalleryOpen = false;
            return true;
        }
        if (ReferenceEquals(surface, SettingsOverlay) && !(key == Key.M && modifiers == ModifierKeys.Control))
        {
            if (key != Key.Escape) return false;
            viewModel.IsSettingsOpen = false;
            return true;
        }
        if (ReferenceEquals(surface, HistoryOverlay) && !(key == Key.M && modifiers == ModifierKeys.Control))
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
            if (viewModel.IsSoundEnabled)
            {
                if (ReferenceEquals(surface, SettingsOverlay) && SettingsSoundVolume.IsKeyboardFocusWithin)
                    SettingsSoundToggle.Focus();
                else if (ReferenceEquals(surface, NewGameSetupPanel) && SetupSoundVolume.IsKeyboardFocusWithin)
                    SetupSoundToggle.Focus();
            }
            viewModel.IsSoundEnabled = !viewModel.IsSoundEnabled;
            return true;
        }
        if (key == Key.Escape)
        {
            if (viewModel.IsHelpOpen) viewModel.IsHelpOpen = false;
            else if (viewModel.IsNewGameSetupOpen && viewModel.IsLobbyConfigurationOpen) viewModel.BackInLobbySetupCommand.Execute(null);
            else if (viewModel.IsNewGameSetupOpen) viewModel.IsNewGameSetupOpen = false;
            else if (viewModel.IsLogOpen) viewModel.IsLogOpen = false;
            else viewModel.ClearSelectionCommand.Execute(null);
            return true;
        }
        if (viewModel.IsGeneralSelectionPending && !IsBlockingTableOverlayOpen(viewModel) &&
            key == Key.Enter && modifiers == ModifierKeys.None && viewModel.CanConfirmGeneralChoice)
        {
            // Arrow navigation can focus the row container instead of its
            // button. Preview that current row before any confirmation.
            if (viewModel.IsCustomGeneralSelectionOpen &&
                Keyboard.FocusedElement is ListBoxItem { IsVisible: true, IsEnabled: true } row &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), CustomGeneralRoster) &&
                row.DataContext is GeneralChoiceViewModel choice && viewModel.CustomGeneralChoices.Contains(choice))
            {
                viewModel.PreviewGeneralChoiceCommand.Execute(choice);
                FocusCurrentSurface(viewModel, null);
                return true;
            }
            // Preview a newly focused candidate or edition before confirming it.
            // Other focused actions keep their own native Button behavior.
            if (Keyboard.FocusedElement is Button { IsVisible: true, IsEnabled: true } generalButton &&
                generalButton.IsDescendantOf(this) &&
                (!ReferenceEquals(generalButton.Command, viewModel.PreviewGeneralChoiceCommand) ||
                 !ReferenceEquals(generalButton.CommandParameter, viewModel.SelectedGeneralChoice))) return false;
            viewModel.ConfirmGeneralChoiceCommand.Execute(null);
            return true;
        }
        if (viewModel.IsGeneralSelectionPending && !IsBlockingTableOverlayOpen(viewModel) &&
            key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9 && modifiers == ModifierKeys.None)
        {
            var generalIndex = key is >= Key.D1 and <= Key.D9 ? key - Key.D1 : key - Key.NumPad1;
            var choices = viewModel.IsCustomGeneralSelectionOpen ? viewModel.CustomGeneralChoices : viewModel.GeneralChoices;
            if (generalIndex < choices.Count)
            {
                viewModel.PreviewGeneralChoiceCommand.Execute(choices[generalIndex]);
                if (IsLoaded) FocusCurrentSurface(viewModel, null);
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
        // Only a current selected hand card or portrait confirms its draft.
        // Other focused choices and actions keep their own Button behavior.
        if (key == Key.Enter && modifiers == ModifierKeys.None &&
            Keyboard.FocusedElement is Button { IsVisible: true, IsEnabled: true } focusedButton &&
            focusedButton.IsDescendantOf(this))
        {
            var selectedChoice = ReferenceEquals(focusedButton.Command, viewModel.SelectCardCommand) &&
                focusedButton.CommandParameter is CardViewModel { IsSelected: true } focusedCard && viewModel.Hand.Contains(focusedCard) ||
                ReferenceEquals(focusedButton.Command, viewModel.SelectTargetCommand) &&
                focusedButton.CommandParameter is SeatViewModel { IsSelectedTarget: true } focusedSeat && viewModel.Seats.Contains(focusedSeat);
            if (!selectedChoice) return false;
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
                var card = viewModel.Hand[index];
                viewModel.SelectCardCommand.Execute(card);
                if (IsLoaded && HandCards.ItemContainerGenerator.ContainerFromItem(card) is UIElement handCard)
                    handCard.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                return true;
            }
        }
        return false;
    }

    private void HistoryOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => QueueOverlayFocus(HistoryOverlay, ref _focusBeforeHistory);

    private void GeneralGalleryOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => QueueOverlayFocus(GeneralGalleryOverlay, ref _focusBeforeGeneralGallery);

    private void IdentityRevealOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => QueueOverlayFocus(IdentityRevealOverlay, ref _focusBeforeIdentityReveal);

    private void SettingsOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => QueueOverlayFocus(SettingsOverlay, ref _focusBeforeSettings);

    private void RetrySettingsPreferences_Click(object sender, RoutedEventArgs e)
        => QueuePreferencesRetryFocus(RetrySettingsPreferencesButton, SettingsOverlay, SettingsSoundToggle);

    private void SettingsSaveGame_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            QueueRetryFocus(SettingsSaveGameButton, SettingsOverlay, () => !viewModel.HasSaveError, () => SettingsAutoAdvanceToggle);
    }

    private void RetrySetupPreferences_Click(object sender, RoutedEventArgs e)
        => QueuePreferencesRetryFocus(RetrySetupPreferencesButton, NewGameSetupPanel, SetupSoundToggle);

    private void QueuePreferencesRetryFocus(Button retry, FrameworkElement surface, UIElement fallback)
    {
        if (DataContext is MainViewModel viewModel)
            QueueRetryFocus(retry, surface, () => !viewModel.HasPreferencesError, () => fallback);
    }

    private void PreferencesRetry_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is not true || e.NewValue is not false || sender is not Button retry ||
            DataContext is not MainViewModel { HasPreferencesError: false } viewModel) return;
        // A debounced preferences write can retire the focused retry without a click.
        if (ReferenceEquals(retry, RetrySettingsPreferencesButton))
            QueueRetryFocus(retry, SettingsOverlay, () => !viewModel.HasPreferencesError, () => SettingsSoundToggle, focusWasWithin: true);
        else if (ReferenceEquals(retry, RetrySetupPreferencesButton))
            QueueRetryFocus(retry, NewGameSetupPanel, () => !viewModel.HasPreferencesError, () => SetupSoundToggle, focusWasWithin: true);
    }

    private void WushuangSaveAccessRetry_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.OriginalSource is not Button retry ||
            !ReferenceEquals(retry.Command, viewModel.RetryWushuangSaveAccessCommand)) return;
        var configuration = viewModel.IsLobbyConfigurationOpen;
        var modeStep = viewModel.IsModeConfigurationOpen;
        var mode = viewModel.SelectedTableMode;
        QueueRetryFocus(retry, NewGameSetupPanel,
            () => !viewModel.HasWushuangSaveAccessError && viewModel.IsLobbyConfigurationOpen == configuration &&
                viewModel.IsModeConfigurationOpen == modeStep && ReferenceEquals(viewModel.SelectedTableMode, mode),
            () => !configuration ? LobbyHome.WushuangSaveAccessFocusTarget :
                viewModel.IsWushuangModeSelection && modeStep && WushuangContinueButton.IsVisible ? WushuangContinueButton :
                viewModel.IsWushuangModeSelection && modeStep && WushuangLoadManualButton.IsVisible ? WushuangLoadManualButton :
                modeStep ? StartNewGameButton : ModeGroupChoices);
    }

    private void QueueRetryFocus(Button retry, FrameworkElement surface, Func<bool> recovered, Func<UIElement> focusTarget,
        bool focusWasWithin = false)
    {
        if (DataContext is not MainViewModel viewModel ||
            !focusWasWithin && !ReferenceEquals(Keyboard.FocusedElement, retry) ||
            !ReferenceEquals(CurrentFocusSurface(viewModel), surface)) return;
        var request = _overlayFocusRequest;
        // Click precedes the bound command. Wait for its error-banner bindings
        // before replacing focus lost with the successful retry button.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (request != _overlayFocusRequest || !IsLoaded || !IsVisible || !IsEnabled || !IsKeyboardFocusWithin ||
                !ReferenceEquals(DataContext, viewModel) ||
                !ReferenceEquals(CurrentFocusSurface(viewModel), surface) ||
                !recovered() || retry.IsVisible) return;
            var fallback = focusTarget();
            if (!fallback.IsVisible || !fallback.IsEnabled || !fallback.Focusable || !fallback.IsDescendantOf(surface)) return;
            if (Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } focused &&
                !ReferenceEquals(focused, this) && !ReferenceEquals(focused, surface) &&
                focused.IsDescendantOf(surface)) return;
            fallback.Focus();
        }));
    }
}
