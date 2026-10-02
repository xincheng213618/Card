using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandResponseChecks
{
    public static void Controls(string output)
    {
        foreach (var kind in new[] { DecisionKind.RespondDodge, DecisionKind.RespondSlash,
                     DecisionKind.RescueDying, DecisionKind.Nullification, DecisionKind.FireAttackReveal, DecisionKind.FireAttackDiscard })
        {
            var (model, registry) = StableHandResponseFixture.Create(kind, output);
            using (model)
            {
                VerifyBoundary(model, output, kind.ToString());
                StableHandResponseFixture.RecordAndReplay(Program.Engine(model), registry, output, kind + ".after");
            }
        }
        var (rescue, rescueRegistry) = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, jijiu: true);
        using (rescue)
        {
            VerifyBoundary(rescue, output, "Jijiu", rescue.DyingChoices.First(choice => choice.Description.Contains("当作【桃】") && choice.Parameters.GetValueOrDefault("physical-card-kind") == nameof(CardKind.Dodge)).Cards[0]);
            StableHandResponseFixture.RecordAndReplay(Program.Engine(rescue), rescueRegistry, output, "Jijiu.after");
        }
    }

    internal static void VerifyBoundary(MainViewModel vm, string output, string label, int? chosenId = null, string? expectedButtonText = null)
    {
        var engine = Program.Engine(vm);
        var prompt = engine.PendingDecision!;
        DecisionContextChecks.VerifyLive(vm);
        var card = chosenId is { } id ? vm.Hand.Single(card => card.Id == id) : vm.Hand.Last(card => card.IsPlayable);
        var choice = prompt.Choices.Single(choice => choice.Cards.Count == 1 && choice.Cards[0] == card.Id);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
        Program.Assert(!vm.CanConfirmSelected && vm.CanActFromHand, $"{label}: response should require selection.");
        var invalid = vm.Hand.FirstOrDefault(card => !card.IsPlayable);
        if (invalid is not null) vm.SelectCardCommand.Execute(invalid);
        foreach (var ambiguous in vm.Hand.Where(card => prompt.Choices.Count(choice => choice.Cards.Count == 1 && choice.Cards[0] == card.Id) > 1))
            Program.Assert(!ambiguous.IsPlayable && ambiguous.AvailabilityText.Contains("多种响应方式"), "Ambiguous card silently picked an effect.");
        Program.Assert(!vm.HasSelection, $"{label}: invalid hand card selected.");
        vm.SelectCardCommand.Execute(card);
        Program.Assert(card.IsSelected && vm.CanConfirmSelected && vm.ActionHint.Contains(choice.Description), $"{label}: missing exact response description.");
        Program.Assert(SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state, $"{label}: selecting paid the cost.");
        Shortcut(window, Key.Escape);
        Program.Assert(!vm.CanConfirmSelected && !card.IsSelected && engine.PendingDecision?.PromptId == prompt.PromptId,
            $"{label}: Esc should cancel selection, never decline the response.");
        var index = vm.Hand.IndexOf(card);
        if (index < 9) Program.Assert(Shortcut(window, Key.D1 + index), "Number shortcut did not select response card.");
        else vm.SelectCardCommand.Execute(card);
        Shortcut(window, Key.F1);
        Program.Assert(!Shortcut(window, Key.Enter) && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state,
            $"{label}: guide allowed a hidden confirmation.");
        Program.Assert(vm.CurrentGuideSteps.Any(step => step.Text.Contains(choice.Description)), $"{label}: guide lost the chosen effect.");
        Shortcut(window, Key.Escape);
        vm.StartTutorialCommand.Execute(null);
        vm.ExitTutorialCommand.Execute(null);
        Program.Assert(vm.Hand.Single(item => item.Id == card.Id).IsSelected && vm.CanConfirmSelected &&
            ReferenceEquals(engine, Program.Engine(vm)) && SnapshotJson.Serialize(engine.CreateSnapshot(0, true)) == state,
            $"{label}: tutorial lost response selection or changed the match.");
        Program.Render(root, 1120, 740, Path.Combine(output, $"40-response-{label}.png"));
        Program.Assert(((TextBlock)window.FindName("DecisionTitleText")).Text == vm.CurrentDecisionContext!.Title &&
            ((TextBlock)window.FindName("DecisionDescriptionText")).Text == vm.CurrentDecisionContext.Description,
            "Actual response context controls did not bind to the current prompt.");
        var confirm = (Button)window.FindName("PlayCardButton");
        Program.Assert(confirm.Visibility == Visibility.Visible && confirm.IsEnabled, $"{label}: main response control is missing.");
        if (expectedButtonText is not null) Program.Assert(Equals(confirm.Content, expectedButtonText), "Converted response button did not name the effective card.");
        Program.Assert(Shortcut(window, Key.Enter), $"{label}: Enter was not handled.");
        Program.Assert(engine.Revision == prompt.Revision + 1 && engine.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.Choice == choice.Id && answer.PromptId == prompt.PromptId,
            $"{label}: confirmation did not submit the selected exact choice once.");
        Program.Assert(!vm.HasSelection && !vm.CanConfirmSelected, $"{label}: old selection leaked into next prompt.");
        DecisionContextChecks.VerifyLive(vm);
        var after = engine.Revision;
        vm.ConfirmSelectedCommand.Execute(null);
        Program.Assert(engine.Revision == after, $"{label}: repeated confirmation reused the previous choice.");
        Console.WriteLine($"  Hand response verified: {label}, {choice.Description}");
        window.Content = null;
        window.Close();
    }

    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [key, ModifierKeys.None])!;
}
