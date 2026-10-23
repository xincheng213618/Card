using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public static void StartupAndModalTransitions() => StartupAndModalTransitions(null);

    public static void StartupAndModalTransitions(string? output)
    {
        var historyStore = new SwitchingHistoryStore();
        var preferencesStore = new PreferencesChecks.FailingPreferencesStore
        {
            Value = new(1, false, MainViewModel.DefaultSoundVolume, false) { MusicEnabled = false }
        };
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardTeamModePackage(),
            new GeneralGalleryFocusChecks.GalleryFixture());
        using var model = new MainViewModel(false, 1, true, new MemorySaveStore(), historyStore: historyStore,
            preferencesStore: preferencesStore, contentRegistry: registry)
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
            ModeSelectionChecks.RequiredChoiceInput(model, window);
            VerifyModeStepFocusOwnership();
            VerifySetupPreferencesRecovery();
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
            VerifySearchShortcutRouting();
            VerifySettingsKeyboardRecovery();
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
                new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), "键盘导航夹具",
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
            VerifyGuideSelectorEscape();
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
            var gallery = (FrameworkElement)window.FindName("GeneralGallery");
            var browseButton = Program.Find<Button>(gallery).Single(button =>
                ReferenceEquals(button.Command, model.SelectGeneralGalleryEntryCommand) &&
                ReferenceEquals(button.CommandParameter, escapeEntry));
            browseButton.Focus();
            RouteGalleryEnter(browseButton);
            Pump(window);
            var detailsClose = (Button)gallery.FindName("CloseGeneralDetailsButton");
            Program.Assert(model.HasGeneralGallerySelection && ReferenceEquals(Keyboard.FocusedElement, detailsClose),
                "Opening the actual gallery card must focus its detail close action instead of retaining the disabled browse card.");
            VerifyGalleryDetailsNavigation();
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

            void VerifyGalleryDetailsNavigation()
            {
                var entries = model.GeneralGalleryEntries.ToArray();
                Program.Assert(entries.Length >= 2, "Continuous gallery navigation needs two distinct real catalogue entries.");
                var next = Program.Find<Button>(gallery).Single(button => ReferenceEquals(button.Command, model.NextGeneralDetailsCommand));
                var previous = Program.Find<Button>(gallery).Single(button => ReferenceEquals(button.Command, model.PreviousGeneralDetailsCommand));
                next.Focus();
                for (var index = 1; index <= 2; index++)
                {
                    RouteGalleryEnter(next);
                    Pump(window);
                    Program.Assert(ReferenceEquals(model.SelectedGeneralGalleryEntry, entries[index % entries.Length]) &&
                        ReferenceEquals(Keyboard.FocusedElement, next) && SameEscapeBoundary(),
                        "Each fresh Enter on Next must advance exactly one catalogue entry and retain its real button's focus.");
                }
                RouteGalleryEnter(next, repeat: true);
                Pump(window);
                Program.Assert(ReferenceEquals(model.SelectedGeneralGalleryEntry, entries[2 % entries.Length]) &&
                    ReferenceEquals(Keyboard.FocusedElement, next) && SameEscapeBoundary(),
                    "Holding Enter must not skip another general or move focus from Next.");
                previous.Focus();
                for (var index = 1; index >= 0; index--)
                {
                    RouteGalleryEnter(previous);
                    Pump(window);
                    Program.Assert(ReferenceEquals(model.SelectedGeneralGalleryEntry, entries[index]) &&
                        ReferenceEquals(Keyboard.FocusedElement, previous) && SameEscapeBoundary(),
                        "Each fresh Enter on Previous must move back exactly one entry and retain its real button's focus.");
                }
                model.NextGeneralDetailsCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                previous.Focus();
                Pump(window);
                Program.Assert(ReferenceEquals(model.SelectedGeneralGalleryEntry, entries[1]) &&
                    ReferenceEquals(Keyboard.FocusedElement, previous),
                    "A queued detail callback must retain another live detail action the user reached before it ran.");
                RouteGalleryEnter(previous);
                Pump(window);
                detailsClose.Focus();
                RouteGalleryEnter(detailsClose);
                Pump(window);
                Program.Assert(!model.HasGeneralGallerySelection && ReferenceEquals(Keyboard.FocusedElement, browseButton) && SameEscapeBoundary(),
                    "Closing details after keyboard navigation must return to the original live catalogue card without changing the match.");
                RouteGalleryEnter(browseButton);
                Pump(window);
                Program.Assert(ReferenceEquals(model.SelectedGeneralGalleryEntry, escapeEntry) &&
                    ReferenceEquals(Keyboard.FocusedElement, detailsClose) && SameEscapeBoundary(),
                    "Reopening the original card must focus the detail action while preserving the original player-safe boundary.");
            }

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

        void VerifyModeStepFocusOwnership()
        {
            var game = Program.Engine(model);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var category = model.SelectedModeCategory;
            var mode = model.SelectedTableMode;
            var role = model.SelectedStartingRole;
            var team = model.SelectedStartingTeam;
            var originalFocus = Keyboard.FocusedElement;
            var panel = (FrameworkElement)window.FindName("ModeConfigurationPanel");
            var scroll = (ScrollViewer)window.FindName("ModeSetupScroll");
            var transform = panel.LayoutTransform;
            var offset = scroll.VerticalOffset;
            var width = window.Width;
            var height = window.Height;
            try
            {
                model.OpenLobbyCategoryCommand.Execute("identity");
                Pump(window);
                var group = model.SelectedModeGroup!;
                var configuredMode = model.SelectedTableMode;
                var guide = model.GuideIdentity;
                QueueConfiguration();
                var back = Program.Find<Button>((DependencyObject)window.FindName("LobbyConfigurationOverlay"))
                    .Single(button => ReferenceEquals(button.Command, model.BackInLobbySetupCommand));
                Program.Assert(model.IsModeConfigurationOpen && back.IsVisible && back.IsEnabled &&
                    back.ActualWidth > 0 && back.ActualHeight > 0 && back.Focus() &&
                    ReferenceEquals(Keyboard.FocusedElement, back),
                    "The loaded mode step must let the user reach its real fixed footer before deferred focus runs.");
                Pump(window);
                Console.WriteLine($"Mode step deferred focus: footer retained={ReferenceEquals(Keyboard.FocusedElement, back)}, " +
                    $"actual={Keyboard.FocusedElement?.GetType().Name}, same match={SameModeBoundary()}.");
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, back) && model.IsModeConfigurationOpen && SameModeBoundary(),
                    "Opening a mode configuration must preserve the user's live footer focus instead of resetting it to the first setup choice.");

                window.Width = 1120;
                window.Height = 780;
                panel.LayoutTransform = new ScaleTransform(1.5, 1.5);
                QueueConfiguration();
                var sound = (CheckBox)window.FindName("SetupSoundToggle");
                scroll.ScrollToEnd();
                window.UpdateLayout();
                Program.Assert(sound.IsVisible && sound.IsEnabled && sound.Focus(),
                    "The enlarged mode step must let the user reach its real sound switch before deferred focus runs.");
                window.UpdateLayout();
                var soundOffset = scroll.VerticalOffset;
                var presenter = Program.Find<ScrollContentPresenter>(scroll)
                    .Single(item => ReferenceEquals(item.TemplatedParent, scroll));
                Program.Assert(soundOffset > 0 && SoundVisible(),
                    "The enlarged fixture must reach the complete sound switch in an actually scrolled configuration.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, sound) && SoundVisible() &&
                    Math.Abs(scroll.VerticalOffset - soundOffset) < 1 && SameChoices() && SameModeBoundary(),
                    "Deferred mode focus must preserve an already reached sound switch and its reading position at 150% content zoom.");

                QueueConfiguration();
                scroll.ScrollToEnd();
                window.UpdateLayout();
                var coveredOffset = scroll.VerticalOffset;
                Program.Assert(coveredOffset > 0, "The covering-panel fixture must begin with a scrolled mode configuration.");
                model.OpenSettingsCommand.Execute(null);
                Pump(window);
                RequireFocusWithin("SettingsOverlay", "A late mode-step callback must not focus beneath covering settings.");
                Program.Assert(Math.Abs(scroll.VerticalOffset - coveredOffset) < 1 && SameChoices() && SameModeBoundary(),
                    "A covered mode-step callback must preserve the old configuration's reading position and every player boundary.");
                model.CloseSettingsCommand.Execute(null);
                Pump(window);

                QueueConfiguration();
                model.BackInLobbySetupCommand.Execute(null);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                window.UpdateLayout();
                var groups = (ItemsControl)window.FindName("ModeGroupChoices");
                var browsing = Program.Find<Button>(groups).Last(button => ReferenceEquals(button.Command, model.OpenModeConfigurationCommand));
                Program.Assert(browsing.IsVisible && browsing.IsEnabled && browsing.Focus(),
                    "Returning mode selection must let the user reach an actual mode card before old callbacks run.");
                Pump(window);
                Program.Assert(model.IsModeSelectionOpen && !model.IsModeConfigurationOpen &&
                    ReferenceEquals(Keyboard.FocusedElement, browsing) && SameChoices() && SameModeBoundary(),
                    "An expired configuration callback must preserve the newly reached mode card after returning one step.");

                QueueConfiguration();
                scroll.ScrollToEnd();
                window.UpdateLayout();
                var beforeDefaultOffset = scroll.VerticalOffset;
                var defaultChoices = (ListBox)window.FindName("RoleChoices");
                var rememberedChoice = defaultChoices.SelectedItem;
                var roomSection = window.FindName("SetupRoomSeatCards");
                FrameworkElement expectedDefault;
                if (roomSection is not null)
                {
                    Program.Assert(roomSection is ItemsControl,
                        "The optional first room section must retain its actual seat ItemsControl.");
                    var roomSeats = (ItemsControl)roomSection;
                    Program.Assert(panel is Panel configuration && configuration.Children.Count > 0 &&
                        ReferenceEquals(configuration.Children[0], roomSeats) && roomSeats.IsVisible && roomSeats.IsEnabled &&
                        roomSeats.Focusable && roomSeats.IsTabStop && roomSeats.Items.Count == model.SelectedTableMode.PlayerCount,
                        "The current room layout must expose its populated, focusable first seat section as the default navigation target.");
                    expectedDefault = roomSeats;
                }
                else
                {
                    var rememberedRow = defaultChoices.ItemContainerGenerator.ContainerFromItem(rememberedChoice) as ListBoxItem;
                    Program.Assert(rememberedChoice is not null && rememberedRow is { IsVisible: true, IsEnabled: true },
                        "The default-focus fixture must retain its actual remembered role choice and loaded row.");
                    expectedDefault = rememberedRow!;
                }
                Program.Assert(beforeDefaultOffset > 1 && Math.Abs(beforeDefaultOffset - scroll.ScrollableHeight) < 1,
                    "Default mode navigation must begin at the actual enlarged configuration's bottom before its callback runs.");
                Program.Assert(window.Focus() && ReferenceEquals(Keyboard.FocusedElement, window),
                    "The default-focus fixture must leave no live configuration action focused while retaining its native window.");
                Pump(window);
                RequireFocusWithin("ModeConfigurationPanel", "Without a live action, the new mode step must still receive its default setup focus.");
                var defaultFocus = Keyboard.FocusedElement as FrameworkElement;
                var defaultBounds = defaultFocus is not null && defaultFocus.IsDescendantOf(presenter)
                    ? defaultFocus.TransformToAncestor(presenter).TransformBounds(new Rect(defaultFocus.RenderSize)) : Rect.Empty;
                var defaultOwner = defaultFocus is ListBoxItem focusedItem ? ItemsControl.ItemsControlFromItemContainer(focusedItem) : null;
                Console.WriteLine($"Mode default navigation: offset={beforeDefaultOffset:F3}->{scroll.VerticalOffset:F3}, " +
                    $"focus={defaultFocus?.GetType().Name}/{defaultFocus?.Name}, exact target={ReferenceEquals(defaultFocus, expectedDefault)}, " +
                    $"bounds={defaultBounds}, viewport={presenter.ActualWidth:F3}x{presenter.ActualHeight:F3}, " +
                    $"same choices={SameChoices()}, same boundary={SameModeBoundary()}.");
                Program.Assert(ReferenceEquals(defaultFocus, expectedDefault) &&
                    (roomSection is not null || ReferenceEquals(defaultOwner, defaultChoices)) &&
                    ReferenceEquals(defaultChoices.SelectedItem, rememberedChoice) && !defaultBounds.IsEmpty &&
                    defaultBounds.Width > 0 && defaultBounds.Height > 0 && defaultBounds.Left >= -1 && defaultBounds.Top >= -1 &&
                    defaultBounds.Right <= presenter.ActualWidth + 1 && defaultBounds.Bottom <= presenter.ActualHeight + 1 &&
                    beforeDefaultOffset - scroll.VerticalOffset > 1 && SameChoices() && SameModeBoundary(),
                    "Default mode navigation must leave the old bottom and reveal its exact complete initial section or remembered selected action without changing choices or the suspended match.");
                Console.WriteLine("Mode-step focus: footer, enlarged sound/scroll, covering settings, expired step and default navigation preserve all player views and journal.");

                void QueueConfiguration()
                {
                    if (model.IsModeConfigurationOpen)
                    {
                        model.BackInLobbySetupCommand.Execute(null);
                        Pump(window);
                    }
                    model.OpenModeConfigurationCommand.Execute(group);
                    // Apply visibility and layout, leaving Input focus queued.
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                    window.UpdateLayout();
                }

                bool SameChoices() => model.SelectedTableMode == configuredMode && model.SelectedStartingRole == role &&
                    model.SelectedStartingTeam == team && model.GuideIdentity == guide;
                bool SoundVisible()
                {
                    var bounds = sound.TransformToAncestor(presenter).TransformBounds(new Rect(sound.RenderSize));
                    return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= presenter.ActualWidth + 1 &&
                        bounds.Bottom <= presenter.ActualHeight + 1;
                }
            }
            finally
            {
                model.IsSettingsOpen = false;
                panel.LayoutTransform = transform;
                window.Width = width;
                window.Height = height;
                model.SelectedModeCategory = category;
                model.SelectedTableMode = mode;
                model.SelectedStartingRole = role;
                model.SelectedStartingTeam = team;
                model.BackToLobbyCommand.Execute(null);
                Pump(window);
                scroll.ScrollToVerticalOffset(offset);
                if (originalFocus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } previous) previous.Focus();
            }
            Program.Assert(model.IsNewGameSetupOpen && !model.IsLobbyConfigurationOpen &&
                model.SelectedModeCategory == category && model.SelectedTableMode == mode &&
                model.SelectedStartingRole == role && model.SelectedStartingTeam == team &&
                ReferenceEquals(Keyboard.FocusedElement, originalFocus) && SameModeBoundary(),
                "Mode-step focus verification must restore the original lobby and preserve every player view and accepted command.");

            bool SameModeBoundary() => ReferenceEquals(Program.Engine(model), game) &&
                CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views);
        }

        void VerifySetupPreferencesRecovery()
        {
            var game = Program.Engine(model);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var category = model.SelectedModeCategory;
            var mode = model.SelectedTableMode;
            var role = model.SelectedStartingRole;
            var team = model.SelectedStartingTeam;
            var volume = model.SoundVolume;
            var originalFocus = Keyboard.FocusedElement;
            var root = (FrameworkElement)window.Content;
            var scroll = (ScrollViewer)window.FindName("ModeSetupScroll");
            var offset = scroll.VerticalOffset;
            var error = (DockPanel)window.FindName("SetupPreferencesError");
            var retry = (Button)window.FindName("RetrySetupPreferencesButton");
            var sound = (CheckBox)window.FindName("SetupSoundToggle");
            var start = (Button)window.FindName("StartNewGameButton");
            try
            {
                model.OpenLobbyCategoryCommand.Execute("identity");
                model.OpenModeConfigurationCommand.Execute(model.VisibleModeGroups.Single(group => group.Variants.Contains(model.SelectedTableMode)));
                VerifyVolumeMuteFocus((Slider)window.FindName("SetupSoundVolume"), sound, start, SameSetupBoundary, "Setup");
                ShowFailedRetry();
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "A failing setup retry must receive native Enter from its focused button.");
                Pump(window);
                Program.Assert(model.HasPreferencesError && error.IsVisible && ReferenceEquals(Keyboard.FocusedElement, retry) &&
                    SameSetupBoundary(), "A failed setup retry must retain its actual button and the suspended game.");

                preferencesStore.FailWrite = false;
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "A successful setup retry must receive native Enter.");
                Pump(window);
                var presenter = Program.Find<ScrollContentPresenter>(scroll)
                    .Single(item => ReferenceEquals(item.TemplatedParent, scroll));
                var soundBounds = sound.TransformToAncestor(presenter).TransformBounds(new Rect(sound.RenderSize));
                Program.Assert(!model.HasPreferencesError && error.Visibility == Visibility.Collapsed &&
                    model.IsModeConfigurationOpen && ReferenceEquals(Keyboard.FocusedElement, sound) &&
                    soundBounds.Top >= -1 && soundBounds.Bottom <= presenter.ActualHeight + 1 &&
                    soundBounds.Left >= -1 && soundBounds.Right <= presenter.ActualWidth + 1 && SameSetupBoundary(),
                    "Successful setup retry must reveal and focus the same sound section without jumping to the configuration header.");
                if (output is not null)
                    Program.Render(root, (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
                        Path.Combine(output, "setup-native-preferences-retry-after.png"));

                QueueSuccessfulRetry();
                Program.Assert(start.Focus(), "The fixed start action must accept the user's focus before deferred retry recovery.");
                Pump(window);
                Program.Assert(!model.HasPreferencesError && ReferenceEquals(Keyboard.FocusedElement, start) && SameSetupBoundary(),
                    "A delayed setup retry must preserve the live action the user reached.");

                QueueSuccessfulRetry();
                model.OpenSettingsCommand.Execute(null);
                Pump(window);
                RequireFocusWithin("SettingsOverlay", "A covering settings panel must retain focus above a completed setup retry.");
                Program.Assert(!model.HasPreferencesError && SameSetupBoundary(),
                    "Covering a completed setup retry must preserve the suspended native game.");
                model.CloseSettingsCommand.Execute(null);
                Pump(window);

                QueueSuccessfulRetry();
                model.BackInLobbySetupCommand.Execute(null);
                Pump(window);
                Program.Assert(!model.IsModeConfigurationOpen && !sound.IsVisible && !model.HasPreferencesError && SameSetupBoundary(),
                    "A delayed setup retry must not return to a closed configuration section.");
                RequireFocusWithin("ModeSelectionPanel", "Closing configuration must retain focus in mode selection.");
                Console.WriteLine("Setup preferences retry: native failure, sound-section recovery, moved focus, covering panel and closed configuration preserve the game.");
            }
            finally
            {
                preferencesStore.FailRead = false;
                preferencesStore.FailWrite = false;
                model.SoundVolume = volume;
                model.FlushPreferences();
                model.IsSettingsOpen = false;
                model.SelectedModeCategory = category;
                model.SelectedTableMode = mode;
                model.SelectedStartingRole = role;
                model.SelectedStartingTeam = team;
                model.BackToLobbyCommand.Execute(null);
                Pump(window);
                scroll.ScrollToVerticalOffset(offset);
                if (originalFocus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } previous) previous.Focus();
            }
            Program.Assert(model.IsNewGameSetupOpen && !model.IsLobbyConfigurationOpen && !model.IsSettingsOpen &&
                model.SelectedModeCategory == category && model.SelectedTableMode == mode &&
                model.SelectedStartingRole == role && model.SelectedStartingTeam == team &&
                model.SoundVolume == volume && ReferenceEquals(Keyboard.FocusedElement, originalFocus) && SameSetupBoundary(),
                "Setup retry verification must restore the original lobby, preferences, focus and suspended game.");

            void ShowFailedRetry()
            {
                preferencesStore.FailWrite = true;
                model.SoundVolume = model.SoundVolume == .31 ? .32 : .31;
                Program.Assert(!model.FlushPreferences() && model.HasPreferencesError,
                    "The setup retry fixture must expose a real failed in-memory preferences write.");
                Pump(window);
                scroll.ScrollToEnd();
                Pump(window);
                Program.Assert(retry.Focus(), "The visible setup retry button must accept native keyboard focus.");
                Pump(window);
                Program.Assert(error.IsVisible && retry.IsVisible && retry.IsEnabled &&
                    ReferenceEquals(Keyboard.FocusedElement, retry), "Setup retry must begin on its actual visible button.");
            }

            void QueueSuccessfulRetry()
            {
                ShowFailedRetry();
                preferencesStore.FailWrite = false;
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "A delayed setup retry must activate through native Enter.");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            }

            bool SameSetupBoundary() => ReferenceEquals(Program.Engine(model), game) &&
                CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views);
        }

        void VerifySettingsKeyboardRecovery()
        {
            var game = Program.Engine(model);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var root = (FrameworkElement)window.Content;
            var overlay = (FrameworkElement)window.FindName("SettingsOverlay");
            var scroll = (ScrollViewer)window.FindName("SettingsContentScroll");
            var content = (StackPanel)window.FindName("SettingsContentHost");
            var back = (Button)window.FindName("CloseSettingsButton");
            var sound = (CheckBox)window.FindName("SettingsSoundToggle");
            var speed = (ListBox)window.FindName("SettingsPlaybackSpeedSelector");
            var error = (Border)window.FindName("SettingsPreferencesError");
            var retry = (Button)window.FindName("RetrySettingsPreferencesButton");
            var originalTransform = content.LayoutTransform;
            var originalVolume = model.SoundVolume;
            try
            {
                model.OpenSettingsCommand.Execute(null);
                content.LayoutTransform = new ScaleTransform(1.5, 1.5);
                Pump(window);
                var presenter = Program.Find<ScrollContentPresenter>(scroll)
                    .Single(item => ReferenceEquals(item.TemplatedParent, scroll));
                var headerOrigin = back.TranslatePoint(new Point(), root);
                Program.Assert(scroll.ScrollableHeight > 0 && scroll.ViewportHeight > 0,
                    "The loaded enlarged settings fixture must actually overflow its scrolling viewport.");
                var selectedSpeed = (ListBoxItem)speed.ItemContainerGenerator.ContainerFromItem(speed.SelectedItem);
                selectedSpeed.Focus();
                scroll.ScrollToEnd();
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, selectedSpeed) &&
                    Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                    "Settings Tab traversal must begin on the actual lower speed item while its content is scrolled to the bottom.");
                RenderSettings("tab-bottom");
                Program.Assert(selectedSpeed.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                    "The lower settings item must participate in forward WPF Tab traversal.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, back),
                    "Forward Tab from the last settings group must reach the fixed return button.");
                Program.Assert(back.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                    "The fixed return button must allow Tab back into settings.");
                Pump(window);
                var soundBounds = sound.TransformToAncestor(presenter).TransformBounds(new Rect(sound.RenderSize));
                RenderSettings("tab-return");
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, sound) &&
                    soundBounds.Top >= -1 && soundBounds.Bottom <= presenter.ActualHeight + 1 &&
                    soundBounds.Left >= -1 && soundBounds.Right <= presenter.ActualWidth + 1 &&
                    scroll.VerticalOffset < scroll.ScrollableHeight &&
                    (back.TranslatePoint(new Point(), root) - headerOrigin).Length < 1 && SameSettingsBoundary(),
                    "Tab back into enlarged settings must automatically reveal the focused control while retaining the fixed return button and native game.");
                var volume = (Slider)window.FindName("SettingsSoundVolume");
                var soundEnabled = model.IsSoundEnabled;
                try
                {
                    model.IsSoundEnabled = true;
                    model.SoundVolume = .4;
                    Pump(window);
                    Program.Assert(volume.Focus() && ReferenceEquals(Keyboard.FocusedElement, volume),
                        "The real enabled volume slider must own keyboard focus without focusing its template parts.");
                    foreach (var (key, expected) in new[]
                    {
                        (Key.Right, .45), (Key.Left, .4), (Key.PageUp, .5),
                        (Key.PageDown, .4), (Key.End, 1d), (Key.Home, 0d)
                    })
                    {
                        Program.Assert(!RouteFocusedKey(key, ModifierKeys.None),
                            "Volume navigation must reach the focused Slider's native key handling.");
                        AssertVolume(expected);
                    }
                    var peer = UIElementAutomationPeer.CreatePeerForElement(volume)!;
                    var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
                    range.SetValue(.37);
                    AssertVolume(.37);
                    var track = (Track)volume.Template.FindName("PART_Track", volume)!;
                    foreach (var (distance, expected) in new[] { (10000d, 1d), (-10000d, 0d) })
                    {
                        track.Thumb.RaiseEvent(new DragDeltaEventArgs(distance, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                        AssertVolume(expected);
                    }
                    range.SetValue(.37);
                    AssertVolume(.37);
                    RenderSettings("volume-focused");
                    VerifyVolumeMuteFocus(volume, sound, back, SameSettingsBoundary, "Settings");
                    sound.Focus();
                    model.IsSoundEnabled = false;
                    Pump(window);
                    Program.Assert(!volume.IsEnabled && model.SoundVolume == .37 && SameSettingsBoundary(),
                        "Muting must disable the real volume control, retain its level and preserve the game.");
                    RenderSettings("volume-disabled");
                    Console.WriteLine("Settings volume: native arrows, page keys, endpoints, RangeValue and Thumb drag preserve shared volume and game state.");
                }
                finally
                {
                    model.IsSoundEnabled = soundEnabled;
                    model.SoundVolume = originalVolume;
                    Pump(window);
                    sound.Focus();
                }
                preferencesStore.FailWrite = true;
                model.SoundVolume = originalVolume == .31 ? .32 : .31;
                Program.Assert(!model.FlushPreferences() && model.HasPreferencesError,
                    "The retry focus fixture must expose an actual failed in-memory preferences write.");
                Pump(window);
                retry.Focus();
                Pump(window);
                Program.Assert(error.IsVisible && retry.IsVisible && retry.IsEnabled &&
                    ReferenceEquals(Keyboard.FocusedElement, retry),
                    "The actual visible retry button must hold keyboard focus before native Enter.");
                RenderSettings("retry-before");
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "A fresh Enter on a failing preferences retry must still reach its native button.");
                Pump(window);
                Program.Assert(model.HasPreferencesError && error.IsVisible && ReferenceEquals(Keyboard.FocusedElement, retry) &&
                    SameSettingsBoundary(),
                    "A failed native retry must retain its actual visible retry button and the unchanged game.");
                preferencesStore.FailWrite = false;
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "A fresh Enter on preferences retry must reach the native button.");
                Pump(window);
                RenderSettings("retry-after");
                var focused = Keyboard.FocusedElement;
                Console.WriteLine($"Settings retry focus: target={focused?.GetType().Name}/{(focused as FrameworkElement)?.Name}, error={error.Visibility}, open={model.IsSettingsOpen}.");
                Program.Assert(!model.HasPreferencesError && error.Visibility == Visibility.Collapsed && model.IsSettingsOpen &&
                    focused is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } target &&
                    !ReferenceEquals(target, window) && target.IsDescendantOf(overlay) && SameSettingsBoundary(),
                    "Successful native Enter retry must collapse its error without leaving focus on the Window, a retired button or the covered table.");

                preferencesStore.FailWrite = true;
                model.SoundVolume = model.SoundVolume == .32 ? .33 : .32;
                Program.Assert(!model.FlushPreferences() && model.HasPreferencesError,
                    "The late retry fixture must expose another real pending preferences write.");
                Pump(window);
                retry.Focus();
                preferencesStore.FailWrite = false;
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "The late-focus fixture must activate retry through native Enter.");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                back.Focus();
                Pump(window);
                Program.Assert(!model.HasPreferencesError && ReferenceEquals(Keyboard.FocusedElement, back) &&
                    back.IsVisible && back.IsEnabled && SameSettingsBoundary(),
                    "A delayed successful retry must retain the live return action the user reached before its callback.");

                void RenderSettings(string state)
                {
                    if (output is not null)
                        Program.Render(root, (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
                            Path.Combine(output, $"settings-native-enlarged-{state}.png"));
                }
                void AssertVolume(double expected)
                {
                    Pump(window);
                    var setupVolume = (Slider)window.FindName("SetupSoundVolume");
                    var label = (TextBlock)window.FindName("SettingsSoundVolumeValue");
                    Program.Assert(Math.Abs(model.SoundVolume - expected) < .000001 &&
                        Math.Abs(volume.Value - expected) < .000001 && Math.Abs(setupVolume.Value - expected) < .000001 &&
                        label.Text == expected.ToString("P0", label.Language.GetSpecificCulture()) &&
                        ReferenceEquals(Keyboard.FocusedElement, volume),
                        $"Native volume input must retain slider focus and synchronize the percentage and setup level: expected={expected}, actual={model.SoundVolume}.");
                }
            }
            finally
            {
                preferencesStore.FailRead = false;
                preferencesStore.FailWrite = false;
                model.SoundVolume = originalVolume;
                model.FlushPreferences();
                content.LayoutTransform = originalTransform;
                scroll.ScrollToHome();
                model.IsSettingsOpen = false;
                Pump(window);
                ((Button)window.FindName("ConfirmGeneralChoiceButton")).Focus();
            }

            bool SameSettingsBoundary() => ReferenceEquals(Program.Engine(model), game) &&
                CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views);
        }

        void VerifySearchShortcutRouting()
        {
            var game = Program.Engine(model);
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var preview = model.SelectedGeneralChoice;
            var guideQuery = model.GuideSearchText;
            var guideCategory = model.SelectedGuideCategory;
            var guideCard = model.SelectedGuideCard;
            var guideSection = model.SelectedGuideSection;
            var galleryQuery = model.GeneralGallerySearchText;
            var gallerySeries = model.SelectedGeneralGallerySeries;
            var galleryGroup = model.SelectedGeneralGalleryGroup;
            var galleryFaction = model.SelectedGeneralGalleryFaction;
            var guide = (FrameworkElement)window.FindName("PlayerGuide");
            var guideSearch = (TextBox)guide.FindName("GuideSearchBox");
            var gallery = (FrameworkElement)window.FindName("GeneralGallery");
            var gallerySearch = (TextBox)gallery.FindName("GeneralGallerySearchBox");
            var generalConfirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
            try
            {
                generalConfirm.Focus();
                Program.Assert(!RouteFocusedKey(Key.F, ModifierKeys.Control) &&
                    ReferenceEquals(Keyboard.FocusedElement, generalConfirm) && ReferenceEquals(model.SelectedGeneralChoice, preview),
                    "Ctrl+F on ordinary candidate selection must remain unhandled without opening the full roster or changing its preview.");
                model.OpenContextGuideCommand.Execute(null);
                model.GuideSearchText = "杀";
                model.SelectedGuideCategory = "基本牌";
                model.SelectedGuideSection = model.GuideSections.Single(section => section.Key == "basics");
                var selectedGuideCard = model.SelectedGuideCard;
                Pump(window);
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control), "The visible guide must own Ctrl+F.");
                Pump(window);
                Program.Assert(model.IsGuideCards && ReferenceEquals(Keyboard.FocusedElement, guideSearch) &&
                    guideSearch.SelectedText == "杀" && model.GuideSearchText == "杀" && model.SelectedGuideCategory == "基本牌" &&
                    ReferenceEquals(model.SelectedGuideCard, selectedGuideCard) && SameBoundary(),
                    "Guide Ctrl+F must reach card search and select its existing draft while preserving the filter, selected card and native match.");
                guideSearch.Select(guideSearch.Text.Length, 0);
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control, repeat: true), "The guide must consume held Ctrl+F.");
                Pump(window);
                Program.Assert(guideSearch.SelectionLength == 0 && ReferenceEquals(Keyboard.FocusedElement, guideSearch),
                    "Held Ctrl+F must not reselect the guide draft or move focus again.");
                model.GuideSearchText = "__no_card_search_result__";
                Pump(window);
                var reset = (Button)guide.FindName("GuideResetSearchButton");
                reset.Focus();
                Program.Assert(model.HasNoGuideCards && reset.IsVisible && !RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "The real empty-guide reset button must receive native Enter.");
                Pump(window);
                Program.Assert(model.GuideSearchText.Length == 0 && model.SelectedGuideCategory == "全部" && !model.HasNoGuideCards &&
                    ReferenceEquals(Keyboard.FocusedElement, guideSearch) && SameBoundary(),
                    "Resetting empty card search must clear its filters and return focus to search without changing the native journal.");
                VerifyGuideReading();

                model.OpenGeneralGalleryCommand.Execute(null);
                model.GeneralGallerySearchText = "焦点";
                Pump(window);
                ((Button)gallery.FindName("CloseGeneralGalleryButton")).Focus();
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control), "The visible general catalogue must own Ctrl+F.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, gallerySearch) && gallerySearch.SelectedText == "焦点" &&
                    model.GeneralGallerySearchText == "焦点" && SameBoundary(),
                    "Catalogue Ctrl+F must focus and select the existing search without changing its filter or game.");
                gallerySearch.Select(gallerySearch.Text.Length, 0);
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control, repeat: true), "The catalogue must consume held Ctrl+F.");
                Pump(window);
                Program.Assert(gallerySearch.SelectionLength == 0, "Held Ctrl+F must not reselect the catalogue search draft.");
                var recoveryEntry = model.GeneralGalleryEntries.First();
                model.SelectGeneralGallerySeriesCommand.Execute(recoveryEntry.SeriesId);
                model.SelectGeneralGalleryGroupCommand.Execute(recoveryEntry.GroupId);
                model.SelectGeneralGalleryFactionCommand.Execute(recoveryEntry.FactionId);
                model.GeneralGallerySearchText = "__no_general_search_result__";
                Pump(window);
                var galleryReset = Program.Find<Button>(gallery)
                    .Single(button => ReferenceEquals(button.Command, model.ClearGeneralGalleryFiltersCommand));
                Program.Assert(recoveryEntry.SeriesId != "all" && recoveryEntry.GroupId != "all" &&
                    recoveryEntry.FactionId != "all" && model.HasNoGeneralGalleryResults && galleryReset.IsVisible && galleryReset.Focus(),
                    "The native catalogue recovery fixture needs empty results within a real series, group and faction.");
                Program.Assert(!RouteFocusedKey(Key.Enter, ModifierKeys.None),
                    "The real empty-catalogue recovery button must receive native Enter.");
                Pump(window);
                Console.WriteLine($"Catalogue empty recovery: focus={Keyboard.FocusedElement?.GetType().Name}." +
                    $"{(Keyboard.FocusedElement as FrameworkElement)?.Name}, search focused={ReferenceEquals(Keyboard.FocusedElement, gallerySearch)}, " +
                    $"empty={model.HasNoGeneralGalleryResults}, series={model.SelectedGeneralGallerySeries}, group={model.SelectedGeneralGalleryGroup}.");
                Program.Assert(model.GeneralGallerySearchText.Length == 0 && model.SelectedGeneralGalleryFaction == "all" &&
                    model.SelectedGeneralGallerySeries == recoveryEntry.SeriesId && model.SelectedGeneralGalleryGroup == recoveryEntry.GroupId &&
                    !model.HasNoGeneralGalleryResults && !model.HasGeneralGallerySelection &&
                    ReferenceEquals(Keyboard.FocusedElement, gallerySearch) && SameBoundary(),
                    "Resetting empty catalogue results must clear search and faction, preserve series and group, and return focus to search without changing the native match.");
                model.SelectGeneralGallerySeriesCommand.Execute(gallerySeries);
                model.SelectGeneralGalleryGroupCommand.Execute(galleryGroup);
                model.SelectGeneralGalleryFactionCommand.Execute(galleryFaction);
                model.GeneralGallerySearchText = "焦点";
                Pump(window);
                var entry = model.GeneralGalleryEntries.First();
                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                Pump(window);
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control), "Catalogue detail Ctrl+F must return to search.");
                Pump(window);
                Program.Assert(!model.HasGeneralGallerySelection && ReferenceEquals(Keyboard.FocusedElement, gallerySearch) &&
                    gallerySearch.SelectedText == "焦点" && SameBoundary(),
                    "Ctrl+F from general details must follow the detail-return callback and select the browse search.");

                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                Pump(window);
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control), "The catalogue must accept its search request before being covered.");
                model.IsSettingsOpen = true;
                model.OpenContextGuideCommand.Execute(null);
                model.SelectedGuideSection = model.GuideSections.Single(section => section.Key == "basics");
                Pump(window);
                var settingsFocus = Keyboard.FocusedElement;
                RequireFocusWithin("SettingsOverlay", "A queued catalogue search stole focus from covering settings.");
                gallerySearch.Select(gallerySearch.Text.Length, 0);
                Program.Assert(!RouteFocusedKey(Key.F, ModifierKeys.Control) && !RouteFocusedKey(Key.F, ModifierKeys.Control, repeat: true),
                    "Settings must leave Ctrl+F unhandled despite retained searchable panels below it.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, settingsFocus) && gallerySearch.SelectionLength == 0 &&
                    model.IsGuideBasics && model.IsHelpOpen && model.IsGeneralGalleryOpen && SameBoundary(),
                    "Covered Ctrl+F must preserve the top focus, lower guide section, catalogue draft and every player-safe boundary.");
                model.IsSettingsOpen = false;
                Pump(window);
                Program.Assert(RouteFocusedKey(Key.F, ModifierKeys.Control), "The catalogue must regain search ownership above a retained guide.");
                Pump(window);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, gallerySearch) && gallerySearch.SelectedText == "焦点" &&
                    model.IsGuideBasics && ReferenceEquals(model.SelectedGeneralChoice, preview) && SameBoundary(),
                    "The top catalogue search must win over a retained guide without changing the hidden general-selection draft.");
            }
            finally
            {
                model.IsSettingsOpen = false;
                model.IsGeneralGalleryOpen = false;
                model.IsHelpOpen = false;
                model.SelectGeneralGallerySeriesCommand.Execute(gallerySeries);
                model.SelectGeneralGalleryGroupCommand.Execute(galleryGroup);
                model.SelectGeneralGalleryFactionCommand.Execute(galleryFaction);
                model.GeneralGallerySearchText = galleryQuery;
                model.GuideSearchText = guideQuery;
                model.SelectedGuideCategory = guideCategory;
                model.SelectedGuideCard = guideCard;
                model.SelectedGuideSection = guideSection;
                Pump(window);
                generalConfirm.Focus();
            }

            bool SameBoundary() => ReferenceEquals(Program.Engine(model), game) &&
                CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views);

            void VerifyGuideReading()
            {
                var details = (ScrollViewer)guide.FindName("GuideCardDetailsScroll");
                var list = (ListBox)guide.FindName("GuideCardList");
                var previousHeight = details.ReadLocalValue(FrameworkElement.HeightProperty);
                var focus = Keyboard.FocusedElement;
                try
                {
                    details.Height = 96;
                    Pump(window);
                    details.ScrollToEnd();
                    Pump(window);
                    Program.Assert(details.VerticalOffset > 0, "The native guide reading fixture must scroll actual card details.");
                    var next = model.FilteredGuideCards.Last(card => card.Kind != model.SelectedGuideCard!.Kind);
                    model.SelectedGuideCard = next;
                    Pump(window);
                    var row = list.ItemContainerGenerator.ContainerFromItem(next) as ListBoxItem;
                    var bounds = row?.TransformToAncestor(list).TransformBounds(new Rect(row.RenderSize));
                    Program.Assert(details.VerticalOffset == 0 && ReferenceEquals(list.SelectedItem, next) &&
                        bounds is { Top: >= -1 } visible && visible.Bottom <= list.ActualHeight + 1,
                        $"Changing guide cards must reset details and reveal the selected native row (offset={details.VerticalOffset}, row={bounds}, listHeight={list.ActualHeight}).");
                    details.ScrollToEnd();
                    Pump(window);
                    var offset = details.VerticalOffset;
                    model.GuideSearchText = " ";
                    model.SelectedGuideCard = null;
                    model.SelectedGuideCard = next;
                    Pump(window);
                    Program.Assert(offset > 0 && details.VerticalOffset == offset && ReferenceEquals(model.SelectedGuideCard, next),
                        "Equivalent search and same-card rebinding must preserve the native reader's scroll position.");
                    model.GuideSearchText = "__missing_reading_card__";
                    Pump(window);
                    model.ClearGuideSearchCommand.Execute(null);
                    Pump(window);
                    Program.Assert(details.VerticalOffset == 0 && model.SelectedGuideCard is not null &&
                        ReferenceEquals(Keyboard.FocusedElement, focus) && SameBoundary(),
                        "Recovering empty guide results must restore readable details while preserving focus and every player-safe boundary.");
                }
                finally
                {
                    if (previousHeight == DependencyProperty.UnsetValue) details.ClearValue(FrameworkElement.HeightProperty);
                    else details.SetValue(FrameworkElement.HeightProperty, previousHeight);
                    Pump(window);
                }
            }
        }

        void VerifyVolumeMuteFocus(Slider volume, CheckBox sound, Button other, Func<bool> sameBoundary, string surface)
        {
            var enabled = model.IsSoundEnabled;
            var level = model.SoundVolume;
            var previousFocus = Keyboard.FocusedElement;
            try
            {
                model.IsSoundEnabled = true;
                Pump(window);
                Program.Assert(volume.Focus() && ReferenceEquals(Keyboard.FocusedElement, volume),
                    $"{surface} mute verification must begin on its actual enabled volume slider.");
                Program.Assert(RouteFocusedKey(Key.M, ModifierKeys.Control),
                    $"{surface} must handle Ctrl+M from its volume slider.");
                Pump(window);
                Program.Assert(!model.IsSoundEnabled && !volume.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, sound) &&
                    sound.IsVisible && sound.IsEnabled && SameVolumeAndGame(),
                    $"{surface} muting a focused slider must retain its volume and move focus to the same sound switch.");
                Program.Assert(RouteFocusedKey(Key.M, ModifierKeys.Control, repeat: true),
                    $"{surface} must consume held Ctrl+M after its focus moves to the sound switch.");
                Pump(window);
                Program.Assert(!model.IsSoundEnabled && ReferenceEquals(Keyboard.FocusedElement, sound) && SameVolumeAndGame(),
                    $"{surface} held mute must not switch sound on again or move its recovered focus.");
                Program.Assert(RouteFocusedKey(Key.M, ModifierKeys.Control),
                    $"{surface} recovered sound switch must continue accepting Ctrl+M.");
                Pump(window);
                Program.Assert(model.IsSoundEnabled && volume.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, sound) && SameVolumeAndGame(),
                    $"{surface} unmuting must restore the same volume and preserve the usable sound-switch focus.");
                Program.Assert(other.Focus() && RouteFocusedKey(Key.M, ModifierKeys.Control),
                    $"{surface} live action must accept focus and the mute shortcut.");
                Pump(window);
                Program.Assert(!model.IsSoundEnabled && ReferenceEquals(Keyboard.FocusedElement, other) && SameVolumeAndGame(),
                    $"{surface} muting must retain a live action already focused away from the volume slider.");
                Program.Assert(RouteFocusedKey(Key.M, ModifierKeys.Control),
                    $"{surface} live action must continue accepting unmute.");
                Pump(window);
                Program.Assert(model.IsSoundEnabled && ReferenceEquals(Keyboard.FocusedElement, other) && SameVolumeAndGame(),
                    $"{surface} unmuting must not replace another valid control's focus.");
            }
            finally
            {
                model.IsSoundEnabled = enabled;
                Pump(window);
                if (previousFocus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } previous) previous.Focus();
            }
            bool SameVolumeAndGame() => Math.Abs(model.SoundVolume - level) < .000001 &&
                Math.Abs(volume.Value - level) < .000001 && sameBoundary();
        }

        bool RouteFocusedKey(Key key, ModifierKeys modifiers, bool repeat = false)
        {
            Program.Assert(Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } &&
                GetFocus() == new WindowInteropHelper(window).Handle,
                "Search keyboard input must route from the real focused enabled control in the hidden native Window.");
            var target = (UIElement)Keyboard.FocusedElement;
            return TableShortcutChecks.WithModifiers(modifiers, () =>
            {
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                target.RaiseEvent(input);
                var previewHandled = input.Handled;
                if (!previewHandled)
                {
                    input.RoutedEvent = Keyboard.KeyDownEvent;
                    target.RaiseEvent(input);
                }
                return previewHandled;
            });
        }

        void RouteGalleryEnter(Button target, bool repeat = false)
        {
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, target) && target.IsVisible && target.IsEnabled &&
                GetFocus() == new WindowInteropHelper(window).Handle,
                "Gallery Enter must route from its actual focused enabled button in the hidden native Window.");
            TableShortcutChecks.WithModifiers(ModifierKeys.None, () =>
            {
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, Key.Enter)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                target.RaiseEvent(input);
                var previewHandled = input.Handled;
                if (!previewHandled)
                {
                    input.RoutedEvent = Keyboard.KeyDownEvent;
                    target.RaiseEvent(input);
                }
                Program.Assert(input.Handled && previewHandled == repeat,
                    "Fresh gallery Enter belongs to the native button; held Enter must stop in the Window preview route.");
                return true;
            });
        }

        void VerifyGuideSelectorEscape()
        {
            var game = Program.Engine(model);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(game.AcceptedCommands);
            var guide = (FrameworkElement)window.FindName("PlayerGuide");
            model.SelectedGuideSection = model.GuideSections.Single(section => section.Key == "cards");
            Pump(window);
            var category = (ComboBox)guide.FindName("GuideCategoryBox");
            category.Focus();
            category.IsDropDownOpen = true;
            Pump(window);
            Program.Assert(category.IsDropDownOpen && model.IsHelpOpen && model.IsGuideCards,
                "The guide Escape fixture must expand its actual card-category selector.");
            Program.Assert(!RouteEscape(repeat: false) && !category.IsDropDownOpen && model.IsHelpOpen && model.IsGuideCards,
                "The native category selector must receive the first Escape before its guide can close.");
            Program.Assert(RouteEscape(repeat: true) && model.IsHelpOpen,
                "Holding Escape after the guide selector closes must retain the containing guide.");
            Program.Assert(RouteEscape(repeat: false) && !model.IsHelpOpen &&
                ReferenceEquals(Program.Engine(model), game) && CommandJson.Serialize(game.AcceptedCommands) == commands &&
                Enumerable.Range(0, views.Length).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).SequenceEqual(views),
                "A fresh Escape must close the guide without changing any player's view or the accepted journal.");
            Pump(window);
            model.OpenContextGuideCommand.Execute(null);
            Pump(window);

            bool RouteEscape(bool repeat) => TableShortcutChecks.WithModifiers(ModifierKeys.None, () =>
            {
                category.Focus();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, category),
                    "Guide selector Escape must route from the actual focused ComboBox.");
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(category)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                category.RaiseEvent(input);
                var previewHandled = input.Handled;
                if (!previewHandled)
                {
                    input.RoutedEvent = Keyboard.KeyDownEvent;
                    category.RaiseEvent(input);
                }
                return previewHandled;
            });
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
