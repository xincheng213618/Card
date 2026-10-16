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
    private static MainViewModel Create(IGameSaveStore store, bool showSetup = false) => new(false, 721019, showSetup, store);

    private static FileGameSaveStore FileStore(string output, string name) =>
        new(Path.Combine(output, "save-fixtures", $"{name}-{Guid.NewGuid():N}"));

    public static void RoundTrips(string output)
    {
        var store = FileStore(output, "round-trip");
        using var original = Create(store);
        original.IsMotionEnabled = !System.Windows.SystemParameters.ClientAreaAnimation;
        original.StartNewGameCommand.Execute(null);
        Require(Engine(original).CreateCheckpoint().Options.AiPolicyVersion == AiDecisionPolicy.CurrentVersion, "New UI games must use the current public-evidence AI.");
        var seen = new HashSet<string>();
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
                restored.LoadManualGameCommand.Execute(null);
                Require(!restored.HasSaveError && !restored.IsNewGameSetupOpen, $"{label}: {restored.SaveStatus}");
                Require(State(restored) == expected && restored.GameLog.SequenceEqual(expectedLog), $"Restored {label} state or public log differs.");
                Require(Engine(restored).CreateCheckpoint().Options.AiPolicyVersion == AiDecisionPolicy.CurrentVersion, "Restoring a new game lost its public-evidence AI policy.");
                Require(restored.RecentPlays.SequenceEqual(expectedPlays), $"Restored {label} public card display differs.");
                Require(restored.IsMotionEnabled == System.Windows.SystemParameters.ClientAreaAnimation,
                    "A restored match must preserve the current device's animation preference.");
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
        oldJson["Checkpoint"]!.AsObject().Remove("RulesVersion");
        File.WriteAllText(legacyPath, oldJson.ToJsonString());
        using var resumedLegacy = Create(store, showSetup: true);
        var oldFileText = File.ReadAllText(legacyPath);
        resumedLegacy.LoadManualGameCommand.Execute(null);
        Require(!resumedLegacy.HasSaveError &&
                Engine(resumedLegacy).RulesVersion == GameCheckpoint.CurrentRulesVersion &&
                State(resumedLegacy) == SnapshotJson.Serialize(legacy.CreateSnapshot(0, true)) &&
                File.ReadAllText(legacyPath) == oldFileText,
            "A JSON save without a rules marker replays under current rules without rewriting its source file.");
        foreach (var marker in new[] { GameCheckpoint.CurrentRulesVersion - 1, GameCheckpoint.CurrentRulesVersion + 1 })
        {
            store.Write(GameSaveSlot.Manual, store.Read(GameSaveSlot.Manual) with
                { Checkpoint = legacy.CreateCheckpoint() with { RulesVersion = marker } });
            resumedLegacy.LoadManualGameCommand.Execute(null);
            Require(!resumedLegacy.HasSaveError && State(resumedLegacy) == SnapshotJson.Serialize(legacy.CreateSnapshot(0, true)),
                "Earlier and later rules markers do not block restoring a valid save through the real UI command.");
        }
        resumedLegacy.StartNewGameCommand.Execute(null);
        Require(Engine(resumedLegacy).CreateCheckpoint().Options.AiPolicyVersion == AiDecisionPolicy.CurrentVersion, "Starting another game should opt into the current public-evidence AI.");
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
        using (var unexpected = Create(new ThrowingReadSaveStore(store), showSetup: true))
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

    private sealed class ThrowingReadSaveStore(IGameSaveStore inner) : IGameSaveStore
    {
        public DateTimeOffset? GetSavedAt(GameSaveSlot slot) => inner.GetSavedAt(slot);
        public GameSaveFile Read(GameSaveSlot slot) => throw new ArithmeticException("fixture unexpected restore failure");
        public void Write(GameSaveSlot slot, GameSaveFile save) => inner.Write(slot, save);
    }

    public static void FaultedSessionPreservesSavesAndExplainsRecovery()
    {
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, false, store,
            exceptionReportStore: new ExceptionReportChecks.IgnoredReports());
        vm.StartNewGameCommand.Execute(null);
        Step(vm);
        vm.SaveGameCommand.Execute(null);
        Require(!vm.HasSaveError && vm.FlushPendingSave(), vm.SaveStatus);
        var saved = store.Read(GameSaveSlot.Manual);
        var writes = store.WriteCount;
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
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasSaveError && !Engine(vm).IsFaulted && !ReferenceEquals(engine, Engine(vm)),
            "An existing valid save must recover the UI with a separate healthy engine.");
    }

    public static void AutomaticAndExit()
    {
        var store = new MemorySaveStore();
        using (var emptyStartup = Create(store, showSetup: true)) { }
        Require(store.WriteCount == 0, "An unopened startup game should not replace the previous autosave.");
        string expected;
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
            expected = State(vm);
            window.Close();
        }
        Require(store.WriteCount == 2, "Closing did not flush pending work.");
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
