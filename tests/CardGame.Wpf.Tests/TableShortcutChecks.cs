using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableShortcutChecks
{
    public static void SinglePressActions()
    {
        var store = new MemorySaveStore();
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardTeamModePackage(),
            new GeneralGalleryFocusChecks.GalleryFixture());
        using var model = new MainViewModel(false, 1, false, store, contentRegistry: registry) { IsMotionEnabled = false };
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
            Program.Assert(!Input(window, Key.F, ModifierKeys.Control) && !Input(window, Key.F, ModifierKeys.Control, repeat: true) &&
                           card.IsSelected && engine.Revision == revision,
                "Ctrl+F outside a searchable panel must remain unhandled and preserve the current hand draft.");
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
            VerifyGalleryMute(card);
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

        void VerifyGalleryMute(CardViewModel selectedCard)
        {
            var engine = Program.Engine(model);
            var views = Enumerable.Range(0, engine.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat))).ToArray();
            var commands = CommandJson.Serialize(engine.AcceptedCommands);
            var originalSound = model.IsSoundEnabled;
            var originalSearch = model.GeneralGallerySearchText;
            try
            {
                model.OpenGeneralGalleryCommand.Execute(null);
                var entry = model.GeneralGalleryEntries.First();
                model.GeneralGallerySearchText = entry.Name;
                foreach (var details in new[] { false, true })
                {
                    if (details) model.SelectGeneralGalleryEntryCommand.Execute(entry);
                    var selected = model.SelectedGeneralGalleryEntry;
                    Program.Assert(model.IsGeneralGalleryOpen && model.HasGeneralGallerySelection == details &&
                                   !Input(window, Key.Enter) && ReferenceEquals(model.SelectedGeneralGalleryEntry, selected),
                        "Gallery Enter must remain owned by its focused control before testing mute in browse and detail views.");
                    Program.Assert(Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled != originalSound &&
                                   Input(window, Key.M, ModifierKeys.Control, true) && model.IsSoundEnabled != originalSound &&
                                   Input(window, Key.M, ModifierKeys.Control) && model.IsSoundEnabled == originalSound,
                        "Gallery browse and details must support Ctrl+M while a held shortcut toggles sound only once.");
                    Program.Assert(model.IsGeneralGalleryOpen && model.HasGeneralGallerySelection == details &&
                                   ReferenceEquals(model.SelectedGeneralGalleryEntry, selected) &&
                                   model.GeneralGallerySearchText == entry.Name && selectedCard.IsSelected &&
                                   ReferenceEquals(Program.Engine(model), engine) &&
                                   CommandJson.Serialize(engine.AcceptedCommands) == commands &&
                                   Enumerable.Range(0, views.Length).All(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat)) == views[seat]),
                        "Gallery mute must preserve its search draft, selected details, hidden hand draft, every player view and accepted journal.");
                }
                Program.Assert(Input(window, Key.Escape) && model.IsGeneralGalleryOpen && !model.HasGeneralGallerySelection &&
                               Input(window, Key.Escape, repeat: true) && model.IsGeneralGalleryOpen &&
                               Input(window, Key.Escape) && !model.IsGeneralGalleryOpen,
                    "Mute shortcuts must retain the gallery's separate detail and browse Escape layers.");
            }
            finally
            {
                model.IsGeneralGalleryOpen = false;
                model.GeneralGallerySearchText = originalSearch;
                model.IsSoundEnabled = originalSound;
            }
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

    internal static bool Input(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.None, bool repeat = false) =>
        (bool)typeof(MainWindow).GetMethod("HandleKeyInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [key, modifiers, repeat])!;

    // Change only this test STA's keyboard table; never send global input.
    internal static T WithControl<T>(Func<T> action) => WithModifiers(ModifierKeys.Control, action);

    internal static T WithModifiers<T>(ModifierKeys modifiers, Func<T> action)
    {
        var (aggregateKey, leftKey, rightKey) = modifiers switch
        {
            ModifierKeys.None => (0, 0, 0),
            ModifierKeys.Control => (0x11, 0xA2, 0xA3), // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
            ModifierKeys.Shift => (0x10, 0xA0, 0xA1), // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
            _ => throw new ArgumentOutOfRangeException(nameof(modifiers))
        };
        var original = new byte[256];
        Program.Assert(GetKeyboardState(original), "A modifier route must preserve the test thread's original keyboard state.");
        var originalModifiers = Keyboard.Modifiers;
        var modified = (byte[])original.Clone();
        // Native focus can inherit a user's held modifier. Define this route's
        // input explicitly, including None, without changing global input.
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
            modified[key] &= 0x7F;
        if (modifiers != ModifierKeys.None)
        {
            modified[aggregateKey] |= 0x80;
            modified[leftKey] |= 0x80;
            modified[rightKey] &= 0x7F;
        }
        Exception? actionError = null;
        try
        {
            Program.Assert(SetKeyboardState(modified) && Keyboard.Modifiers == modifiers,
                $"The real WPF primary keyboard device must observe {modifiers} in this calling thread before routing.");
            return action();
        }
        catch (Exception error)
        {
            actionError = error;
            throw;
        }
        finally
        {
            var set = SetKeyboardState(original);
            var setError = set ? 0 : Marshal.GetLastWin32Error();
            var restored = new byte[256];
            var get = GetKeyboardState(restored);
            var getError = get ? 0 : Marshal.GetLastWin32Error();
            var bytesEqual = restored.SequenceEqual(original);
            var restoredModifiers = Keyboard.Modifiers;
            if (!set || !get || !bytesEqual || restoredModifiers != originalModifiers)
            {
                var differences = string.Join(", ", Enumerable.Range(0, original.Length)
                    .Where(key => original[key] != restored[key])
                    .Select(key => $"0x{key:X2}:0x{original[key]:X2}->0x{restored[key]:X2}"));
                var restoreError = new InvalidOperationException(
                    "All 256 original keyboard bytes and WPF modifiers must be restored even when a routed assertion fails. " +
                    $"Route={modifiers}; SetKeyboardState={set}, error={setError}; GetKeyboardState={get}, error={getError}; " +
                    $"bytesEqual={bytesEqual}; originalModifiers={originalModifiers}; restoredModifiers={restoredModifiers}; differences=[{differences}].");
                if (actionError is not null)
                    throw new AggregateException("The modifier route and restoration both failed.", actionError, restoreError);
                throw restoreError;
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] states);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] states);
}
