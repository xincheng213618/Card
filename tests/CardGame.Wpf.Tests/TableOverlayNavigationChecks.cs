using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableOverlayNavigationChecks
{
    public static void OpeningLogAndResults(string output)
    {
        var saves = new MemorySaveStore();
        using var model = new MainViewModel(false, 31, true, saves, historyStore: new MemoryMatchHistoryStore())
        {
            IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false
        };
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var timer = (DispatcherTimer)typeof(MainViewModel).GetField("_openingDealTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;
        var originalInterval = timer.Interval;
        var escapes = new List<string>();
        try
        {
            // Isolate navigation from timed dismissal; the existing timer checks
            // retain the real countdown and covered/uncovered behavior.
            timer.Interval = TimeSpan.FromSeconds(30);
            var handle = new WindowInteropHelper(window).EnsureHandle();
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000);
            SetParent(handle, new nint(-3));
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            Pump();
            Program.Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) &&
                           GetForegroundWindow() != handle && GetFocus() == handle,
                "Overlay navigation must use the loaded hidden Window without taking desktop foreground focus.");

            model.StartNewGameFromLobbyCommand.Execute(null);
            model.ContinueFromIdentityRevealCommand.Execute(null);
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.AdvanceToDecision(model);
            Pump();
            Program.Assert(model.IsOpeningDealVisible && model.OpeningHandCards.Count > 0,
                "The opening navigation fixture must use the real initial-card presentation.");
            var openingGame = Program.Engine(model);
            var openingViews = Views(openingGame);
            ProbeCycle("OpeningDealOverlay", (Button)window.FindName("DismissOpeningDealButton"));
            Program.Assert(model.IsOpeningDealVisible && Views(openingGame).SequenceEqual(openingViews),
                "Traversing the initial-card presentation must not dismiss it or change the match.");
            model.DismissOpeningDealCommand.Execute(null);
            var card = model.Hand.First(item => item.IsPlayable);
            model.SelectCardCommand.Execute(card);
            model.ToggleLogCommand.Execute(null);
            Pump();
            ProbeCycle("BattleLogOverlay", (Button)window.FindName("CloseBattleLogButton"));
            Program.Assert(model.IsLogOpen && card.IsSelected && Views(openingGame).SequenceEqual(openingViews),
                "Traversing the visible log must preserve the selected hand draft and every player's view.");
            model.ToggleLogCommand.Execute(null);
            model.IsMotionEnabled = false;

            // A one-turn cap reaches a real native completion without a match
            // simulation or seed search. Load its accepted command checkpoint.
            var registry = openingGame.ContentRegistry;
            var completed = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 721019, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 8,
                MaxTurns = 1, UseInteractiveSetup = false, AdvanceAfterHumanCommands = false
            }, registry);
            Program.Assert(completed.Submit(new StartGameCommand()).Accepted, "The native result fixture must start.");
            for (var step = 0; step < 24 && completed.State.Status != EngineStatus.Completed; step++)
            {
                var command = completed.PendingDecision switch
                {
                    { Kind: DecisionKind.PlayCard } prompt => (GameCommand)new EndPlayPhaseCommand(0, completed.Revision, prompt.PromptId),
                    { Kind: DecisionKind.DiscardCards } prompt => new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, completed.Revision),
                    null => new AdvanceOneStepCommand(completed.Revision),
                    var prompt => throw new InvalidOperationException($"Unexpected result-fixture prompt: {prompt.Kind}.")
                };
                Program.Assert(completed.Submit(command).Accepted, "The bounded result fixture must advance through native commands.");
            }
            Program.Assert(completed.State.Status == EngineStatus.Completed && completed.State.Winner == Winner.Draw,
                "The native fixture must reach an actual draw before inspecting the result controls.");
            saves.Write(GameSaveSlot.Manual, new GameSaveFile(1, DateTimeOffset.UtcNow, false, completed.CreateCheckpoint()));
            model.LoadManualGameCommand.Execute(null);
            Pump();
            var game = Program.Engine(model);
            var result = (FrameworkElement)window.FindName("MatchResultPanel");
            var rematch = (Button)window.FindName("ResultRematchButton");
            var summary = model.CompletedMatch;
            var commandCount = game.AcceptedCommands.Count;
            var resultViews = Views(game);
            Program.Assert(!model.HasSaveError && model.HasGameOver && result.IsVisible && summary is not null &&
                           model.MatchHistory.Count == 1 && Views(GameReplay.Restore(game.CreateCheckpoint(), registry)).SequenceEqual(resultViews),
                "The real result panel must expose its completed match, history entry and replay-compatible safe views.");
            ProbeCycle("MatchResultPanel", rematch);
            var controlEnter = TableShortcutChecks.WithControl(() => PressEnter(rematch));
            Pump();
            Program.Assert(controlEnter && model.HasGameOver && !model.IsNewGameSetupOpen &&
                           ReferenceEquals(model.CompletedMatch, summary) && game.AcceptedCommands.Count == commandCount &&
                           Views(game).SequenceEqual(resultViews),
                "Ctrl+Enter on the actual result rematch button must be consumed without starting or replacing the completed match.");
            Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "result-navigation.png"));
            var history = Program.Find<Button>(result).Single(button => ReferenceEquals(button.Command, model.OpenHistoryCommand));
            Program.Assert(PressEnter(history) && model.IsHistoryOpen, "Ordinary Enter must open the focused result history action.");
            Pump();
            model.CloseHistoryCommand.Execute(null);
            Pump();
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, history) && model.HasGameOver,
                "Closing history must restore the result action that opened it.");
            Program.Assert(PressEnter(rematch) && model.IsNewGameSetupOpen && model.HasGameOver &&
                           ReferenceEquals(Program.Engine(model), game) && Views(game).SequenceEqual(resultViews) &&
                           TableShortcutChecks.Input(window, Key.Enter, repeat: true) && model.IsNewGameSetupOpen &&
                           game.AcceptedCommands.Count == commandCount && model.MatchHistory.Count == 1,
                "Ordinary rematch Enter must open setup once and retain the completed match until a new game is configured.");
            Console.WriteLine($"Native result input: commands={commandCount}, safe viewers={resultViews.Length}, " +
                              "Ctrl+Enter consumed=True, ordinary history/rematch Enter=True, history rows=1.");
            Program.Assert(escapes.Count == 0, "Overlay keyboard navigation escaped: " + string.Join("; ", escapes));

            void ProbeCycle(string surfaceName, Button entry)
            {
                var surface = (FrameworkElement)window.FindName(surfaceName);
                foreach (var direction in new[] { FocusNavigationDirection.Next, FocusNavigationDirection.Previous })
                {
                    Program.Assert(entry.Focus(), $"{surfaceName} must focus its real entry button.");
                    Pump();
                    var wrapped = false;
                    for (var step = 0; step < 128; step++)
                    {
                        if (Keyboard.FocusedElement is not UIElement current ||
                            !current.MoveFocus(new TraversalRequest(direction))) break;
                        Pump();
                        if (Keyboard.FocusedElement is not UIElement next || !next.IsDescendantOf(surface)) break;
                        if (ReferenceEquals(next, entry)) { wrapped = true; break; }
                    }
                    Console.WriteLine($"Overlay traversal: {surfaceName}, {direction}, wrapped={wrapped}, focus={Keyboard.FocusedElement}.");
                    if (!wrapped) escapes.Add($"{surfaceName} {direction}");
                }
            }

            bool PressEnter(Button button)
            {
                Program.Assert(button.Focus() && ReferenceEquals(Keyboard.FocusedElement, button) && button.IsVisible &&
                               button.IsEnabled && button.Command!.CanExecute(button.CommandParameter) && GetFocus() == handle,
                    "Each Enter route must start on its actual enabled focused button in the hidden Window.");
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, 0, Key.Enter)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                button.RaiseEvent(args);
                if (!args.Handled)
                {
                    args.RoutedEvent = Keyboard.KeyDownEvent;
                    button.RaiseEvent(args);
                }
                return args.Handled;
            }
            void Pump()
            {
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                window.UpdateLayout();
            }
        }
        finally
        {
            timer.Interval = originalInterval;
            window.Content = null;
            window.Close();
        }
    }

    private static string[] Views(GameEngine game) => Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();

    [DllImport("user32.dll")] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
