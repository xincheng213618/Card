using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ModalFocusChecks
{
    private const int ExtendedStyle = -20;
    private const int NoActivate = 0x08000000;

    public static void StartupAndModalTransitions()
    {
        var historyStore = new SwitchingHistoryStore();
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardTeamModePackage(),
            new GeneralGalleryFocusChecks.GalleryFixture());
        using var model = new MainViewModel(false, 1, true, new MemorySaveStore(), historyStore: historyStore, contentRegistry: registry)
        {
            IsMotionEnabled = false,
            IsMusicEnabled = false,
            IsSoundEnabled = false
        };
        model.SelectedStartingRole = new StartingRoleOption("fixture", Role.Lord, string.Empty);
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1440, Height = 860
        };
        DependencyObject? latestFocus = null;
        var loadedCount = 0;
        var unloadedCount = 0;
        window.Loaded += (_, _) => loadedCount++;
        window.Unloaded += (_, _) => unloadedCount++;
        window.AddHandler(UIElement.GotFocusEvent, new RoutedEventHandler((_, args) =>
            latestFocus = args.OriginalSource as DependencyObject), true);
        try
        {
            // Protect this owned HWND before loading its real WPF tree: queued
            // focus callbacks can otherwise activate a merely hidden Window.
            var handle = new WindowInteropHelper(window).EnsureHandle();
            SetWindowLong(handle, ExtendedStyle, GetWindowLong(handle, ExtendedStyle) | NoActivate);
            Program.Assert((GetWindowLong(handle, ExtendedStyle) & NoActivate) != 0,
                "The modal fixture could not disable activation on its owned HWND.");
            var previousParent = SetParent(handle, new nint(-3));
            Program.Assert(previousParent != 0 || Marshal.GetLastPInvokeError() == 0,
                "The modal fixture could not attach its owned message-only HWND.");
            window.Show();
            LogWindowState("shown");
            ShowWindow(handle, 0);
            LogWindowState("hidden");
            // Thread-local keyboard focus keeps the real logical-focus checks
            // available without activating the message-only native surface.
            SetFocus(handle);
            Pump(window);
            LogWindowState("pumped");
            Program.Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) &&
                           GetForegroundWindow() != handle && (GetWindowLong(handle, ExtendedStyle) & NoActivate) != 0,
                $"The modal fixture must load its actual Window while keeping its native surface hidden: " +
                $"loaded={window.IsLoaded}, WPF visible={window.IsVisible}, native visible={IsWindowVisible(handle)}, " +
                $"owns foreground={GetForegroundWindow() == handle}, no activate={(GetWindowLong(handle, ExtendedStyle) & NoActivate) != 0}.");

            void LogWindowState(string stage) => Console.WriteLine(
                $"Modal Window {stage}: loaded={window.IsLoaded}, WPF visible={window.IsVisible}, " +
                $"native visible={IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}, " +
                $"source matches={(PresentationSource.FromVisual(window) as HwndSource)?.Handle == handle}, " +
                $"loaded events={loadedCount}, unloaded events={unloadedCount}.");
            RequireFocusWithin("NewGameSetupPanel", "Initial loading did not focus the visible lobby.");
            var adviceButton = (Button)((FrameworkElement)window.FindName("PlayerGuide")).FindName("PlayAdviceButton");
            Program.Assert(!adviceButton.IsEnabled && !model.CanRequestPlayAdvice,
                "The loaded advice button must initially reflect the setup's unavailable action.");

            latestFocus = null;
            model.StartNewGameCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, window.FindName("ContinueFromIdentityRevealButton")),
                "The identity reveal did not receive logical focus after a real start.");
            var revision = Program.Engine(model).Revision;
            latestFocus = null;
            model.ContinueFromIdentityRevealCommand.Execute(null);
            Pump(window);
            RequireFocusWithin("GeneralSelectionOverlay",
                "Closing identity focused the table header instead of the pending general choice.");
            Program.Assert(Program.Engine(model).Revision == revision,
                "Moving focus from identity to general selection submitted a choice.");

            var original = (Button)window.FindName("ConfirmGeneralChoiceButton");
            var state = SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0, true));
            var viewerState = SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0));
            foreach (var (name, open, close) in new (string, ICommand, ICommand)[]
            {
                ("SettingsOverlay", model.OpenSettingsCommand, model.CloseSettingsCommand),
                ("HistoryOverlay", model.OpenHistoryCommand, model.CloseHistoryCommand),
                ("GeneralGalleryOverlay", model.OpenGeneralGalleryCommand, model.CloseGeneralGalleryCommand),
                ("PlayerGuideOverlay", model.OpenContextGuideCommand, model.ToggleHelpCommand),
                ("NewGameSetupPanel", model.NewGameCommand, model.CancelNewGameSetupCommand)
            })
            {
                ResetFocus();
                original.Focus();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, original),
                    "The loaded fixture must focus the real external control before opening a modal.");
                open.Execute(null);
                Pump(window);
                RequireFocusWithin(name, $"Opening {name} did not focus its active surface.");
                ResetFocus();
                close.Execute(null);
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, original),
                    $"Closing {name} did not restore its enabled original general control.");

                original.Focus();
                open.Execute(null);
                // Apply visibility bindings, leaving Input-priority focus queued.
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                UIElement earlyTarget = name switch
                {
                    "SettingsOverlay" => (CheckBox)window.FindName("SettingsMotionToggle"),
                    "HistoryOverlay" => Program.Find<Button>((DependencyObject)window.FindName("HistoryPanel"))
                        .Single(button => ReferenceEquals(button.Command, model.RefreshHistoryCommand)),
                    "GeneralGalleryOverlay" => (Button)((FrameworkElement)window.FindName("GeneralGallery")).FindName("CloseGeneralGalleryButton"),
                    "PlayerGuideOverlay" => (ListBox)((FrameworkElement)window.FindName("PlayerGuide")).FindName("GuideNavigation"),
                    _ => Program.Find<Button>((DependencyObject)window.FindName("NewGameSetupPanel"))
                        .Single(button => button.IsVisible && ReferenceEquals(button.Command, model.CancelNewGameSetupCommand))
                };
                earlyTarget.Focus();
                var earlyFocus = Keyboard.FocusedElement;
                Program.Assert(earlyFocus is UIElement { IsVisible: true, IsEnabled: true } focusedAction &&
                               (ReferenceEquals(focusedAction, earlyTarget) || focusedAction.IsDescendantOf(earlyTarget)),
                    $"The queued-opening fixture must focus a real action in {name} before Input callbacks.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, earlyFocus),
                    $"The queued {name} opening callback replaced the user's chosen action with its default.");

                close.Execute(null);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var currentCandidate = Program.Find<Button>((ItemsControl)window.FindName("GeneralCandidateCards"))
                    .Single(button => ReferenceEquals(button.DataContext, model.SelectedGeneralChoice));
                currentCandidate.Focus();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, currentCandidate),
                    $"The queued {name} closing fixture must focus the current candidate before Input callbacks.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, currentCandidate),
                    $"The queued {name} closing callback replaced the user's candidate with its older return target.");
                Program.Assert(Program.Engine(model).Revision == revision &&
                               SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0)) == viewerState,
                    $"Opening and closing focus races in {name} changed the pending general decision.");
            }

            original.Focus();
            model.OpenSettingsCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var unavailableMotion = (CheckBox)window.FindName("SettingsMotionToggle");
            unavailableMotion.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, unavailableMotion),
                "The unavailable-action fixture must own keyboard focus before disabling its motion toggle.");
            unavailableMotion.IsEnabled = false;
            try
            {
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, window.FindName("SettingsSoundToggle")),
                    "A disabled early action prevented queued settings focus from reaching its enabled default.");
            }
            finally { unavailableMotion.ClearValue(UIElement.IsEnabledProperty); }
            model.CloseSettingsCommand.Execute(null);
            Pump(window);

            model.OpenHistoryCommand.Execute(null);
            Pump(window);
            var historyPanel = (FrameworkElement)window.FindName("HistoryPanel");
            var historyClose = (Button)historyPanel.FindName("HistoryCloseButton");
            var historyRefresh = Program.Find<Button>(historyPanel).Single(button =>
                ReferenceEquals(button.Command, model.RefreshHistoryCommand));
            Program.Assert(model.HasNoMatchHistory && historyClose.IsEnabled && historyRefresh.IsEnabled,
                "The empty-history traversal fixture must start with only its visible close and refresh actions.");
            VerifyEmptyHistoryTraversal();

            var emptyHistory = historyStore.Current;
            var populatedHistory = new MemoryMatchHistoryStore();
            var historyEntry = new MatchHistoryEntry(1, new string('a', 64),
                new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), "键盘导航夹具", Program.Engine(model).RulesVersion,
                MatchOutcome.Win, new MatchSummary(1, [new PlayerMatchResult(0, "战绩夹具", "主公", true, true, 1, 0, 0, 0, 0, 0, 0)]));
            populatedHistory.Record(historyEntry);
            var historyList = (ListBox)historyPanel.FindName("HistoryList");
            var historyPlayers = (ItemsControl)historyPanel.FindName("HistoryPlayers");
            historyStore.Current = populatedHistory;
            model.RefreshHistoryCommand.Execute(null);
            Pump(window);
            RequirePopulatedHistory();

            historyRefresh.Focus();
            historyStore.Current = emptyHistory;
            model.RefreshHistoryCommand.Execute(null);
            Pump(window);
            Program.Assert(!model.HasMatchHistory && model.SelectedHistoryEntry is null && !historyList.IsEnabled &&
                           !historyPlayers.IsEnabled && historyPlayers.Items.Count == 0 &&
                           ReferenceEquals(Keyboard.FocusedElement, historyRefresh),
                "Refreshing empty history lost refresh focus or left stale enabled details.");
            VerifyEmptyHistoryTraversal();

            historyStore.Current = populatedHistory;
            model.RefreshHistoryCommand.Execute(null);
            Pump(window);
            RequirePopulatedHistory();
            historyStore.Current = emptyHistory;
            model.RefreshHistoryCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, historyClose) ||
                           ReferenceEquals(Keyboard.FocusedElement, historyRefresh),
                $"Clearing focused history left keyboard focus on {Keyboard.FocusedElement?.GetType().Name ?? "null"} instead of a visible action.");
            VerifyEmptyHistoryTraversal();

            historyStore.Current = populatedHistory;
            model.RefreshHistoryCommand.Execute(null);
            Pump(window);
            RequirePopulatedHistory();
            historyStore.Current = emptyHistory;
            model.RefreshHistoryCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            historyStore.Current = populatedHistory;
            model.RefreshHistoryCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            historyList.Focus();
            Pump(window);
            Program.Assert(historyList.IsKeyboardFocusWithin && model.HasMatchHistory,
                "An old empty-history callback stole focus after records were restored.");

            historyStore.Current = emptyHistory;
            model.RefreshHistoryCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            model.OpenSettingsCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, window.FindName("SettingsSoundToggle")),
                "An old empty-history callback stole focus from newly opened settings.");
            model.CloseSettingsCommand.Execute(null);
            Pump(window);
            var historyReturn = Keyboard.FocusedElement;
            Program.Assert(historyReturn is UIElement { IsVisible: true, IsEnabled: true } historyReturnTarget &&
                           historyReturnTarget.IsDescendantOf((DependencyObject)window.FindName("GeneralSelectionOverlay")),
                "Closing settings did not return to the current general-selection surface.");
            model.OpenHistoryCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, historyClose),
                "Reopening emptied history did not focus its visible close action.");
            VerifyEmptyHistoryTraversal();

            void RequirePopulatedHistory()
            {
                Program.Assert(model.HasMatchHistory && ReferenceEquals(model.SelectedHistoryEntry, historyEntry) &&
                               historyList.IsEnabled && historyList.Items.Count == 1 && historyPlayers.IsEnabled &&
                               historyPlayers.Items.Count == 1 && ReferenceEquals(historyPlayers.Items[0], historyEntry.Summary.Players[0]),
                    "Refreshing populated history did not restore enabled selection and actual player details.");
                historyList.Focus();
                Pump(window);
                Program.Assert(historyList.IsKeyboardFocusWithin, "Populated history could not receive list focus.");
            }

            void VerifyEmptyHistoryTraversal()
            {
                foreach (var direction in new[] { FocusNavigationDirection.Next, FocusNavigationDirection.Previous })
                {
                    historyClose.Focus();
                    for (var step = 0; step < 6; step++)
                    {
                        var previous = (UIElement)Keyboard.FocusedElement;
                        previous.MoveFocus(new TraversalRequest(direction));
                        Pump(window);
                        var current = Keyboard.FocusedElement;
                        Program.Assert((ReferenceEquals(current, historyClose) || ReferenceEquals(current, historyRefresh)) &&
                                       !ReferenceEquals(current, previous),
                            $"Empty history {direction} traversal stopped on {current?.GetType().Name} behind the empty-state message.");
                    }
                }
            }
            model.CloseHistoryCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, historyReturn) && Program.Engine(model).Revision == revision &&
                           SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0, true)) == state,
                "Empty-history traversal lost its reopened general return control or changed its pending decision.");

            original.Focus();
            model.OpenContextGuideCommand.Execute(null);
            Pump(window);
            model.ToggleHelpCommand.Execute(null);
            // Apply each real visibility transition while keeping Input-priority
            // focus callbacks queued until both close and reopen have happened.
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            model.OpenContextGuideCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            ResetFocus();
            Pump(window);
            RequireFocusWithin("PlayerGuideOverlay", "A quick guide reopen did not retain its active focus.");
            ResetFocus();
            model.ToggleHelpCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, original),
                "Closing a rapidly reopened guide lost its original external return target.");

            ResetFocus();
            model.OpenContextGuideCommand.Execute(null);
            model.ToggleHelpCommand.Execute(null);
            model.OpenContextGuideCommand.Execute(null);
            model.OpenSettingsCommand.Execute(null);
            Pump(window);
            RequireFocusWithin("SettingsOverlay", "An old guide focus callback stole focus from settings.");
            model.CloseSettingsCommand.Execute(null);
            Pump(window);
            Program.Assert(Program.Engine(model).Revision == revision &&
                           SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0, true)) == state,
                "Modal focus transitions changed the real pending general decision.");

            model.OpenGeneralGalleryCommand.Execute(null);
            Pump(window);
            RequireFocusWithin("GeneralGalleryOverlay", "The real gallery did not receive initial focus.");
            var escapeGame = Program.Engine(model);
            var escapeViews = Enumerable.Range(0, escapeGame.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(escapeGame.CreateSnapshot(seat))).ToArray();
            var escapeCommands = CommandJson.Serialize(escapeGame.AcceptedCommands);
            var escapeRevision = escapeGame.Revision;
            var escapeEntry = model.GeneralGalleryEntries.First();
            model.SelectGeneralGalleryEntryCommand.Execute(escapeEntry);
            Pump(window);
            var gallery = (FrameworkElement)window.FindName("GeneralGallery");
            var detailsClose = (Button)gallery.FindName("CloseGeneralDetailsButton");
            Program.Assert(model.HasGeneralGallerySelection && ReferenceEquals(Keyboard.FocusedElement, detailsClose),
                "The modified Escape fixture must start on the actual gallery detail control.");
            model.OpenHistoryCommand.Execute(null);
            Pump(window);
            RequireFocusWithin("HistoryOverlay", "History did not take focus above the retained gallery.");
            Program.Assert(RouteShiftEscape(repeat: false) && !model.IsHistoryOpen && model.IsGeneralGalleryOpen &&
                ReferenceEquals(model.SelectedGeneralGalleryEntry, escapeEntry) && SameEscapeBoundary(),
                "A fresh Shift+Escape must close the focused history while retaining the covered gallery details and native match boundary.");
            Pump(window);
            RequireFocusWithin("GeneralGalleryOverlay", "Closing history did not return focus to the retained gallery.");
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, detailsClose),
                "Returning from history must focus the actual retained gallery detail control before its repeated key event.");
            var heldDetails = RouteShiftEscape(repeat: true);
            Pump(window);
            Program.Assert(heldDetails && !model.IsHistoryOpen && model.IsGeneralGalleryOpen &&
                ReferenceEquals(model.SelectedGeneralGalleryEntry, escapeEntry) &&
                ReferenceEquals(Keyboard.FocusedElement, detailsClose) && SameEscapeBoundary(),
                "Held Shift+Escape after history closes must preserve the retained gallery details, exact focus, all safe views and accepted journal.");
            Program.Assert(RouteShiftEscape(repeat: false) && model.IsGeneralGalleryOpen &&
                !model.HasGeneralGallerySelection && SameEscapeBoundary(),
                "A fresh Shift+Escape must close only the retained gallery details.");
            Pump(window);
            RequireFocusWithin("GeneralGalleryOverlay", "Closing details did not restore actual gallery browse focus.");
            var browseFocus = Keyboard.FocusedElement;
            var heldBrowse = RouteShiftEscape(repeat: true);
            Pump(window);
            Program.Assert(heldBrowse && model.IsGeneralGalleryOpen && !model.HasGeneralGallerySelection &&
                ReferenceEquals(Keyboard.FocusedElement, browseFocus) && SameEscapeBoundary(),
                "Held Shift+Escape after details close must preserve the gallery, actual browse focus, all safe views and accepted journal.");
            Program.Assert(RouteShiftEscape(repeat: false) && !model.IsGeneralGalleryOpen && SameEscapeBoundary(),
                "A fresh Shift+Escape must still close the gallery exactly once without changing the match.");
            Pump(window);

            bool SameEscapeBoundary() => ReferenceEquals(Program.Engine(model), escapeGame) && escapeGame.Revision == escapeRevision &&
                CommandJson.Serialize(escapeGame.AcceptedCommands) == escapeCommands &&
                Enumerable.Range(0, escapeViews.Length).All(seat => SnapshotJson.Serialize(escapeGame.CreateSnapshot(seat)) == escapeViews[seat]);

            model.OpenSettingsCommand.Execute(null);
            Pump(window);
            var soundToggle = (CheckBox)window.FindName("SettingsSoundToggle");
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, soundToggle),
                "Settings must focus its real default action before a covered gallery is opened.");
            model.OpenGeneralGalleryCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var coveredSettingsMotion = (CheckBox)window.FindName("SettingsMotionToggle");
            coveredSettingsMotion.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, coveredSettingsMotion),
                "The covered-opening fixture must select a settings action before the gallery's Input callback.");
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, coveredSettingsMotion),
                "Opening a covered gallery replaced the user's current settings action with its default.");
            soundToggle.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, soundToggle) &&
                           TableShortcutChecks.Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled &&
                           TableShortcutChecks.Input(window, Key.M, ModifierKeys.Control, repeat: true) && model.IsSoundEnabled,
                "A covered gallery intercepted Ctrl+M while the actual settings control had keyboard focus.");
            Program.Assert(TableShortcutChecks.Input(window, Key.M, ModifierKeys.Control) && !model.IsSoundEnabled,
                "A fresh second Ctrl+M did not restore mute in the focused settings panel.");
            Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !model.IsSettingsOpen && model.IsGeneralGalleryOpen,
                "Escape did not close the focused settings before the covered gallery.");
            Pump(window);
            RequireFocusWithin("GeneralGalleryOverlay", "Closing settings did not focus the retained gallery.");
            model.CloseGeneralGalleryCommand.Execute(null);
            Pump(window);
            Program.Assert(Program.Engine(model).Revision == revision &&
                           SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0, true)) == state,
                "Stacked navigation shortcuts changed the pending general decision.");

            model.IsMotionEnabled = true;
            model.StartNewGameFromLobbyCommand.Execute(null);
            Pump(window);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            Pump(window);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            ResetFocus();
            Pump(window);
            Program.Assert(model.IsOpeningDealVisible &&
                           ReferenceEquals(Keyboard.FocusedElement, window.FindName("DismissOpeningDealButton")),
                "Completing the real general choice did not focus the opening cards.");
            revision = Program.Engine(model).Revision;
            state = SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0, true));
            ResetFocus();
            model.ToggleLogCommand.Execute(null);
            Pump(window);
            RequireFocusWithin("BattleLogOverlay", "The visible log did not receive focus above the opening cards.");
            ResetFocus();
            model.ToggleLogCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, window.FindName("DismissOpeningDealButton")),
                "Closing the log did not restore the still-visible opening cards.");
            ResetFocus();
            model.OpenSettingsCommand.Execute(null);
            Pump(window);
            RequireFocusWithin("SettingsOverlay", "Settings did not receive focus above the opening cards.");
            ResetFocus();
            model.CloseSettingsCommand.Execute(null);
            Pump(window);
            Program.Assert(model.IsOpeningDealVisible &&
                           ReferenceEquals(Keyboard.FocusedElement, window.FindName("DismissOpeningDealButton")) &&
                           Program.Engine(model).Revision == revision &&
                           SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0, true)) == state,
                "Closing settings did not restore the opening cards without changing the match.");
            model.OpenSettingsCommand.Execute(null);
            Pump(window);
            var motionToggle = (CheckBox)window.FindName("SettingsMotionToggle");
            motionToggle.Focus();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, motionToggle),
                "The covered-deal check must begin on a real settings control.");
            // The opening-deal timer uses the same dismissal path. Completing a
            // covered layer must preserve the user's current settings control.
            model.DismissOpeningDealCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, motionToggle) &&
                           Program.Engine(model).Revision == revision,
                "Dismissing the covered opening cards moved focus within the active settings panel.");
            model.CloseSettingsCommand.Execute(null);
            Pump(window);
            model.ToggleLogCommand.Execute(null);
            Pump(window);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, window.FindName("CloseBattleLogButton")),
                "The active-table log check must begin on its real close control.");
            model.ToggleLogCommand.Execute(null);
            Pump(window);
            var table = (FrameworkElement)window.FindName("TableSurface");
            Program.Assert(Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } tableTarget &&
                           tableTarget.IsDescendantOf(table),
                "Closing the active-table log left focus on a hidden control instead of the table.");

            VerifyOpeningMotionPreference();
            VerifyAdviceAvailability(adviceButton);
            ResetFocus();
            model.OpenContextGuideCommand.Execute(null);
            window.Close();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(latestFocus is null, "A queued overlay callback focused a closed Window.");
        }
        finally
        {
            window.Close();
            window.Content = null;
        }

        void RequireFocusWithin(string name, string message)
        {
            var surface = (DependencyObject)window.FindName(name);
            Program.Assert(Keyboard.FocusedElement is UIElement target && target.IsDescendantOf(surface), message);
        }

        void ResetFocus()
        {
            latestFocus = null;
        }

        bool RouteShiftEscape(bool repeat)
        {
            var focused = Keyboard.FocusedElement as UIElement;
            Program.Assert(focused is { IsVisible: true, IsEnabled: true } &&
                GetFocus() == new WindowInteropHelper(window).Handle &&
                ReferenceEquals(PresentationSource.FromVisual(focused), PresentationSource.FromVisual(window)),
                "Modified Escape must route from the actual enabled focused control in the hidden native Window.");
            var target = focused!;
            return TableShortcutChecks.WithModifiers(ModifierKeys.Shift, () =>
            {
                var previews = 0;
                var bubbles = 0;
                KeyEventHandler observePreview = (_, args) => { if (args.Key == Key.Escape) previews++; };
                KeyEventHandler observeBubble = (_, args) => { if (args.Key == Key.Escape) bubbles++; };
                window.AddHandler(Keyboard.PreviewKeyDownEvent, observePreview, handledEventsToo: true);
                target.AddHandler(Keyboard.KeyDownEvent, observeBubble, handledEventsToo: true);
                try
                {
                    var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, Key.Escape)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                    target.RaiseEvent(input);
                    var previewHandled = input.Handled;
                    if (!previewHandled)
                    {
                        input.RoutedEvent = Keyboard.KeyDownEvent;
                        target.RaiseEvent(input);
                    }
                    Console.WriteLine($"Actual Shift+Escape: repeat={input.IsRepeat}, target={target.GetType().Name}/{(target as FrameworkElement)?.Name}, " +
                        $"previews={previews}, preview handled={previewHandled}, bubbles={bubbles}, handled={input.Handled}, " +
                        $"history={model.IsHistoryOpen}, gallery={model.IsGeneralGalleryOpen}, details={model.HasGeneralGallerySelection}.");
                    Program.Assert(previews == 1, "The actual Window must observe exactly one modified Escape tunnel.");
                    return previewHandled && bubbles == 0 && input.Handled;
                }
                finally
                {
                    window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observePreview);
                    target.RemoveHandler(Keyboard.KeyDownEvent, observeBubble);
                }
            });
        }

        void VerifyAdviceAvailability(Button adviceButton)
        {
            var game = Program.Engine(model);
            var before = SafeViews();
            var commands = game.AcceptedCommands.Count;
            model.OpenContextGuideCommand.Execute(null);
            Pump(window);
            Console.WriteLine($"Advice after startup: available={model.CanRequestPlayAdvice}, visible={adviceButton.IsVisible}, enabled={adviceButton.IsEnabled}.");
            Program.Assert(model.CanRequestPlayAdvice && adviceButton.IsVisible && adviceButton.IsEnabled &&
                           adviceButton.Command!.CanExecute(adviceButton.CommandParameter),
                "The actual advice button stayed disabled after startup reached the human play decision.");
            typeof(System.Windows.Controls.Primitives.ButtonBase)
                .GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(adviceButton, null);
            Pump(window);
            Program.Assert(model.CurrentPlayAdvice?.Revision == game.Revision &&
                           ReferenceEquals(game, Program.Engine(model)) && SafeViews().SequenceEqual(before) &&
                           game.AcceptedCommands.Count == commands,
                "The real advice button must show current guidance without submitting or altering any player's view.");
            model.ToggleHelpCommand.Execute(null);
            model.NewGameCommand.Execute(null);
            model.OpenContextGuideCommand.Execute(null);
            Pump(window);
            Program.Assert(!model.CanRequestPlayAdvice && !adviceButton.IsEnabled && model.CurrentPlayAdvice is null,
                "Opening setup must withdraw the advice command and its old recommendation.");
            model.ToggleHelpCommand.Execute(null);
            model.CancelNewGameSetupCommand.Execute(null);
            model.OpenContextGuideCommand.Execute(null);
            Pump(window);
            Program.Assert(model.CanRequestPlayAdvice && adviceButton.IsVisible && adviceButton.IsEnabled &&
                           ReferenceEquals(game, Program.Engine(model)) && SafeViews().SequenceEqual(before),
                "Returning from setup must re-enable the same bound advice button without changing the match.");
            model.ToggleHelpCommand.Execute(null);
            Pump(window);

            string[] SafeViews() => Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        }

        void VerifyOpeningMotionPreference()
        {
            SetMotion(true);
            model.StartNewGameFromLobbyCommand.Execute(null);
            var original = Program.Engine(model);
            var before = SafeViews();
            SetMotion(false);
            Program.Assert(model.IsIdentityRevealOpen && ReferenceEquals(Program.Engine(model), original) &&
                           SafeViews().SequenceEqual(before),
                "Turning off motion must preserve the identity and every player's pending view.");
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            Console.WriteLine($"Opening motion disabled before deal: motion={model.IsMotionEnabled}, opening={model.IsOpeningDealVisible}.");
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "Disabling motion during an armed start must cancel its pending opening animation.");

            SetMotion(true);
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "Re-enabling motion must not replay an opening canceled before the deal.");
            model.StartNewGameFromLobbyCommand.Execute(null);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            Program.Assert(model.IsOpeningDealVisible, "The current-motion fixture needs a visible original opening.");
            original = Program.Engine(model);
            before = SafeViews();
            SetMotion(false);
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0 &&
                           ReferenceEquals(Program.Engine(model), original) && SafeViews().SequenceEqual(before),
                "Turning off motion must retire an already visible opening without altering any player's view.");
            SetMotion(true);
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "Re-enabling motion must not replay an opening canceled while visible.");
            model.StartNewGameFromLobbyCommand.Execute(null);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            original = Program.Engine(model);
            before = SafeViews();
            Program.Assert(model.IsOpeningDealVisible, "The practice-return fixture needs a visible original opening.");
            model.StartTutorialCommand.Execute(null);
            SetMotion(false);
            model.ExitTutorialCommand.Execute(null);
            Program.Assert(!model.IsMotionEnabled && !model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0 &&
                           ReferenceEquals(Program.Engine(model), original) && SafeViews().SequenceEqual(before),
                "Tutorial return must honor current motion preferences while preserving the suspended match.");
            SetMotion(true);
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "Re-enabling motion must not replay a retired opening animation.");

            model.StartNewGameFromLobbyCommand.Execute(null);
            original = Program.Engine(model);
            before = SafeViews();
            model.StartTutorialCommand.Execute(null);
            SetMotion(false);
            model.ExitTutorialCommand.Execute(null);
            Program.Assert(model.IsIdentityRevealOpen && !model.IsMotionEnabled &&
                           ReferenceEquals(Program.Engine(model), original) && SafeViews().SequenceEqual(before),
                "Returning from practice must keep the pending identity and the current disabled-motion preference.");
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "Tutorial return must not re-arm opening motion disabled during practice.");

            string[] SafeViews() => Enumerable.Range(0, Program.Engine(model).CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(seat))).ToArray();

            void SetMotion(bool enabled)
            {
                model.OpenSettingsCommand.Execute(null);
                Pump(window);
                var toggle = (CheckBox)window.FindName("SettingsMotionToggle");
                Program.Assert(toggle.IsChecked == model.IsMotionEnabled,
                    "The actual motion checkbox must show the current preference before activation.");
                if (toggle.IsChecked != enabled)
                    typeof(System.Windows.Controls.Primitives.ToggleButton)
                        .GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(toggle, null);
                Program.Assert(toggle.IsChecked == enabled && model.IsMotionEnabled == enabled,
                    "Activating the real motion checkbox did not update its bound preference.");
                model.CloseSettingsCommand.Execute(null);
                Pump(window);
            }
        }
    }

    private static void Pump(MainWindow window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private sealed class SwitchingHistoryStore : IMatchHistoryStore
    {
        public IMatchHistoryStore Current { get; set; } = new MemoryMatchHistoryStore();
        public MatchHistoryLoad Read() => Current.Read();
        public void Record(MatchHistoryEntry entry) => Current.Record(entry);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
