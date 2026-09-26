using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class SkillInteractionChecks
{
    public static void ConfirmAndResume(string output)
    {
        CheckSkill(ActiveSkillChecks.FindZhihengViewModel, "制衡", 2, output);
        CheckSkill(ActiveSkillChecks.FindRendeViewModel, "仁德", 1, output);
        CheckSkill(ActiveSkillChecks.FindHuichunViewModel, "回春", 2, output);
    }

    private static void CheckSkill(Func<MainViewModel> create, string skillName, int cardCount, string output)
    {
        using var vm = create();
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => choice.SkillName == skillName));
        if (skillName == "回春")
        {
            ActiveSkillChecks.AdvanceHuichunToPlay(vm);
            Require(ActiveSkillChecks.ReachHuichunTargets(vm), "Huichun did not reach its real multi-target boundary.");
        }
        else Program.AdvanceToDecision(vm);
        var engine = Program.Engine(vm);
        var action = engine.GetHumanLegalActions().Single(action => action.Kind == LegalActionKind.UseSkill);
        var prompt = engine.PendingDecision!;
        var validCards = prompt.ActiveSkillValidCardIds ?? throw new InvalidOperationException("Skill has no selectable costs.");
        var cards = validCards.Take(cardCount).ToArray();
        var targets = (prompt.ActiveSkillValidTargetSeats ?? []).Take(action.MinTargetCount).ToArray();
        var before = State(vm);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, $"37-{skillName}-entry.png"));
        var entry = Program.Find<Button>(root).Single(button => button.DataContext is HumanSkillViewModel skill && skill.Name == skillName);
        var confirm = (Button)window.FindName("PlayCardButton");
        Require(entry.IsEnabled && entry.Visibility == Visibility.Visible, "Skill entry is not available.");
        entry.Command.Execute(entry.CommandParameter);
        Require(vm.IsActiveSkillSelectionPending && !vm.CanConfirmSelected, "Empty skill selection should not be confirmable.");
        vm.ConfirmSelectedCommand.Execute(null);
        Require(State(vm) == before && vm.IsActiveSkillSelectionPending && !Shortcut(window, Key.Enter),
            "Empty confirmation changed the game or canceled the draft.");

        // Drafts survive a guide visit and canceling the new-game overlay.
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == cards[0]));
        Require(Shortcut(window, Key.F1) && vm.CurrentGuideTitle.Contains(skillName), "Skill draft uses ordinary card guidance.");
        var draftState = State(vm);
        Require(!Shortcut(window, Key.Enter) && State(vm) == draftState, "Guide Enter submitted a hidden skill.");
        Shortcut(window, Key.Escape);
        Require(vm.Hand.Single(card => card.Id == cards[0]).IsSelected, "Closing the guide lost the draft.");
        vm.NewGameCommand.Execute(null);
        Shortcut(window, Key.Escape);
        Require(vm.Hand.Single(card => card.Id == cards[0]).IsSelected && State(vm) == before, "Canceling setup lost or committed the skill draft.");
        Shortcut(window, Key.Escape);
        Require(!vm.IsActiveSkillSelectionPending && !vm.HasSelection && State(vm) == before, "Esc did not cancel the whole draft without paying its cost.");

        vm.UseActiveSkillCommand.Execute(null);
        foreach (var id in cards) vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == id));
        if (targets.Length > 0)
        {
            Require(!vm.CanConfirmSelected, "Cards alone enabled a skill requiring targets.");
            vm.ConfirmSelectedCommand.Execute(null);
            Require(State(vm) == before && vm.Hand.Count(card => card.IsSelected) == cards.Length,
                "Incomplete target selection committed or lost the chosen cards.");
        }
        foreach (var seat in targets) vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
        Require(vm.CanConfirmSelected && vm.PlayButtonText == $"发动{skillName}", "The main action button is not the skill confirmation.");
        if (targets.Length > 0)
        {
            vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == targets[0]));
            Require(!vm.CanConfirmSelected, "Removing a required target did not disable confirmation.");
            vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == targets[0]));
        }
        foreach (var id in cards) vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == id));
        Require(!vm.CanConfirmSelected, "Removing the cost did not disable confirmation.");
        foreach (var id in cards) vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == id));
        Require(vm.CanConfirmSelected, "Restoring the cost did not re-enable confirmation.");
        Shortcut(window, Key.F1);
        Require(vm.CurrentGuideTitle == $"确认发动【{skillName}】" && vm.CurrentGuideBody.Contains(vm.HumanPlayer!.SkillName),
            "The guide does not describe the actual skill confirmation.");
        Program.Render(root, 1120, 740, Path.Combine(output, $"38-{skillName}-guide.png"));
        Shortcut(window, Key.Escape);

        vm.StartTutorialCommand.Execute(null);
        Require(vm.IsTutorialActive && !vm.IsActiveSkillSelectionPending, "Formal skill draft leaked into practice.");
        vm.ExitTutorialCommand.Execute(null);
        Require(ReferenceEquals(Program.Engine(vm), engine) && State(vm) == before && vm.IsActiveSkillSelectionPending,
            "Practice did not restore the original engine and skill draft.");
        Require(vm.Hand.Where(card => card.IsSelected).Select(card => card.Id).Order().SequenceEqual(cards.Order()) &&
            vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).Order().SequenceEqual(targets.Order()),
            "Practice lost selected costs or targets.");
        Require(vm.Hand.All(card => card.IsPlayable == validCards.Contains(card.Id)) && vm.CanConfirmSelected,
            "Restored skill cards retained the tutorial's dimming or confirmation gate.");
        Program.Render(root, 1120, 740, Path.Combine(output, $"39-{skillName}-ready.png"));
        var currentEntry = Program.Find<Button>(root).Single(button => button.DataContext is HumanSkillViewModel skill && skill.Name == skillName);
        Require(!currentEntry.IsEnabled && confirm.IsEnabled, "Skill draft must have only one enabled primary confirmation.");
        var draftRevision = engine.Revision;
        currentEntry.Command!.Execute(currentEntry.CommandParameter);
        Require(engine.Revision == draftRevision && vm.IsActiveSkillSelectionPending && vm.CanConfirmSelected,
            "Re-clicking a skill chip committed or changed the prepared draft.");
        var revision = engine.Revision;
        if (skillName == "回春") confirm.Command.Execute(null);
        else Require(Shortcut(window, Key.Enter), "Enter did not confirm the complete skill draft.");
        Require(engine.Revision == revision + 1 && engine.AcceptedCommands.Last() is UseSkillCommand command &&
            command.Skill == action.Skill && command.CardIds.Order().SequenceEqual(cards.Order()) && command.TargetSeats.Order().SequenceEqual(targets.Order()),
            "Primary confirmation did not submit exactly one typed skill command with the chosen cost and targets.");
        Require(!vm.IsActiveSkillSelectionPending && !vm.HasSelection, "Committed skill retained the old draft.");
        var after = State(vm);
        vm.ConfirmSelectedCommand.Execute(null);
        Require(State(vm) == after, "Repeated confirmation submitted the same draft twice.");
        window.Content = null;
        window.Close();
    }

    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [key, ModifierKeys.None])!;
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
}
