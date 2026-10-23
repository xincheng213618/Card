using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PersistenceChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static GameEngine Engine(MainViewModel vm) => (GameEngine)typeof(MainViewModel)
        .GetField("_game", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
    private static MainViewModel Create(IGameSaveStore store, bool showSetup = false) =>
        new(false, 721019, showSetup, store, wushuangSaveStore: new MemorySaveStore());

    private static FileGameSaveStore FileStore(string output, string name) =>
        new(Path.Combine(output, "save-fixtures", $"{name}-{Guid.NewGuid():N}"));

    public static void RoundTrips(string output)
    {
        var store = FileStore(output, "round-trip");
        using var original = Create(store);
        original.IsMotionEnabled = false;
        original.StartNewGameCommand.Execute(null);
        Require(Engine(original).CreateCheckpoint().Options.AiPolicyVersion == AiDecisionPolicy.CurrentVersion, "New UI games must use the current public-evidence AI.");
        var seen = new HashSet<string>();
        var restoredMotionPreferences = new HashSet<bool>();
        for (var step = 0; step < 6000; step++)
        {
            var engine = Engine(original);
            var label = original.HasGameOver ? "Completed" : engine.PendingDecision?.Kind.ToString()
                ?? (engine.ResolutionStack.Count > 0 ? "InFlight" : "Running");
            if (seen.Add(label))
            {
                original.SaveGameCommand.Execute(null);
                Require(!original.HasSaveError && original.FlushPendingSave(), original.SaveStatus);
                var expected = State(original);
                var expectedLog = original.GameLog.ToArray();
                var expectedPlays = original.RecentPlays.ToArray();
                var savedText = File.ReadAllText(store.GetPath(GameSaveSlot.Manual));
                using var restored = Create(store, showSetup: true);
                Require(restored.HasAutomaticSave && restored.HasManualSave, "Startup did not discover both save slots.");
                Require(File.ReadAllText(store.GetPath(GameSaveSlot.Manual)) == savedText, "Constructing a new view overwrote a save.");
                // Device preferences are independent of Windows' system animation
                // setting and the source match. Cover both values without another match.
                var expectedMotion = seen.Count % 2 != 0;
                restored.IsMotionEnabled = expectedMotion;
                restored.LoadManualGameCommand.Execute(null);
                Require(!restored.HasSaveError && !restored.IsNewGameSetupOpen, $"{label}: {restored.SaveStatus}");
                Require(State(restored) == expected && restored.GameLog.SequenceEqual(expectedLog), $"Restored {label} state or public log differs.");
                Require(Engine(restored).CreateCheckpoint().Options.AiPolicyVersion == AiDecisionPolicy.CurrentVersion, "Restoring a new game lost its public-evidence AI policy.");
                Require(restored.RecentPlays.SequenceEqual(expectedPlays), $"Restored {label} public card display differs.");
                Require(restored.IsMotionEnabled == expectedMotion,
                    "A restored match must preserve the current device's animation preference.");
                restoredMotionPreferences.Add(expectedMotion);
                Require(restored.Hand.All(card => !card.IsSelected) && restored.SelectedDiscardCount == 0, "Restore retained stale visual selection.");
                if (!original.HasGameOver)
                {
                    Step(original);
                    Step(restored);
                    Require(State(original) == State(restored), $"Restored {label} continuation diverged.");
                }
                Require(File.ReadAllText(store.GetPath(GameSaveSlot.Manual)) == savedText, "Autosave changed the manual slot.");
            }
            else if (!original.HasGameOver) Step(original);

            if (original.HasGameOver) break;
            if (seen.Contains("SelectGeneral") && seen.Contains("PlayCard") && seen.Contains("DiscardCards") &&
                seen.Contains("InFlight") && seen.Count(label => label is not ("SelectGeneral" or "PlayCard" or "DiscardCards" or "Running" or "InFlight")) >= 2) break;
        }
        Require(seen.Contains("SelectGeneral") && seen.Contains("PlayCard") && seen.Contains("DiscardCards") && seen.Contains("InFlight"), "Round-trip coverage missed a required game boundary.");
        Require(seen.Any(label => label is "RespondDodge" or "RespondSlash" or "SelectHarvestCard" or "RescueDying"), "Round-trip coverage missed an actual response.");
        Require(restoredMotionPreferences.SetEquals([false, true]), "Round-trip coverage must preserve both device animation preferences.");
        Console.WriteLine($"  Restored and continued: {string.Join(", ", seen)}.");

        var legacy = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 721019,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 1
        }, CardGame.Content.Standard.StandardContentRegistry.Create());
        Require(legacy.Submit(new StartGameCommand()).Accepted, "Legacy UI fixture failed to start.");
        store.Write(GameSaveSlot.Manual, store.Read(GameSaveSlot.Manual) with { Checkpoint = legacy.CreateCheckpoint() });
        var legacyPath = store.GetPath(GameSaveSlot.Manual);
        var oldJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(legacyPath))!;
        oldJson["Checkpoint"]!["Options"]!.AsObject().Remove("AiPolicyVersion");
        oldJson["Checkpoint"]!["RulesVersion"] = int.MaxValue;
        File.WriteAllText(legacyPath, oldJson.ToJsonString());
        using var resumedLegacy = Create(store, showSetup: true);
        var oldFileText = File.ReadAllText(legacyPath);
        resumedLegacy.LoadManualGameCommand.Execute(null);
        Require(!resumedLegacy.HasSaveError &&
                State(resumedLegacy) == SnapshotJson.Serialize(legacy.CreateSnapshot(0, true)) &&
                File.ReadAllText(legacyPath) == oldFileText,
            "Legacy JSON fields do not block replay or rewrite the source file.");
        resumedLegacy.SaveGameCommand.Execute(null);
        Require(!resumedLegacy.HasSaveError &&
                !System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(legacyPath))!["Checkpoint"]!.AsObject().ContainsKey("RulesVersion"),
            "Saving the restored match must omit the removed rules epoch.");
        resumedLegacy.StartNewGameCommand.Execute(null);
        Require(Engine(resumedLegacy).CreateCheckpoint().Options.AiPolicyVersion == AiDecisionPolicy.CurrentVersion, "Starting another game should opt into the current public-evidence AI.");

        var historyDirectory = Path.Combine(output, "versionless-history");
        var historyStore = new FileMatchHistoryStore(historyDirectory);
        var historyEntry = new MatchHistoryEntry(1, new string('c', 64), DateTimeOffset.UtcNow, "存档夹具", MatchOutcome.Win,
            new CardGame.Wpf.Presentation.MatchSummary(1,
                [new CardGame.Wpf.Presentation.PlayerMatchResult(0, "战绩夹具", "主公", true, true, 1, 0, 0, 0, 0, 0, 0)]));
        var historyPath = Path.Combine(historyDirectory, historyEntry.Id + ".json");
        if (File.Exists(historyPath)) File.Delete(historyPath);
        historyStore.Record(historyEntry);
        var historyJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(historyPath))!.AsObject();
        Require(!historyJson.ContainsKey("RulesVersion"), "New history records must omit the removed rules epoch.");
        historyJson["RulesVersion"] = int.MaxValue;
        File.WriteAllText(historyPath, historyJson.ToJsonString());
        var history = historyStore.Read();
        Require(history.UnreadableCount == 0 && history.Entries.Single().Id == historyEntry.Id,
            "Old history JSON retains its identity and remains readable after removing the rules field.");
    }

    public static void FailedWritesPreserveSavesAndFailedLoadsInitializeGame(string output)
    {
        var store = FileStore(output, "failure");
        using var vm = Create(store);
        vm.StartNewGameCommand.Execute(null);
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError && vm.FlushPendingSave(), vm.SaveStatus);
        var manualPath = store.GetPath(GameSaveSlot.Manual);
        var initial = File.ReadAllText(manualPath);
        Step(vm);
        using (var locked = new FileStream(manualPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            vm.SaveGameCommand.Execute(null);
            Require(vm.HasSaveError, "A locked save should report the write failure.");
            Require(File.ReadAllText(manualPath) == initial, "A failed replacement damaged the old save.");
        }
        Require(!Directory.EnumerateFiles(Path.GetDirectoryName(manualPath)!, "*.tmp").Any(), "Failed write left temporary files.");
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError && File.ReadAllText(manualPath + ".previous") == initial, "Save retry or previous-file retention failed.");
        var goodSave = store.Read(GameSaveSlot.Manual);
        vm.SelectedTableMode = vm.TableModes.Single(mode => mode.PlayerCount == 5);
        vm.StartNewGameCommand.Execute(null);
        var automatic = File.ReadAllText(store.GetPath(GameSaveSlot.Automatic));

        File.WriteAllText(manualPath, "{ incomplete json");
        CheckInitializedGame();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { SchemaVersion = 999 } });
        CheckInitializedGame();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { Options = goodSave.Checkpoint.Options with { ModeId = "missing:mode" } } });
        CheckInitializedGame();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { Options = goodSave.Checkpoint.Options with { AiPolicyVersion = 99 } } });
        CheckInitializedGame();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { ContentHash = "invalid-content" } });
        CheckInitializedGame();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with
        {
            Commands = goodSave.Checkpoint.Commands.Select((command, index) =>
                index == 0 ? new AdvanceOneStepCommand(999) : command).ToArray()
        } });
        CheckInitializedGame();
        File.Delete(manualPath);
        CheckInitializedGame();
        Require(File.ReadAllText(store.GetPath(GameSaveSlot.Automatic)) == automatic, "A failed manual load changed the automatic slot.");
        var errorText = vm.SaveStatus;
        Require(vm.FlushPendingSave() && vm.HasSaveError && vm.SaveStatus == errorText,
            "Initializing after failed load must not queue an autosave or erase its explanation.");

        var automaticPath = store.GetPath(GameSaveSlot.Automatic);
        File.WriteAllText(automaticPath, "{ invalid automatic save");
        using (var startup = Create(store, showSetup: true))
        {
            var previous = Engine(startup);
            startup.ContinueGameCommand.Execute(null);
            Require(startup.HasSaveError && !ReferenceEquals(previous, Engine(startup)) &&
                !Engine(startup).IsFaulted && !startup.IsNewGameSetupOpen && !startup.IsAutoAdvance &&
                Engine(startup).AcceptedCommands is [StartGameCommand] && startup.FlushPendingSave(),
                "A failed continue from startup initializes a healthy fresh match without starting autosave.");
        }
        Require(File.ReadAllText(automaticPath) == "{ invalid automatic save",
            "Initializing and closing after a failed continue must preserve the unreadable autosave.");
        using (var unexpected = Create(new SwitchingSaveStore(store) { FailReads = true }, showSetup: true))
        {
            var previous = Engine(unexpected);
            unexpected.ContinueGameCommand.Execute(null);
            Require(unexpected.HasSaveError && !ReferenceEquals(previous, Engine(unexpected)) &&
                !Engine(unexpected).IsFaulted && unexpected.SaveStatus.Contains("已初始化新牌局"),
                "Unexpected exceptions during loading also initialize a healthy game.");
        }
        Step(vm);
        Require(Engine(vm).AcceptedCommands.Count > 1 && vm.FlushPendingSave() &&
            File.ReadAllText(automaticPath + ".previous") == "{ invalid automatic save",
            "The initialized match can advance and autosave normally after a real user action, retaining the previous file.");

        void CheckInitializedGame()
        {
            var previous = Engine(vm);
            var originalFile = File.Exists(manualPath) ? File.ReadAllText(manualPath) : null;
            vm.IsAutoAdvance = true;
            vm.LoadManualGameCommand.Execute(null);
            Require(vm.HasSaveError && !ReferenceEquals(Engine(vm), previous) && !Engine(vm).IsFaulted &&
                Engine(vm).AcceptedCommands is [StartGameCommand] && !vm.IsAutoAdvance && !vm.IsNewGameSetupOpen &&
                vm.SaveStatus.Contains("已初始化新牌局") && vm.FlushPendingSave(),
                "A damaged, incompatible or unreplayable save initializes a fresh paused match.");
            Require((File.Exists(manualPath) ? File.ReadAllText(manualPath) : null) == originalFile &&
                File.ReadAllText(store.GetPath(GameSaveSlot.Automatic)) == automatic,
                "Initializing a new match must preserve the failed source file and automatic slot.");
        }
    }

    private sealed class SwitchingSaveStore(IGameSaveStore inner) : IGameSaveStore
    {
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public DateTimeOffset? GetSavedAt(GameSaveSlot slot) => inner.GetSavedAt(slot);
        public GameSaveFile Read(GameSaveSlot slot)
        {
            ReadCount++;
            return FailReads ? throw new ArithmeticException("fixture unexpected restore failure") : inner.Read(slot);
        }
        public void Write(GameSaveSlot slot, GameSaveFile save)
        {
            if (FailWrites) throw new IOException("fixture automatic save failure");
            inner.Write(slot, save);
            WriteCount++;
        }
    }

    public static void FaultedSessionPreservesSavesAndExplainsRecovery()
    {
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, false, store,
            exceptionReportStore: new ExceptionReportChecks.IgnoredReports(), wushuangSaveStore: new MemorySaveStore());
        vm.StartNewGameCommand.Execute(null);
        Step(vm);
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError && vm.FlushPendingSave(), vm.SaveStatus);
        var saved = store.Read(GameSaveSlot.Manual);
        var savedState = State(vm);
        Require(vm.HasAutomaticSave, "The fault-recovery fixture must retain a valid automatic save.");
        var writes = store.WriteCount;
        Step(vm);
        var engine = Engine(vm);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Action fail = () => typeof(GameEngine).GetMethod("ExecuteExclusive", flags)!.Invoke(engine,
            [new Action(() => throw new InvalidOperationException("fixture internal rule failure"))]);
        typeof(MainViewModel).GetMethod("ExecuteSafely", flags)!.Invoke(vm, [fail]);
        Require(engine.IsFaulted && engine.LastTrustedCheckpoint is not null && !vm.IsAutoAdvance && !vm.CanStepAi &&
            !vm.StepAiCommand.CanExecute(null) && vm.PromptText.Contains("已停止推进") && vm.ActionHint.Contains("读取此前存档"),
            "A rules failure must stop advancement and expose a readable recovery path.");
        vm.SaveGameCommand.Execute(null);
        Require(vm.HasSaveError && vm.SaveStatus.Contains("结算异常") && vm.SaveStatus.Contains("已有存档未被覆盖") &&
            !vm.SaveStatus.Contains("不兼容") && !vm.SaveStatus.Contains("仍可继续") && store.WriteCount == writes &&
            ReferenceEquals(saved, store.Read(GameSaveSlot.Manual)),
            "A faulted session must preserve saves and must not be described as incompatible or still playable.");
        vm.ContinueGameCommand.Execute(null);
        Require(!vm.HasSaveError && !Engine(vm).IsFaulted && !ReferenceEquals(engine, Engine(vm)) && State(vm) == savedState,
            "Explicit automatic recovery must still read the trusted save when the faulted live session cannot flush its pending work.");
        engine = Engine(vm);
        typeof(MainViewModel).GetMethod("ExecuteSafely", flags)!.Invoke(vm, [fail]);
        Require(engine.IsFaulted, "The retained manual-recovery check must begin from a faulted restored session.");
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && !Engine(vm).IsFaulted && !ReferenceEquals(engine, Engine(vm)),
            "An existing valid save must recover the UI with a separate healthy engine.");
    }

    public static void AutomaticAndExit()
    {
        var memory = new MemorySaveStore();
        var store = new SwitchingSaveStore(memory);
        using (var emptyStartup = Create(store, showSetup: true)) { }
        Require(store.WriteCount == 0, "An unopened startup game should not replace the previous autosave.");
        string expected;
        int writesBeforeClose;
        using (var vm = Create(store))
        {
            var window = new MainWindow(vm);
            vm.StartNewGameCommand.Execute(null);
            Step(vm);
            Step(vm);
            Require(store.WriteCount == 0, "Autosave should coalesce rapid engine steps.");
            var commandsBeforeAuto = Engine(vm).CreateCheckpoint().Commands.Count;
            vm.IsAutoAdvance = true;
            Pump(TimeSpan.FromMilliseconds(1150));
            Require(store.WriteCount == 1 && vm.HasAutomaticSave && !vm.HasSaveError,
                "Automatic play should not starve autosave.");
            // Faster playback may already be waiting for the human. Verify saved
            // progress instead of requiring the AI to remain busy for 1.15 seconds.
            var savedCommands = store.Read(GameSaveSlot.Automatic).Checkpoint.Commands.Count;
            Require(savedCommands > commandsBeforeAuto && savedCommands <= Engine(vm).CreateCheckpoint().Commands.Count,
                "Autosave did not include the automatically committed progress.");
            vm.IsAutoAdvance = false;
            Step(vm);
            var pendingGame = Engine(vm);
            var pendingViews = SafeViews();
            var pendingJournal = CommandJson.Serialize(pendingGame.AcceptedCommands);
            Require(pendingGame.AcceptedCommands.Count > savedCommands,
                "The lobby continue fixture must have a real accepted action beyond the existing autosave.");
            vm.NewGameCommand.Execute(null);
            vm.ContinueGameCommand.Execute(null);
            Console.WriteLine($"Lobby continue pending progress: saved commands={savedCommands}, " +
                $"pending={pendingGame.AcceptedCommands.Count}, restored={Engine(vm).AcceptedCommands.Count}, writes={store.WriteCount}.");
            Require(!vm.IsNewGameSetupOpen && !vm.HasSaveError && SafeViews().SequenceEqual(pendingViews) &&
                CommandJson.Serialize(Engine(vm).AcceptedCommands) == pendingJournal,
                "Continuing immediately from the lobby must preserve every committed player view and command before the one-second autosave tick.");

            Step(vm);
            pendingGame = Engine(vm);
            pendingViews = SafeViews();
            pendingJournal = CommandJson.Serialize(pendingGame.AcceptedCommands);
            var savedBeforeFailure = memory.Read(GameSaveSlot.Automatic);
            var readsBeforeFailure = store.ReadCount;
            store.FailWrites = true;
            try
            {
                vm.NewGameCommand.Execute(null);
                vm.ContinueGameCommand.Execute(null);
                Require(vm.IsNewGameSetupOpen && vm.HasSaveError && vm.SaveStatus.Contains("保存失败") &&
                    ReferenceEquals(Engine(vm), pendingGame) && SafeViews().SequenceEqual(pendingViews) &&
                    CommandJson.Serialize(pendingGame.AcceptedCommands) == pendingJournal && store.ReadCount == readsBeforeFailure &&
                    ReferenceEquals(memory.Read(GameSaveSlot.Automatic), savedBeforeFailure),
                    "A failed pre-continue flush must leave the lobby and exact live progress intact without reading or replacing the older autosave.");
            }
            finally { store.FailWrites = false; }
            vm.ContinueGameCommand.Execute(null);
            Require(!vm.IsNewGameSetupOpen && !vm.HasSaveError && SafeViews().SequenceEqual(pendingViews) &&
                CommandJson.Serialize(Engine(vm).AcceptedCommands) == pendingJournal,
                "Retrying automatic continue must save and restore the retained committed progress after storage recovers.");
            Step(vm);
            expected = State(vm);
            writesBeforeClose = store.WriteCount;
            window.Close();

            string[] SafeViews() => Enumerable.Range(0, Engine(vm).State.Players.Count)
                .Select(seat => SnapshotJson.Serialize(Engine(vm).CreateSnapshot(seat))).ToArray();
        }
        Require(store.WriteCount == writesBeforeClose + 1, "Closing did not flush pending work.");
        using var next = Create(store, showSetup: true);
        Require(next.HasAutomaticSave, "Next launch did not find autosave.");
        next.ContinueGameCommand.Execute(null);
        Require(!next.HasSaveError && State(next) == expected, "Continue after close did not restore the last committed action.");
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    internal static void Step(MainViewModel vm)
    {
        if (vm.IsGeneralSelectionPending) vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        else if (vm.IsDiscardSelectionPending)
        {
            vm.RecommendDiscardCommand.Execute(null);
            vm.ConfirmSelectedCommand.Execute(null);
        }
        else if (vm.CanStepAi) vm.StepAiCommand.Execute(null);
        else if (vm.CanEndTurn) vm.EndTurnCommand.Execute(null);
        else
        {
            var choices = new (IEnumerable<PromptChoice>, ICommand)[]
            {
                (vm.ResponseChoices, vm.SelectResponseChoiceCommand), (vm.DyingChoices, vm.SelectDyingChoiceCommand),
                (vm.HarvestChoices, vm.SelectHarvestChoiceCommand), (vm.TargetCardChoices, vm.SelectTargetCardChoiceCommand),
                (vm.FireAttackChoices, vm.SelectFireAttackChoiceCommand),
                (vm.NullificationChoices, vm.SelectNullificationChoiceCommand), (vm.SkillChoices, vm.SelectSkillChoiceCommand)
            };
            var found = choices.FirstOrDefault(pair => pair.Item1.Any());
            Require(found.Item1 is not null, "UI has no available continuation.");
            found.Item2.Execute(found.Item1!.First());
        }
        Require(!vm.PromptText.Contains("未执行"), vm.PromptText);
    }
}
