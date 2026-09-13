using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HistoryChecks
{
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement => Program.Find<T>(root).Single(item => item.Name == name);
    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [key, ModifierKeys.None])!;

    public static void PersistenceAndFailures(string output)
    {
        var directory = Path.Combine(output, "history-fixtures", Guid.NewGuid().ToString("N"));
        var history = new FileMatchHistoryStore(directory);
        var saves = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, false, saves, historyStore: history);
        Require(vm.HasNoMatchHistory && !Directory.Exists(directory), "An unfinished game must not create a history file.");
        Complete(vm);
        Require(vm.MatchHistory.Count == 1, "Completing a real game did not record exactly one result.");
        var original = vm.MatchHistory.Single();
        Require(original.Summary.Players.SequenceEqual(vm.CompletedMatch!.Players), "Recorded statistics differ from the real settlement.");
        var expectedId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint())))).ToLowerInvariant();
        Require(original.Id == expectedId, "History was fingerprinted before the final command was journaled.");
        var path = Path.Combine(directory, original.Id + ".json");
        var bytes = File.ReadAllText(path);
        Require(!bytes.Contains("Checkpoint") && !bytes.Contains("Seed") && !bytes.Contains("CardId") && !bytes.Contains("Hand"), "History contains private engine data.");
        vm.SaveGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && vm.MatchHistory.Count == 1 && File.ReadAllText(path) == bytes, "Reloading one ending changed or duplicated its history.");

        using var reopened = new MainViewModel(false, 721020, true, saves, historyStore: new FileMatchHistoryStore(directory));
        Require(reopened.MatchHistory.Count == 1 && !reopened.HasGameOver && reopened.IsNewGameSetupOpen, "History must survive relaunch independently of the active save.");
        reopened.StartNewGameCommand.Execute(null);
        Require(reopened.MatchHistory.Count == 1, "Starting a new match erased the history.");
        Complete(reopened);
        Require(reopened.MatchHistory.Count == 2 && reopened.MatchHistory.Select(entry => entry.Id).Distinct().Count() == 2, "A different completed game was not recorded separately.");
        var window = new MainWindow(reopened);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "50-history-settlement.png"));
        reopened.OpenHistoryCommand.Execute(null);
        Program.Render(root, 1120, 740, Path.Combine(output, "50-history-results.png"));
        var before = State(reopened);
        Named<ListBox>(root, "HistoryList").SelectedItem = reopened.MatchHistory.Last();
        Program.Render(root, 1440, 860, Path.Combine(output, "51-history-selected.png"));
        Require(reopened.SelectedHistoryEntry!.Id == original.Id && State(reopened) == before, "Browsing an old result changed the active game.");
        Require(Named<ItemsControl>(root, "HistoryPlayers").Items.Count == original.Summary.Players.Count, "History table omitted players.");

        var damaged = Path.Combine(directory, new string('f', 64) + ".json");
        File.WriteAllText(damaged, "{ broken");
        reopened.RefreshHistoryCommand.Execute(null);
        Require(reopened.MatchHistory.Count == 2 && reopened.HistoryStatus.Contains("1 条战绩无法读取") && File.ReadAllText(damaged) == "{ broken", "One damaged result must not hide or alter healthy files.");
        Program.Render(root, 1120, 740, Path.Combine(output, "52-history-read-warning.png"));
        var unsupported = Path.Combine(directory, new string('e', 64) + ".json");
        File.WriteAllText(unsupported, System.Text.Json.JsonSerializer.Serialize(original with { FormatVersion = 99, Id = new string('e', 64) }));
        reopened.RefreshHistoryCommand.Execute(null);
        Require(reopened.MatchHistory.Count == 2 && reopened.HistoryStatus.Contains("2 条战绩无法读取"),
            "A syntactically valid but unsupported history record escaped the recoverable error boundary.");
        window.Content = null;
        window.Close();

        var failing = new FailingHistoryStore();
        using var retry = new MainViewModel(false, 721019, true, saves, historyStore: failing);
        retry.LoadManualGameCommand.Execute(null);
        Require(retry.HasGameOver && !retry.HasSaveError && retry.HistoryStatus.Contains("尚未写入"), "History failure interrupted loading a completed save.");
        retry.StartNewGameCommand.Execute(null);
        failing.Fail = false;
        retry.RefreshHistoryCommand.Execute(null);
        retry.RefreshHistoryCommand.Execute(null);
        Require(retry.MatchHistory.Count == 1 && !retry.HasGameOver, "Retry lost or duplicated a pending result after starting another game.");

        // Validate retention ordering independently of file timestamps, with public summary fixtures.
        var retention = new FileMatchHistoryStore(Path.Combine(directory, "retention"));
        for (var i = 0; i < 55; i++)
            retention.Record(original with { Id = i.ToString("x64"), RecordedAtUtc = original.RecordedAtUtc.AddMinutes(i), Outcome = (MatchOutcome)(i % 3) });
        var recent = retention.Read().Entries;
        Require(recent.Count == 50 && recent[0].Id == 54.ToString("x64") && recent[^1].Id == 5.ToString("x64") &&
            Directory.GetFiles(Path.Combine(directory, "retention"), "*.json").Length == 55, "Recent-50 view deleted files or sorted by file timestamps.");
        Require(recent.Select(entry => entry.Outcome).Distinct().Count() == 3, "Serialized win/loss/draw outcomes did not round-trip.");
        Console.WriteLine("  Two real completed matches persisted; reload deduplication, pending retry, corrupt-file isolation and 55-file retention passed.");
    }

    public static void ModalLifecycle(string output)
    {
        using var vm = new MainViewModel(false, 721019, true, new MemorySaveStore());
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        // Materialize the startup view before opening its modal, matching the player's actual path.
        Program.Render(root, 1120, 740, Path.Combine(output, "49-history-startup.png"));
        var entryButton = Named<Button>(root, "SetupHistoryButton");
        Require(entryButton.IsEnabled && entryButton.Command is not null, "Startup history entry is inaccessible.");
        entryButton.Command!.Execute(null);
        Program.Render(root, 1120, 740, Path.Combine(output, "49-history-empty.png"));
        VerifyCloseBounds(root);
        Require(!Named<Grid>(root, "TableSurface").IsEnabled && !Named<Border>(root, "NewGameSetupPanel").IsEnabled &&
            Named<Button>(root, "HistoryCloseButton").IsEnabled, "History modal failed to isolate the setup/table controls.");
        Require(KeyboardNavigation.GetTabNavigation(Program.Find<MatchHistoryPanel>(root).Single()) == KeyboardNavigationMode.Cycle, "History must contain Tab navigation.");
        Require(Shortcut(window, Key.Escape) && !vm.IsHistoryOpen && vm.IsNewGameSetupOpen, "Esc did not return to the original setup.");
        vm.StartNewGameCommand.Execute(null);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Require(vm.CanStepAi, "History timer fixture has no AI continuation.");
        vm.IsAutoAdvance = true;
        vm.OpenHistoryCommand.Execute(null);
        var paused = State(vm);
        Pump(1400);
        Require(State(vm) == paused && vm.IsAutoAdvance, "History failed to pause the enabled AI timer.");
        vm.CloseHistoryCommand.Execute(null);
        Pump(750);
        Require(State(vm) != paused && vm.IsAutoAdvance, "Returning from history failed to resume the timer.");
        vm.IsAutoAdvance = false;
        Program.AdvanceToDecision(vm);
        var slash = vm.Hand.First(card => card.Name == "杀" && card.IsPlayable);
        vm.SelectCardCommand.Execute(slash);
        vm.SelectTargetCommand.Execute(vm.Seats.First(seat => seat.IsLegalTarget));
        Require(vm.CanConfirmSelected, "Fixture needs a selected legal attack.");
        var selected = State(vm);
        vm.OpenHistoryCommand.Execute(null);
        Program.Render(root, 1120, 740, Path.Combine(output, "53-history-return.png"));
        VerifyCloseBounds(root);
        Require(!Named<Grid>(root, "TableSurface").IsEnabled && !Shortcut(window, Key.Enter) && !Shortcut(window, Key.F5) && !Shortcut(window, Key.F1), "History lets game shortcuts pass through.");
        Require(Shortcut(window, Key.Escape) && !vm.IsHistoryOpen && vm.CanConfirmSelected && slash.IsSelected && State(vm) == selected && !vm.IsAutoAdvance, "Closing history changed the selected play or pause preference.");
        Require(Shortcut(window, Key.Enter) && State(vm) != selected, "Preserved attack failed to submit after closing history.");
        vm.StartTutorialCommand.Execute(null);
        Require(vm.IsTutorialActive && vm.HasNoMatchHistory, "Tutorial entry must not create a result.");
        window.Content = null;
        window.Close();
    }

    private static void Complete(MainViewModel vm)
    {
        for (var step = 0; step < 18000 && !vm.HasGameOver; step++) PersistenceChecks.Step(vm);
        Require(vm.HasGameOver, "Real history fixture did not complete.");
    }

    private static void VerifyCloseBounds(FrameworkElement root)
    {
        var button = Named<Button>(root, "HistoryCloseButton");
        var origin = button.TranslatePoint(new Point(), root);
        Require(button.ActualWidth > 0 && button.ActualHeight > 0 && origin.X >= 0 && origin.Y >= 0 &&
            origin.X + button.ActualWidth <= root.ActualWidth && origin.Y + button.ActualHeight <= root.ActualHeight,
            $"History close button is clipped: {origin}, {button.ActualWidth}x{button.ActualHeight}, root {root.ActualWidth}x{root.ActualHeight}.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class FailingHistoryStore : IMatchHistoryStore
    {
        private readonly MemoryMatchHistoryStore _store = new();
        public bool Fail { get; set; } = true;
        public MatchHistoryLoad Read() => _store.Read();
        public void Record(MatchHistoryEntry entry)
        {
            if (Fail) throw new IOException("Fixture write failure.");
            _store.Record(entry);
        }
    }
}
