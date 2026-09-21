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
        Require(Engine(original).CreateCheckpoint().Options.AiPolicyVersion == 3, "New UI games must use the current public-evidence AI.");
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
                Require(Engine(restored).CreateCheckpoint().Options.AiPolicyVersion == 3, "Restoring a new game lost its public-evidence AI policy.");
                Require(restored.RecentPlays.SequenceEqual(expectedPlays), $"Restored {label} public card display differs.");
                Require(restored.IsMotionEnabled == original.IsMotionEnabled, "Animation preference did not survive JSON save and restore.");
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
        var stateBeforeRejectedLoad = State(resumedLegacy);
        var oldFileText = File.ReadAllText(legacyPath);
        resumedLegacy.LoadManualGameCommand.Execute(null);
        Require(resumedLegacy.HasSaveError &&
                Engine(resumedLegacy).RulesVersion == GameCheckpoint.CurrentRulesVersion &&
                State(resumedLegacy) == stateBeforeRejectedLoad &&
                File.ReadAllText(legacyPath) == oldFileText,
            "The exact-rules loader accepted a JSON save without a rules marker, changed the active game, or rewrote the protected file.");
        resumedLegacy.StartNewGameCommand.Execute(null);
        Require(Engine(resumedLegacy).CreateCheckpoint().Options.AiPolicyVersion == 3, "Starting another game should opt into the current public-evidence AI.");
    }

    public static void FailedFilesPreserveGame(string output)
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
        var currentEngine = Engine(vm);
        var currentState = State(vm);
        var currentLog = vm.GameLog.ToArray();
        var automatic = File.ReadAllText(store.GetPath(GameSaveSlot.Automatic));

        File.WriteAllText(manualPath, "{ incomplete json");
        CheckRejectedLoad();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { SchemaVersion = 999 } });
        CheckRejectedLoad();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { Options = goodSave.Checkpoint.Options with { ModeId = "missing:mode" } } });
        CheckRejectedLoad();
        store.Write(GameSaveSlot.Manual, goodSave with { Checkpoint = goodSave.Checkpoint with { Options = goodSave.Checkpoint.Options with { AiPolicyVersion = 99 } } });
        CheckRejectedLoad();
        File.Delete(manualPath);
        CheckRejectedLoad();
        Require(File.ReadAllText(store.GetPath(GameSaveSlot.Automatic)) == automatic, "A failed manual load changed the automatic slot.");
        var errorText = vm.SaveStatus;
        Require(vm.FlushPendingSave() && vm.HasSaveError && vm.SaveStatus == errorText, "Background autosave should not erase a failed-load explanation.");

        void CheckRejectedLoad()
        {
            vm.LoadManualGameCommand.Execute(null);
            Require(vm.HasSaveError && ReferenceEquals(Engine(vm), currentEngine) && State(vm) == currentState && vm.GameLog.SequenceEqual(currentLog),
                "A damaged or incompatible save replaced the active game.");
        }
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
            vm.IsAutoAdvance = true;
            Pump(TimeSpan.FromMilliseconds(1150));
            Require(store.WriteCount == 1 && vm.CanStepAi && vm.HasAutomaticSave && !vm.HasSaveError, "Continuous AI should not starve autosave.");
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
