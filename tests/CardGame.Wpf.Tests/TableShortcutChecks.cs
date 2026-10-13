using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableShortcutChecks
{
    public static void SinglePressActions()
    {
        var store = new MemorySaveStore();
        using var model = new MainViewModel(false, 1, false, store) { IsMotionEnabled = false };
        model.SelectedStartingRole = new StartingRoleOption("fixture", Role.Lord, string.Empty);
        model.StartNewGameCommand.Execute(null);
        var window = new MainWindow(model);
        try
        {
            var engine = Program.Engine(model);
            var revision = engine.Revision;
            Program.Assert(model.IsIdentityRevealOpen && Input(window, Key.Space) && !model.IsIdentityRevealOpen &&
                           engine.Revision == revision && Input(window, Key.Space, repeat: true),
                "Held Space escaped the dismissed identity overlay onto the next focused button.");
            var general = model.SelectedGeneralChoice;
            Program.Assert(Input(window, Key.D2, repeat: true) && ReferenceEquals(model.SelectedGeneralChoice, general) &&
                           Input(window, Key.Enter, repeat: true) && engine.Revision == revision,
                "Held selection keys changed or confirmed a general preview.");
            Program.Assert(Input(window, Key.Enter) && engine.Revision == revision + 1,
                "A fresh Enter did not confirm the current general once.");
            Program.AdvanceToDecision(model);
            revision = engine.Revision;
            var card = model.Hand.Take(9).First(item => item.IsPlayable);
            var number = Key.D1 + model.Hand.IndexOf(card);
            Program.Assert(Input(window, number) && card.IsSelected && Input(window, number, repeat: true) && card.IsSelected,
                "Holding a hand number toggled the selected card off.");
            Program.Assert(Input(window, Key.Enter, repeat: true) &&
                           Input(window, Key.Enter, ModifierKeys.Control, true) && engine.Revision == revision,
                "Held Enter confirmed a draft or ended the current Play phase.");

            Program.Assert(Input(window, Key.F1) && model.IsHelpOpen && Input(window, Key.F1, repeat: true) && model.IsHelpOpen,
                "Holding F1 repeatedly opened and closed the guide.");
            Program.Assert(!Input(window, number, repeat: true) && !Input(window, Key.Space, repeat: true) && card.IsSelected,
                "Repeating text digits or spaces in the guide were intercepted as hand input.");
            Program.Assert(Input(window, Key.Escape) && !model.IsHelpOpen && card.IsSelected &&
                           Input(window, Key.Escape, repeat: true) && card.IsSelected,
                "Held Escape closed the guide and then cleared the preserved draft.");

            var sound = model.IsSoundEnabled;
            Program.Assert(Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled != sound &&
                           Input(window, Key.M, ModifierKeys.Control, true) && model.IsSoundEnabled != sound,
                "Holding Ctrl+M repeatedly toggled sound.");
            Input(window, Key.M, ModifierKeys.Control);
            var writes = store.WriteCount;
            Program.Assert(Input(window, Key.F5) && store.WriteCount == writes + 1 &&
                           Input(window, Key.F5, repeat: true) && store.WriteCount == writes + 1,
                "Holding F5 repeatedly wrote the manual save.");

            foreach (var (open, close) in new (Action, Action)[]
            {
                (() => model.IsSettingsOpen = true, () => model.IsSettingsOpen = false),
                (() => model.IsHistoryOpen = true, () => model.IsHistoryOpen = false),
                (() => model.IsGeneralGalleryOpen = true, () => model.IsGeneralGalleryOpen = false)
            })
            {
                open();
                Program.Assert(!Input(window, number, repeat: true) && !Input(window, Key.Space, repeat: true) &&
                               card.IsSelected && engine.Revision == revision,
                    "A modal overlay consumed repeatable text digits or changed the hidden hand.");
                close();
            }
            model.ClearSelectionCommand.Execute(null);
            var equip = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
            var equipment = model.Hand.Single(item => item.Id == equip.CardId);
            model.SelectCardCommand.Execute(equipment);
            Program.Assert(Input(window, Key.Enter) && engine.Revision == revision + 1 &&
                           engine.CreateSnapshot(0).Players[0].Equipment.Any(item => item.Id == equipment.Id),
                "A fresh Enter did not commit the real equipment draft.");
            Program.Assert(Input(window, Key.Enter, repeat: true) && engine.Revision == revision + 1,
                "A held confirmation advanced the next decision.");
            VerifyRejectedTutorialEndTurn();
        }
        finally
        {
            window.Content = null;
            window.Close();
        }

        void VerifyRejectedTutorialEndTurn()
        {
            var original = Program.Engine(model);
            var originalViews = Views(original);
            var root = (FrameworkElement)window.Content;
            foreach (var route in new[] { "button", "Ctrl+Enter" })
            {
                if (model.IsTutorialActive) model.RestartTutorialStepCommand.Execute(null);
                else model.StartTutorialCommand.Execute(null);
                Layout();
                var practice = Program.Engine(model);
                var prompt = practice.PendingDecision;
                Program.Assert(model.IsTutorialActive && !model.IsTutorialStepComplete &&
                               prompt?.Kind == DecisionKind.PlayCard && model.CanEndTurn,
                    "The rejected End Turn fixture must reach the real first tutorial Play prompt.");
                var slashAction = practice.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
                    action.TargetSeats.Count == 1 && model.Hand.Any(card => card.Id == action.CardId &&
                        card.Kind == CardKind.Slash && card.IsPlayable));
                var slash = model.Hand.Single(card => card.Id == slashAction.CardId);
                model.SelectCardCommand.Execute(slash);
                var target = model.Seats.Single(seat => seat.Seat == slashAction.TargetSeats.Single());
                Program.Assert(target.IsLegalTarget, "The tutorial Slash needs a genuinely legal native target.");
                model.SelectTargetCommand.Execute(target);
                Layout();
                var confirm = Program.Find<Button>(root).Single(button => button.Name == "PlayCardButton");
                var endTurn = Program.Find<Button>(root).Single(button => button.Name == "EndTurnButton");
                Program.Assert(slash.IsSelected && target.IsSelectedTarget && model.HasSelection && model.CanConfirmSelected &&
                               confirm.Visibility == Visibility.Visible && confirm.IsEnabled &&
                               ReferenceEquals(confirm.Command, model.ConfirmSelectedCommand) &&
                               confirm.Command!.CanExecute(confirm.CommandParameter) &&
                               endTurn.Visibility == Visibility.Visible && endTurn.IsEnabled &&
                               ReferenceEquals(endTurn.Command, model.EndTurnCommand) &&
                               endTurn.Command!.CanExecute(endTurn.CommandParameter),
                    "Both the real confirmation and End Turn buttons must be available before tutorial rejection.");
                var selectedText = model.SelectedCardText;
                var views = Views(practice);
                var beforeRevision = practice.Revision;
                var commands = CommandJson.Serialize(practice.AcceptedCommands);
                var commandCount = practice.AcceptedCommands.Count;
                var movements = System.Text.Json.JsonSerializer.Serialize(practice.CardMovements);
                var eventCount = practice.Events.Count;
                if (route == "button")
                    typeof(System.Windows.Controls.Primitives.ButtonBase)
                        .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(endTurn, null);
                else Program.Assert(Input(window, Key.Enter, ModifierKeys.Control),
                    "Ctrl+Enter must reach the available tutorial End Turn route.");
                Layout();
                Program.Assert(model.TutorialInstruction.StartsWith("这一节先练习", StringComparison.Ordinal) &&
                               !model.IsTutorialStepComplete && ReferenceEquals(Program.Engine(model), practice) &&
                               practice.Revision == beforeRevision && Views(practice).SequenceEqual(views) &&
                               CommandJson.Serialize(practice.AcceptedCommands) == commands &&
                               System.Text.Json.JsonSerializer.Serialize(practice.CardMovements) == movements &&
                               practice.Events.Count == eventCount,
                    $"Tutorial rejection through {route} must preserve the engine, every player-safe view and its journals.");
                Program.Assert(model.Hand.Contains(slash) && slash.IsSelected &&
                               model.Hand.Count(card => card.IsSelected) == 1 && model.Seats.Contains(target) &&
                               target.IsSelectedTarget && model.Seats.Count(seat => seat.IsSelectedTarget) == 1 &&
                               model.SelectedCardText == selectedText && model.HasSelection && model.CanConfirmSelected &&
                               ReferenceEquals(Program.Find<Button>(root).Single(button => button.Name == "PlayCardButton"), confirm) &&
                               confirm.Visibility == Visibility.Visible && confirm.IsEnabled &&
                               confirm.Command!.CanExecute(confirm.CommandParameter),
                    $"Rejected End Turn through {route} must preserve the real Slash/target draft and enabled confirmation button.");
                Program.Assert(Input(window, Key.Enter) && model.IsTutorialStepComplete &&
                               practice.Revision == beforeRevision + 1 && practice.AcceptedCommands.Count == commandCount + 1 &&
                               practice.AcceptedCommands.Last() is PlayCardCommand play && play.CardId == slash.Id &&
                               play.ActorSeat == prompt!.PlayerSeat && play.ExpectedRevision == beforeRevision &&
                               play.PromptId == prompt.PromptId && play.TargetSeats.SequenceEqual([target.Seat]) &&
                               practice.Events.Skip(eventCount).Count(envelope => envelope.Payload is CardUseDeclaredEvent declared &&
                                   declared.CardId == slash.Id && declared.CardKind == CardKind.Slash && declared.SourceSeat == 0) == 1,
                    $"The original Enter confirmation must still submit the exact tutorial Slash once after rejected {route}.");
            }
            model.ExitTutorialCommand.Execute(null);
            Program.Assert(!model.IsTutorialActive && ReferenceEquals(Program.Engine(model), original) &&
                           Views(original).SequenceEqual(originalViews),
                "Rejected tutorial End Turn checks must leave the suspended real table intact.");

            void Layout()
            {
                root.Measure(new Size(1120, 740));
                root.Arrange(new Rect(0, 0, 1120, 740));
                root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                root.UpdateLayout();
            }
            static string[] Views(GameEngine game) => Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        }
    }

    public static void OpeningOverlayPriority()
    {
        var store = new MemorySaveStore();
        using var model = new MainViewModel(false, 1, false, store, useExpandedContent: true)
        {
            IsMotionEnabled = true,
            IsSoundEnabled = false
        };
        model.SelectedStartingRole = new StartingRoleOption("fixture", Role.Lord, string.Empty);
        model.StartNewGameFromLobbyCommand.Execute(null);
        model.ContinueFromIdentityRevealCommand.Execute(null);
        model.ConfirmGeneralChoiceCommand.Execute(null);
        AdvanceToOpeningDeal();
        Program.Assert(model.IsOpeningDealVisible, "The real lobby start did not present its opening cards.");
        model.SaveGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var saved = store.Read(GameSaveSlot.Manual);
        var savedViews = Views();
        var window = new MainWindow(model);
        try
        {
            VerifyNavigationOverlays(() => model.IsOpeningDealVisible, "opening deal");
            VerifyStackedNavigation();
            model.ToggleLogCommand.Execute(null);
            VerifyNativeInput(() => model.IsOpeningDealVisible, "opening deal beneath the log");
            Program.Assert(Input(window, Key.Escape) && !model.IsLogOpen && model.IsOpeningDealVisible,
                "Escape dismissed the opening cards beneath the visible log.");
            Program.Assert(Input(window, Key.Space) && !model.IsOpeningDealVisible &&
                           Input(window, Key.Space, repeat: true),
                "The opening cards no longer dismiss once, or held Space escaped onto the table.");

            model.IsMotionEnabled = false;
            model.StartNewGameCommand.Execute(null);
            Program.Assert(model.IsIdentityRevealOpen, "The second start did not present its identity.");
            VerifyNavigationOverlays(() => model.IsIdentityRevealOpen, "identity reveal");
            Program.Assert(Input(window, Key.Space) && !model.IsIdentityRevealOpen &&
                           Input(window, Key.Space, repeat: true),
                "The identity reveal no longer dismisses once, or held Space escaped onto general selection.");
            VerifyStartupReplacement();
            VerifyOpeningTimerCoverage();
            VerifyStartupControlEnter(Program.Engine(model).ContentRegistry);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }

        string[] Views() => Enumerable.Range(0, Program.Engine(model).CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(seat))).ToArray();

        void AdvanceToOpeningDeal()
        {
            for (var step = 0; step < 32 && !model.IsOpeningDealVisible; step++)
            {
                if (AnswerStartupFaction(model)) continue;
                var snapshot = Program.Engine(model).CreateSnapshot(0);
                Program.Assert(snapshot.Status == EngineStatus.Running && snapshot.PendingDecision is null && model.CanStepAi,
                    $"The opening fixture reached another decision or ended before showing its actual deal: " +
                    $"status={snapshot.Status}, prompt={snapshot.PendingDecision?.Kind}, revision={snapshot.Revision}.");
                model.StepAiCommand.Execute(null);
            }
            Program.Assert(model.IsOpeningDealVisible,
                "The opening fixture must show its actual deal within 32 setup steps without simulating onward to play.");
        }

        void VerifyOpeningTimerCoverage()
        {
            model.StartNewGameFromLobbyCommand.Execute(null);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            AdvanceToOpeningDeal();
            Program.Assert(model.IsOpeningDealVisible, "The timer fixture needs a real opening deal.");
            var engine = Program.Engine(model);
            var views = Views();
            var openingIds = model.OpeningHandCards.Select(card => card.Id).ToArray();
            var timer = (DispatcherTimer)typeof(MainViewModel)
                .GetField("_openingDealTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model)!;
            var interval = timer.Interval;
            var ticks = 0;
            EventHandler observe = (_, _) => ticks++;
            timer.Tick += observe;
            try
            {
                // Exercise the actual DispatcherTimer without adding six 1.8 s
                // waits to routine verification. This is not a timing benchmark.
                timer.Interval = TimeSpan.FromMilliseconds(50);
                foreach (var (name, open, isOpen) in new (string, ICommand, Func<bool>)[]
                {
                    ("settings", model.OpenSettingsCommand, () => model.IsSettingsOpen),
                    ("history", model.OpenHistoryCommand, () => model.IsHistoryOpen),
                    ("gallery", model.OpenGeneralGalleryCommand, () => model.IsGeneralGalleryOpen),
                    ("guide", model.OpenContextGuideCommand, () => model.IsHelpOpen),
                    ("lobby", model.NewGameCommand, () => model.IsNewGameSetupOpen),
                    ("log", model.ToggleLogCommand, () => model.IsLogOpen)
                })
                {
                    open.Execute(null);
                    var beforeTicks = ticks;
                    PumpTimer(TimeSpan.FromMilliseconds(120));
                    Console.WriteLine($"Opening timer behind {name}: enabled={timer.IsEnabled}, " +
                                      $"ticks={ticks - beforeTicks}, opening={model.IsOpeningDealVisible}.");
                    Program.Assert(isOpen() && model.IsOpeningDealVisible && !timer.IsEnabled && ticks == beforeTicks &&
                                   model.OpeningHandCards.Select(card => card.Id).SequenceEqual(openingIds) &&
                                   ReferenceEquals(Program.Engine(model), engine) && Views().SequenceEqual(views),
                        $"The opening countdown ran or changed the match behind {name}.");
                    Program.Assert(Input(window, Key.Escape) && !isOpen() && model.IsOpeningDealVisible && timer.IsEnabled,
                        $"Returning from {name} must resume the current opening countdown.");
                }
                model.OpenGeneralGalleryCommand.Execute(null);
                model.IsSettingsOpen = true;
                PumpTimer(TimeSpan.FromMilliseconds(120));
                Program.Assert(model.IsGeneralGalleryOpen && model.IsSettingsOpen && model.IsOpeningDealVisible &&
                               !timer.IsEnabled, "Stacked navigation panels must keep the opening countdown paused.");
                Program.Assert(Input(window, Key.Escape) && !model.IsSettingsOpen && model.IsGeneralGalleryOpen &&
                               model.IsOpeningDealVisible && !timer.IsEnabled,
                    "Closing only the top navigation panel must not resume the covered opening countdown.");
                PumpTimer(TimeSpan.FromMilliseconds(120));
                Program.Assert(Input(window, Key.Escape) && !model.IsGeneralGalleryOpen &&
                               model.IsOpeningDealVisible && timer.IsEnabled,
                    "Closing the last covering panel must resume the current opening countdown.");
                var beforeDismiss = ticks;
                PumpTimer(TimeSpan.FromSeconds(3), () => !model.IsOpeningDealVisible);
                Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0 && !timer.IsEnabled &&
                               ticks == beforeDismiss + 1 && ReferenceEquals(Program.Engine(model), engine) &&
                               Views().SequenceEqual(views),
                    "An uncovered opening must expire once through its real timer without changing any player's view.");
                Console.WriteLine($"Opening timer uncovered: ticks={ticks - beforeDismiss}, faces={model.OpeningHandCards.Count}.");
            }
            finally
            {
                timer.Tick -= observe;
                timer.Interval = interval;
            }
        }

        void VerifyStartupReplacement()
        {
            model.IsMotionEnabled = true;
            model.StartNewGameFromLobbyCommand.Execute(null);
            var armedEngine = Program.Engine(model);
            var armedViews = Views();
            Program.Assert(model.IsIdentityRevealOpen && model.IsGeneralSelectionPending,
                "The replacement fixture needs an armed lobby start before general selection.");
            store.Write(GameSaveSlot.Manual, saved with
                { Checkpoint = saved.Checkpoint with { SchemaVersion = int.MaxValue } });
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(model.HasSaveError && ReferenceEquals(Program.Engine(model), armedEngine) &&
                           Views().SequenceEqual(armedViews) && model.IsIdentityRevealOpen && !model.IsOpeningDealVisible,
                "A rejected load must preserve the active match and its still-pending identity/opening presentation.");
            store.Write(GameSaveSlot.Manual, saved);
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && !ReferenceEquals(Program.Engine(model), armedEngine) &&
                           Views().SequenceEqual(savedViews) && !model.IsIdentityRevealOpen &&
                           !model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "A successful load must retire the replaced match's identity and pending opening cards.");

            // Loading intentionally restores the lobby's random identity choice.
            // Keep subsequent startup checks on the human lord's first turn.
            model.SelectedStartingRole = new StartingRoleOption("fixture", Role.Lord, string.Empty);

            model.StartNewGameFromLobbyCommand.Execute(null);
            model.StartNewGameCommand.Execute(null);
            Program.Assert(Input(window, Key.Space) && !model.IsIdentityRevealOpen && Input(window, Key.Enter),
                "A plain replacement start must still expose and confirm its own current identity/general.");
            Program.AdvanceToDecision(model);
            Program.Assert(!model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "An armed lobby start must not lend its opening animation to a later plain start.");

            model.StartNewGameFromLobbyCommand.Execute(null);
            var original = Program.Engine(model);
            var originalViews = Views();
            model.StartTutorialCommand.Execute(null);
            Program.Assert(model.IsTutorialActive && !ReferenceEquals(Program.Engine(model), original) &&
                           !model.IsIdentityRevealOpen && !model.IsOpeningDealVisible && model.OpeningHandCards.Count == 0,
                "Entering practice must withdraw the suspended match's identity and armed opening presentation.");
            model.ExitTutorialCommand.Execute(null);
            Program.Assert(!model.IsTutorialActive && ReferenceEquals(Program.Engine(model), original) &&
                           Views().SequenceEqual(originalViews) && model.IsIdentityRevealOpen && !model.IsOpeningDealVisible,
                "Returning from practice must restore the original unconfirmed identity without changing any player's view.");
            Program.Assert(Input(window, Key.Space) && Input(window, Key.Enter),
                "The resumed lobby start must still accept its own identity and general confirmation.");
            AdvanceToOpeningDeal();
            Console.WriteLine($"Resumed startup: identity={model.IsIdentityRevealOpen}, opening={model.IsOpeningDealVisible}, " +
                $"faces={model.OpeningHandCards.Count}, hand={model.Hand.Count}, current={model.OpeningHandCards.All(model.Hand.Contains)}, " +
                $"prompt={Program.Engine(model).PendingDecision?.Kind}, revision={Program.Engine(model).Revision}.");
            Program.Assert(model.IsOpeningDealVisible && model.OpeningHandCards.Count > 0 &&
                           model.OpeningHandCards.All(model.Hand.Contains),
                "Only the resumed lobby match's actual deal may consume its pending opening presentation.");

            var openingIds = model.OpeningHandCards.Select(card => card.Id).ToArray();
            originalViews = Views();
            model.StartTutorialCommand.Execute(null);
            Program.Assert(model.IsTutorialActive && !model.IsOpeningDealVisible && !model.IsIdentityRevealOpen &&
                           model.OpeningHandCards.Count == 0,
                "Practice must also withdraw an already visible original opening hand.");
            model.ExitTutorialCommand.Execute(null);
            Program.Assert(ReferenceEquals(Program.Engine(model), original) && Views().SequenceEqual(originalViews) &&
                           model.IsOpeningDealVisible && model.OpeningHandCards.Select(card => card.Id).SequenceEqual(openingIds) &&
                           model.OpeningHandCards.All(model.Hand.Contains),
                "Returning to visible opening cards must use the resumed hand's current objects and preserve all player views.");
            model.DismissOpeningDealCommand.Execute(null);
        }
        void VerifyNavigationOverlays(Func<bool> underlyingVisible, string underlying)
        {
            foreach (var (name, open, isOpen) in new (string, ICommand, Func<bool>)[]
            {
                ("settings", model.OpenSettingsCommand, () => model.IsSettingsOpen),
                ("history", model.OpenHistoryCommand, () => model.IsHistoryOpen),
                ("gallery", model.OpenGeneralGalleryCommand, () => model.IsGeneralGalleryOpen),
                ("guide", model.OpenContextGuideCommand, () => model.IsHelpOpen),
                ("lobby", model.NewGameCommand, () => model.IsNewGameSetupOpen)
            })
            {
                open.Execute(null);
                Program.Assert(isOpen(), $"The real {name} command did not open its panel.");
                VerifyNativeInput(underlyingVisible, $"{underlying} beneath {name}");
                Program.Assert(Input(window, Key.Escape) && !isOpen() && underlyingVisible(),
                    $"Escape did not close {name} while preserving the underlying {underlying}.");
            }
            model.OpenContextGuideCommand.Execute(null);
            Program.Assert(Input(window, Key.F1) && !model.IsHelpOpen && underlyingVisible(),
                $"F1 was intercepted by the {underlying} beneath the guide.");
            model.OpenGeneralGalleryCommand.Execute(null);
            model.SelectGeneralGalleryEntryCommand.Execute(model.GeneralGalleryEntries.First());
            Program.Assert(model.HasGeneralGallerySelection && Input(window, Key.Escape) &&
                           !model.HasGeneralGallerySelection && model.IsGeneralGalleryOpen && underlyingVisible(),
                $"Escape did not close gallery details above the {underlying}.");
            Program.Assert(Input(window, Key.Escape) && !model.IsGeneralGalleryOpen && underlyingVisible(),
                $"The second Escape did not close the gallery above the {underlying}.");
        }

        void VerifyStackedNavigation()
        {
            var engine = Program.Engine(model);
            var revision = engine.Revision;
            var snapshot = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
            model.OpenGeneralGalleryCommand.Execute(null);
            var entry = model.GeneralGalleryEntries.First();
            model.SelectGeneralGalleryEntryCommand.Execute(entry);
            model.OpenHistoryCommand.Execute(null);
            Program.Assert(model.IsHistoryOpen && model.IsGeneralGalleryOpen && model.HasGeneralGallerySelection,
                "Real history opening must preserve the covered gallery details in this fixture.");
            VerifyNativeInput(() => model.IsOpeningDealVisible, "history above gallery details");
            VerifyMute("history above gallery details");
            Program.Assert(!Input(window, Key.F1) && !model.IsHelpOpen &&
                           Input(window, Key.Escape) && !model.IsHistoryOpen && model.IsGeneralGalleryOpen &&
                           ReferenceEquals(model.SelectedGeneralGalleryEntry, entry),
                "Escape closed the covered gallery details instead of the visible history panel.");
            Program.Assert(Input(window, Key.Escape, repeat: true) && model.HasGeneralGallerySelection &&
                           Input(window, Key.Escape) && !model.HasGeneralGallerySelection && model.IsGeneralGalleryOpen &&
                           Input(window, Key.Escape) && !model.IsGeneralGalleryOpen,
                "Returning from history lost gallery details or held Escape closed the next layer.");

            model.NewGameCommand.Execute(null);
            var category = model.ModeCategories.First(item => model.OpenLobbyCategoryCommand.CanExecute(item.Id));
            model.OpenLobbyCategoryCommand.Execute(category.Id);
            model.OpenSettingsCommand.Execute(null);
            model.OpenGeneralGalleryCommand.Execute(null);
            model.OpenHistoryCommand.Execute(null);
            model.OpenContextGuideCommand.Execute(null);
            Program.Assert(model.IsSettingsOpen && model.IsHistoryOpen && model.IsGeneralGalleryOpen &&
                           model.IsHelpOpen && model.IsNewGameSetupOpen && model.IsLobbyConfigurationOpen,
                "The real navigation commands did not establish all five retained layers.");
            VerifyNativeInput(() => model.IsOpeningDealVisible, "settings above retained navigation panels");
            VerifyMute("settings above retained navigation panels");
            Program.Assert(!Input(window, Key.F1) && model.IsHelpOpen &&
                           Input(window, Key.Escape) && !model.IsSettingsOpen && model.IsHistoryOpen &&
                           model.IsGeneralGalleryOpen && model.IsHelpOpen && model.IsNewGameSetupOpen,
                "Settings shortcuts changed a covered navigation panel.");
            VerifyMute("history above retained navigation panels");
            Program.Assert(Input(window, Key.Escape) && !model.IsHistoryOpen && model.IsGeneralGalleryOpen &&
                           model.IsHelpOpen && model.IsNewGameSetupOpen,
                "Escape did not peel history while retaining gallery, guide and lobby.");
            var sound = model.IsSoundEnabled;
            Program.Assert(!Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled == sound &&
                           !Input(window, Key.F1) && model.IsHelpOpen &&
                           Input(window, Key.Escape) && !model.IsGeneralGalleryOpen && model.IsHelpOpen &&
                           model.IsNewGameSetupOpen && Input(window, Key.Escape, repeat: true) && model.IsHelpOpen,
                "Gallery shortcuts changed the hidden guide, or repeated Escape closed it.");
            Program.Assert(Input(window, Key.F1) && !model.IsHelpOpen && model.IsNewGameSetupOpen &&
                           Input(window, Key.Escape) && model.IsNewGameSetupOpen && !model.IsLobbyConfigurationOpen &&
                           Input(window, Key.Escape) && !model.IsNewGameSetupOpen && model.IsOpeningDealVisible &&
                           engine.Revision == revision && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == snapshot,
                "Peeling the guide and lobby changed the underlying opening deal or match.");

            void VerifyMute(string context)
            {
                var sound = model.IsSoundEnabled;
                Program.Assert(Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled != sound &&
                               Input(window, Key.M, ModifierKeys.Control, repeat: true) && model.IsSoundEnabled != sound &&
                               Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled == sound,
                    $"The {context} let a covered panel block or repeat Ctrl+M.");
            }
        }

        void VerifyNativeInput(Func<bool> underlyingVisible, string context)
        {
            var engine = Program.Engine(model);
            var revision = engine.Revision;
            var snapshot = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
            foreach (var key in new[] { Key.Tab, Key.Enter, Key.Space, Key.D1 })
                Program.Assert(!Input(window, key) && underlyingVisible(),
                    $"The {context} intercepted {key} or dismissed a hidden startup overlay.");
            Program.Assert(!Input(window, Key.Space, repeat: true) && underlyingVisible() &&
                           engine.Revision == revision && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == snapshot,
                $"The {context} intercepted repeatable text input or changed the underlying match.");
        }
    }

    private static bool AnswerStartupFaction(MainViewModel model)
    {
        var game = Program.Engine(model);
        var snapshot = game.CreateSnapshot(0);
        if (snapshot.PendingDecision is not { Kind: DecisionKind.SelectFaction } pending) return false;
        Program.Assert(snapshot.Status == EngineStatus.AwaitingHumanFactionSelection &&
                       pending.PlayerSeat == 0 && pending.IsPrivate && model.IsSkillSelectionPending &&
                       model.SkillChoices.Count > 0,
            "The opening setup must expose its actual private faction choice before dealing cards.");
        var choice = model.SkillChoices[0];
        var commands = game.AcceptedCommands.Count;
        model.SelectSkillChoiceCommand.Execute(choice);
        Program.Assert(ReferenceEquals(Program.Engine(model), game) &&
                       game.Revision == snapshot.Revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                       game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.ActorSeat == 0 &&
                       answer.PromptId == pending.PromptId && answer.Choice == choice.Id &&
                       answer.ExpectedRevision == snapshot.Revision,
            "The real faction choice must submit exactly one command through the existing UI choice route.");
        return true;
    }

    private static void VerifyStartupControlEnter(ContentRegistry registry)
    {
        using var model = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true,
            saveStore: new MemorySaveStore(), useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        model.SelectedStartingRole = new StartingRoleOption("fixture", Role.Lord, string.Empty);
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var root = (FrameworkElement)window.Content;
        // This check owns its presentation timer. Keep its ordinary dismissal
        // from racing the isolated shortcut routes; the existing timer checks
        // above still exercise the real interval and covered/uncovered ticks.
        var timer = (DispatcherTimer)typeof(MainViewModel).GetField("_openingDealTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;
        var originalInterval = timer.Interval;
        try
        {
            timer.Interval = TimeSpan.FromSeconds(30);
            var handle = new WindowInteropHelper(window).EnsureHandle();
            const int extendedStyle = -20;
            const int noActivate = 0x08000000;
            SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
            var previousParent = SetParent(handle, new nint(-3));
            var parentError = Marshal.GetLastPInvokeError();
            Program.Assert((previousParent != 0 || parentError == 0) && IsMessageOnlyWindow(handle),
                $"The startup shortcut fixture must own a message-only HWND: parent={previousParent}, error={parentError}.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            Pump();

            model.StartNewGameFromLobbyCommand.Execute(null);
            Pump();
            var identityGame = Program.Engine(model);
            var identityBoundary = Boundary(identityGame);
            var identityPreview = model.SelectedGeneralChoice;
            var identityButton = (Button)window.FindName("ContinueFromIdentityRevealButton");
            Program.Assert(identityGame.Seed == 31 && model.IsIdentityRevealOpen && model.IsGeneralSelectionPending &&
                model.ChoiceSnapshot.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0, IsPrivate: true } &&
                model.CanConfirmGeneralChoice && !model.CanEndTurn &&
                ReferenceEquals(identityButton.Command, model.ContinueFromIdentityRevealCommand),
                "The identity route must begin on the real seed31 lobby start, covering its native private selection prompt.");
            var identityRoute = PressControlEnter(identityButton);
            Pump();
            var identityPreserved = ReferenceEquals(Program.Engine(model), identityGame) &&
                Boundary(identityGame) == identityBoundary && model.IsIdentityRevealOpen && model.IsGeneralSelectionPending &&
                ReferenceEquals(model.SelectedGeneralChoice, identityPreview) &&
                ReferenceEquals(Keyboard.FocusedElement, identityButton) && identityButton.IsVisible && identityButton.IsEnabled;
            Console.WriteLine($"Startup identity Ctrl+Enter: seed=31, visible={model.IsIdentityRevealOpen}, " +
                $"preview handled={identityRoute.PreviewHandled}, bubble={identityRoute.BubbleCount}, handled={identityRoute.Handled}, " +
                $"same native boundary={Boundary(identityGame) == identityBoundary}, exact button focus={ReferenceEquals(Keyboard.FocusedElement, identityButton)}.");

            // Continue normally whether or not the broken shortcut already
            // continued, so both independent startup observations reach logs.
            if (model.IsIdentityRevealOpen) model.ContinueFromIdentityRevealCommand.Execute(null);
            Program.Assert(model.IsGeneralSelectionPending && model.CanConfirmGeneralChoice,
                "The original native general prompt must remain available when preparing the opening presentation.");
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            if (AnswerStartupFaction(model)) Program.AdvanceToDecision(model);
            Pump();
            var openingGame = Program.Engine(model);
            var openingBoundary = Boundary(openingGame);
            var openingCards = model.OpeningHandCards.ToArray();
            var openingButton = (Button)window.FindName("DismissOpeningDealButton");
            Program.Assert(!model.IsIdentityRevealOpen && !model.IsGeneralSelectionPending && model.IsOpeningDealVisible &&
                openingCards.Length > 0 && openingCards.All(model.Hand.Contains) && timer.IsEnabled &&
                ReferenceEquals(openingButton.Command, model.DismissOpeningDealCommand),
                "The opening route must use the actual private initial cards created by the same native lobby start and confirmed general.");
            var openingRoute = PressControlEnter(openingButton);
            Pump();
            var openingPreserved = ReferenceEquals(Program.Engine(model), openingGame) &&
                Boundary(openingGame) == openingBoundary && model.IsOpeningDealVisible &&
                model.OpeningHandCards.SequenceEqual(openingCards) && openingCards.All(model.Hand.Contains) &&
                timer.IsEnabled && ReferenceEquals(Keyboard.FocusedElement, openingButton) &&
                openingButton.IsVisible && openingButton.IsEnabled;
            Console.WriteLine($"Startup opening Ctrl+Enter: seed=31, visible={model.IsOpeningDealVisible}, faces={model.OpeningHandCards.Count}, " +
                $"preview handled={openingRoute.PreviewHandled}, bubble={openingRoute.BubbleCount}, handled={openingRoute.Handled}, " +
                $"same native boundary={Boundary(openingGame) == openingBoundary}, exact button focus={ReferenceEquals(Keyboard.FocusedElement, openingButton)}.");
            Program.Assert(identityPreserved && openingPreserved &&
                identityRoute is { PreviewHandled: true, BubbleCount: 0, Handled: true } &&
                openingRoute is { PreviewHandled: true, BubbleCount: 0, Handled: true },
                "Ctrl+Enter must preserve both actual startup surfaces, their exact focused buttons and every native safe view and journal without continuing identity or dismissing initial cards.");

            void Pump()
            {
                root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }

            (bool PreviewHandled, int BubbleCount, bool Handled) PressControlEnter(Button button)
            {
                Pump();
                button.Focus();
                Program.Assert(window.IsLoaded && window.IsVisible && IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                    !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                    (GetWindowLong(handle, extendedStyle) & noActivate) != 0 &&
                    button.IsVisible && button.IsEnabled && button.IsDescendantOf(window) &&
                    ReferenceEquals(PresentationSource.FromVisual(button), PresentationSource.FromVisual(window)) &&
                    ReferenceEquals(Keyboard.FocusedElement, button) && Keyboard.Modifiers == ModifierKeys.None &&
                    button.Command is not null && button.Command.CanExecute(button.CommandParameter),
                    "Each startup key route must focus its current actual enabled Button in the owned hidden nonforeground HWND before setting modifiers.");
                var tunnels = 0;
                var bubbles = 0;
                KeyEventHandler observeTunnel = (_, args) => { if (args.Key == Key.Enter) tunnels++; };
                KeyEventHandler observeBubble = (_, args) => { if (args.Key == Key.Enter) bubbles++; };
                window.AddHandler(Keyboard.PreviewKeyDownEvent, observeTunnel, handledEventsToo: true);
                button.AddHandler(Keyboard.KeyDownEvent, observeBubble, handledEventsToo: true);
                try
                {
                    return WithControl(() =>
                    {
                        Program.Assert(Keyboard.Modifiers == ModifierKeys.Control && ReferenceEquals(Keyboard.FocusedElement, button),
                            "The actual startup button must still own focus when the calling-thread Control state is applied.");
                        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, 0, Key.Enter)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        button.RaiseEvent(input);
                        var previewHandled = input.Handled;
                        if (!input.Handled)
                        {
                            input.RoutedEvent = Keyboard.KeyDownEvent;
                            button.RaiseEvent(input);
                        }
                        Program.Assert(tunnels == 1 && IsMessageOnlyWindow(handle) && !IsWindowVisible(handle) &&
                            GetForegroundWindow() != handle,
                            "The actual startup Window must observe one Control+Enter tunnel while remaining hidden and nonforeground.");
                        return (previewHandled, bubbles, input.Handled);
                    });
                }
                finally
                {
                    window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observeTunnel);
                    button.RemoveHandler(Keyboard.KeyDownEvent, observeBubble);
                }
            }
        }
        finally
        {
            timer.Interval = originalInterval;
            window.Content = null;
            window.Close();
        }

        static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
        {
            game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
            Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
            Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
        });
    }

    private static bool IsMessageOnlyWindow(nint handle)
    {
        nint previous = 0;
        for (var count = 0; count < 4096; count++)
        {
            previous = FindWindowEx(new nint(-3), previous, null, null);
            if (previous == handle) return true;
            if (previous == 0) return false;
        }
        throw new InvalidOperationException("The owned startup HWND was not found within the bounded message-only window enumeration.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    internal static bool Input(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.None, bool repeat = false) =>
        (bool)typeof(MainWindow).GetMethod("HandleKeyInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [key, modifiers, repeat])!;

    // Change only this test STA's keyboard table; never send global input.
    internal static T WithControl<T>(Func<T> action) => WithModifiers(ModifierKeys.Control, action);

    internal static T WithModifiers<T>(ModifierKeys modifiers, Func<T> action)
    {
        var (aggregateKey, leftKey, rightKey) = modifiers switch
        {
            ModifierKeys.Control => (0x11, 0xA2, 0xA3), // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
            ModifierKeys.Shift => (0x10, 0xA0, 0xA1), // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
            _ => throw new ArgumentOutOfRangeException(nameof(modifiers))
        };
        var original = new byte[256];
        Program.Assert(GetKeyboardState(original) && Keyboard.Modifiers == ModifierKeys.None,
            "A modifier route must begin with the test thread's original unmodified keyboard state.");
        var modified = (byte[])original.Clone();
        modified[aggregateKey] |= 0x80;
        modified[leftKey] |= 0x80;
        modified[rightKey] &= 0x7F;
        try
        {
            Program.Assert(SetKeyboardState(modified) && Keyboard.Modifiers == modifiers,
                $"The real WPF primary keyboard device must observe {modifiers} in this calling thread before routing.");
            return action();
        }
        finally
        {
            Program.Assert(SetKeyboardState(original), "The modifier route must restore its original calling-thread keyboard state.");
            var restored = new byte[256];
            Program.Assert(GetKeyboardState(restored) && restored.SequenceEqual(original) && Keyboard.Modifiers == ModifierKeys.None,
                "All 256 original keyboard bytes and WPF modifiers must be restored even when a routed assertion fails.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] states);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] states);

    private static void PumpTimer(TimeSpan timeout, Func<bool>? until = null)
    {
        var frame = new DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        monitor.Tick += (_, _) =>
        {
            if (until?.Invoke() is true || watch.Elapsed >= timeout) frame.Continue = false;
        };
        try { monitor.Start(); Dispatcher.PushFrame(frame); }
        finally { monitor.Stop(); }
    }
}
