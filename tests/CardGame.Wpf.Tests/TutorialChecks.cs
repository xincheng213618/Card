using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Training;
using CardGame.Wpf.ViewModels;

internal static class TutorialChecks
{
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));

    public static void RealScenarios()
    {
        foreach (var lesson in TutorialScenario.Lessons)
        {
            var first = TutorialScenario.Create(lesson);
            var second = TutorialScenario.Create(lesson);
            Require(SnapshotJson.Serialize(first.CreateSnapshot(0, true)) == SnapshotJson.Serialize(second.CreateSnapshot(0, true)),
                $"{lesson.Action} practice setup is not deterministic.");
            Require(first.AcceptedCommands.Count == first.Revision, $"{lesson.Action} practice bypassed the command boundary.");
            var restored = GameReplay.Restore(first.CreateCheckpoint(), StandardContentRegistry.Create());
            Require(SnapshotJson.Serialize(first.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
                $"{lesson.Action} practice cannot be rebuilt from its command journal.");
        }
    }

    public static void CompleteCourseAndRestore(string output)
    {
        var saves = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: saves) { IsMotionEnabled = false };
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var originalSlash = vm.Hand.First(card => card.Name == "杀" && card.IsPlayable);
        vm.SelectCardCommand.Execute(originalSlash);
        var originalTarget = vm.Seats.First(seat => seat.IsLegalTarget);
        vm.SelectTargetCommand.Execute(originalTarget);
        var originalState = State(vm);
        var originalRevision = Program.Engine(vm).Revision;
        var originalPlayable = vm.Hand.Where(card => card.IsPlayable).Select(card => card.Id).Order().ToArray();

        vm.StartTutorialCommand.Execute(null);
        Require(vm.IsTutorialActive && vm.TutorialTitle == "使用杀" && !vm.IsNewGameSetupOpen, "Tutorial did not enter its first real position.");
        Require(vm.Hand.Any(card => card.Name == "杀" && card.IsPlayable) && vm.Hand.Where(card => card.IsPlayable).All(card => card.Name == "杀"),
            "Attack lesson should highlight only the required physical Slash.");
        Require(saves.WriteCount == 1 && saves.Read(GameSaveSlot.Automatic).Checkpoint.Revision == originalRevision,
            "Entering practice must flush the suspended match once.");
        var writesAtEntry = saves.WriteCount;
        Require(!vm.SaveGameCommand.CanExecute(null) && !vm.NewGameCommand.CanExecute(null) && !vm.ToggleLogCommand.CanExecute(null),
            "Practice must disable operations that could replace or save its temporary game.");

        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "27-tutorial-attack.png"));
        Require(Program.Find<Grid>(root).Single(grid => grid.Name == "TutorialActionPanel").Visibility == Visibility.Visible,
            "Actual tutorial strip is not visible in the table.");

        var beforeWrongAction = State(vm);
        vm.EndTurnCommand.Execute(null);
        Require(State(vm) == beforeWrongAction && vm.TutorialInstruction.Contains("这一节先练习"),
            "An unrelated action escaped the lesson boundary.");
        CompleteAttack(vm);
        Require(vm.IsTutorialStepComplete && !vm.IsTutorialBoardEnabled, "Attack lesson did not recognize an actual Slash declaration.");
        var completedAttack = State(vm);
        vm.StepAiCommand.Execute(null);
        Require(State(vm) == completedAttack, "A completed lesson continued behind its result prompt.");
        Program.Render(root, 1120, 740, Path.Combine(output, "28-tutorial-step-complete.png"));

        vm.NextTutorialStepCommand.Execute(null);
        Require(vm.TutorialTitle == "用闪保护自己" && Program.Engine(vm).PendingDecision?.Kind == DecisionKind.RespondDodge,
            "Dodge lesson did not open at a real response prompt.");
        var dodge = FindResponseCard(vm, CardKind.Dodge);
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == dodge.Cards[0]));
        Require(vm.CanConfirmSelected && vm.PlayButtonText == "打出闪", "Dodge lesson cannot use the main hand confirmation.");
        vm.ConfirmSelectedCommand.Execute(null);
        Require(vm.IsTutorialStepComplete, "Dodge lesson did not recognize the committed response.");

        vm.NextTutorialStepCommand.Execute(null);
        Require(vm.TutorialTitle == "用桃回复体力", "Recovery lesson was skipped.");
        var injured = Program.Engine(vm).State.Players.Single(player => player.IsHuman);
        Require(injured.Hp < injured.MaxHp, "Recovery lesson is not a real injured state.");
        Require(vm.Hand.Where(card => card.IsPlayable).All(card => card.Name == "桃"), "Recovery lesson should highlight only Peach.");
        var peach = vm.Hand.First(card => card.Name == "桃" && card.IsPlayable);
        vm.SelectCardCommand.Execute(peach);
        Require(vm.CanConfirmSelected, "Recovery lesson did not expose the real self-target action.");
        vm.ConfirmSelectedCommand.Execute(null);
        Require(vm.IsTutorialStepComplete, "Recovery lesson did not recognize the actual recovery event.");

        vm.NextTutorialStepCommand.Execute(null);
        Require(vm.TutorialTitle == "决定留下哪些牌" && vm.RequiredDiscardCount >= 2, "Discard lesson is not a real hand-limit prompt.");
        foreach (var card in vm.Hand.Take(vm.RequiredDiscardCount).ToArray()) vm.SelectCardCommand.Execute(card);
        Require(vm.CanConfirmSelected, "Discard lesson did not accept an exact subset.");
        vm.ConfirmSelectedCommand.Execute(null);
        Require(vm.IsTutorialCourseComplete && vm.TutorialTitle == "新手演练完成", "The four-lesson course did not reach completion.");
        Program.Render(root, 1120, 740, Path.Combine(output, "29-tutorial-complete.png"));
        Require(saves.WriteCount == writesAtEntry && vm.FlushPendingSave(), "Practice wrote its temporary match to a save slot.");

        vm.NextTutorialStepCommand.Execute(null);
        Require(!vm.IsTutorialActive && !vm.IsAutoAdvance && State(vm) == originalState,
            "Leaving practice did not restore the exact suspended game.");
        Require(vm.Hand.Single(card => card.Id == originalSlash.Id).IsSelected &&
                vm.Seats.Single(seat => seat.Seat == originalTarget.Seat).IsSelectedTarget && vm.CanConfirmSelected,
            "Leaving practice lost the suspended hand or target selection.");
        Require(saves.WriteCount == writesAtEntry, "Restoring the suspended game caused an extra save write.");
        Require(vm.Hand.Where(card => card.IsPlayable).Select(card => card.Id).Order().SequenceEqual(originalPlayable),
            "Returning from practice left the formal hand with tutorial-only availability.");
        window.Content = null;
        window.Close();

        using var setupVm = new MainViewModel(false, 721019, showSetup: true, saveStore: new MemorySaveStore());
        var setupWindow = new MainWindow(setupVm);
        var setupRoot = (FrameworkElement)setupWindow.Content;
        Program.Render(setupRoot, 1120, 740, Path.Combine(output, "30-tutorial-entry.png"));
        var entry = Program.Find<Button>(setupRoot).Single(button => button.Name == "StartTutorialButton");
        Require(entry.Command?.CanExecute(entry.CommandParameter) == true, "Setup tutorial entry is not usable.");
        entry.Command!.Execute(entry.CommandParameter);
        setupVm.ExitTutorialCommand.Execute(null);
        Require(setupVm.IsNewGameSetupOpen, "Practice started from setup did not return to setup.");
        setupWindow.Content = null;
        setupWindow.Close();

        using var failedVm = new MainViewModel(false, 721019, showSetup: false, saveStore: new RejectingSaveStore());
        failedVm.SelectGeneralChoiceCommand.Execute(failedVm.GeneralChoices[0]);
        var stateBeforeFailure = State(failedVm);
        failedVm.StartTutorialCommand.Execute(null);
        Require(!failedVm.IsTutorialActive && failedVm.HasTutorialEntryError && State(failedVm) == stateBeforeFailure,
            "A failed safety save replaced the formal match with practice.");
    }

    private static void CompleteAttack(MainViewModel vm)
    {
        var slash = vm.Hand.First(card => card.Name == "杀" && card.IsPlayable);
        vm.SelectCardCommand.Execute(slash);
        vm.SelectTargetCommand.Execute(vm.Seats.First(seat => seat.IsLegalTarget));
        Require(vm.CanConfirmSelected, "Attack lesson did not expose a real target.");
        vm.ConfirmSelectedCommand.Execute(null);
    }

    private static PromptChoice FindResponseCard(MainViewModel vm, CardKind kind)
    {
        var hand = Program.Engine(vm).State.Players.Single(player => player.IsHuman).Hand;
        return vm.ResponseChoices.Single(choice => choice.Cards.Count == 1 &&
            hand.Any(card => card.Id == choice.Cards[0] && card.Kind == kind));
    }

    private sealed class RejectingSaveStore : IGameSaveStore
    {
        public DateTimeOffset? GetSavedAt(GameSaveSlot slot) => null;
        public GameSaveFile Read(GameSaveSlot slot) => throw new FileNotFoundException();
        public void Write(GameSaveSlot slot, GameSaveFile save) => throw new IOException("test write failure");
    }
}
