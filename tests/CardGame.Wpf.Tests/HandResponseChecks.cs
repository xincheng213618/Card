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
                VerifyCancel(model, output, kind);
                StableHandResponseFixture.RecordAndReplay(Program.Engine(model), registry, output, kind + ".cancelled");
                if (kind == DecisionKind.RespondDodge) VerifyExtraChoices(model);
            }
        }
        var (rescue, rescueRegistry) = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, jijiu: true);
        using (rescue)
        {
            VerifyBoundary(rescue, output, "Jijiu", rescue.DyingChoices.First(choice => choice.Description.Contains("当作【桃】") && choice.Parameters.GetValueOrDefault("physical-card-kind") == nameof(CardKind.Dodge)).Cards[0]);
            StableHandResponseFixture.RecordAndReplay(Program.Engine(rescue), rescueRegistry, output, "Jijiu.after");
        }
        var (arrow, arrowRegistry) = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, arrowBarrage: true);
        using (arrow)
        {
            VerifyBoundary(arrow, output, "ArrowBarrage");
            StableHandResponseFixture.RecordAndReplay(Program.Engine(arrow), arrowRegistry, output, "ArrowBarrage.after");
            VerifyCancel(arrow, output, DecisionKind.RespondDodge);
            StableHandResponseFixture.RecordAndReplay(Program.Engine(arrow), arrowRegistry, output, "ArrowBarrage.cancelled");
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
        Program.Assert(((ItemsControl)window.FindName("HandResponseExtraChoices")).Items.Count == vm.HandResponseExtraChoices.Count &&
            vm.HandResponseExtraChoices.All(candidate => candidate.Cards.Count != 1 || candidate.Cards[0] != card.Id),
            $"{label}: selected hand response still has a duplicate central button.");
        Program.Assert(((Button)window.FindName("CancelActionButton")).Visibility == Visibility.Visible &&
            ((Grid)window.FindName("PrimaryActionButtons")).Children.OfType<Button>().Count(button => button.Visibility == Visibility.Visible) == 2,
            $"{label}: response must expose only the bottom confirm and cancel buttons.");
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

    private static void VerifyCancel(MainViewModel vm, string output, DecisionKind kind)
    {
        // Reload the saved, real pending response so both unselected and selected cancellation
        // exercise the same published prompt without another setup or seed search.
        foreach (var selectCard in new[] { false, true })
        {
            vm.LoadManualGameCommand.Execute(null);
            var engine = Program.Engine(vm);
            var prompt = engine.PendingDecision!;
            Program.Assert(prompt.Kind == kind, "Reload did not restore the original response.");
            var decline = prompt.Choices.SingleOrDefault(choice => choice.Cards.Count == 0);
            Program.Assert(vm.HandResponseExtraChoices.Count == 0,
                $"{kind}: ordinary hand choices or declining were duplicated in the center.");
            Program.Assert(vm.CanCancelAction == (decline is not null),
                $"{kind}: cancellation availability does not match the real prompt.");
            if (selectCard) vm.SelectCardCommand.Execute(vm.Hand.Last(card => card.IsPlayable));
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, $"response-{kind}-cancel-{selectCard}.png"));
            var cancel = (Button)window.FindName("CancelActionButton");
            Program.Assert(cancel.IsEnabled == (selectCard || decline is not null),
                $"{kind}: the actual cancel control has the wrong enabled state.");
            var movements = engine.CardMovements.Count;
            if (cancel.IsEnabled) cancel.Command!.Execute(cancel.CommandParameter);
            if (decline is null)
                Program.Assert(engine.Revision == prompt.Revision && engine.PendingDecision?.PromptId == prompt.PromptId && !vm.HasSelection,
                    "Mandatory fire-attack revealing must only clear selection, never decline or submit a card.");
            else
                Program.Assert(engine.Revision == prompt.Revision + 1 && engine.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.PromptId == prompt.PromptId && answer.Choice == decline.Id && !vm.HasSelection &&
                    !engine.CardMovements.Skip(movements).Any(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing),
                    $"{kind}: Cancel must decline once, with no selected card paid as a response cost.");
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifyExtraChoices(MainViewModel vm)
    {
        vm.LoadManualGameCommand.Execute(null);
        var snapshot = Program.Engine(vm).CreateSnapshot(0);
        var prompt = snapshot.PendingDecision!;
        var native = prompt.Choices.First(choice => choice.Cards.Count == 1);
        var hand = snapshot.Players.Single(player => player.IsHuman).Hand;
        // Presentation-only published variants cover skill/equipment choices and ambiguous
        // one-card effects. Core commands remain untouched throughout this UI projection check.
        var ambiguous = native with { Id = new("fixture:alternate-response"), Description = "另一种响应效果" };
        var equipment = new PromptChoice(new("fixture:equipment-response"), "发动装备响应", [], [],
            new Dictionary<string, string> { ["response"] = "bagua" });
        var pair = new PromptChoice(new("fixture:two-card-response"), "两张手牌响应", hand.Take(2).Select(card => card.Id).ToArray(), [],
            new Dictionary<string, string> { ["response"] = "zhangba-slash" });
        var refresh = typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            refresh.Invoke(vm, [snapshot with { PendingDecision = prompt with { Choices = prompt.Choices.Concat([ambiguous, equipment, pair]).ToArray() } }]);
            Program.Assert(vm.HandResponseExtraChoices.Select(choice => choice.Id).ToHashSet()
                    .SetEquals([native.Id, ambiguous.Id, equipment.Id, pair.Id]) &&
                !vm.Hand.Single(card => card.Id == native.Cards[0]).IsPlayable &&
                vm.HandResponseExtraChoices.All(choice => vm.SelectHandResponseChoiceCommand.CanExecute(choice)),
                "Distinct effects, equipment, and multi-card responses must retain their exact central choices.");
        }
        finally { refresh.Invoke(vm, [snapshot]); }
    }

    private static bool Shortcut(MainWindow window, Key key) => (bool)typeof(MainWindow)
        .GetMethod("HandleShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [key, ModifierKeys.None])!;
}
