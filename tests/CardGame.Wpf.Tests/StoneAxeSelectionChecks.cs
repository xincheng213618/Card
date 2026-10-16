using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class StoneAxeSelectionChecks
{
    public static void TwoCardsThenConfirmation(string output)
    {
        var witness = StoneAxeScenario.FindHumanTrigger();
        var registry = StoneAxeScenario.CreateRegistry();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, witness.Game.CreateCheckpoint()));
        using var model = new MainViewModel(false, witness.Game.Seed, true, store,
            useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var game = Engine(model);
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var before = Boundary(game);
            var pair = prompt.Choices.First(choice => choice.Cards.Count == 2 &&
                choice.Cards.All(id => model.Hand.Any(card => card.Id == id)));
            var first = model.Hand.Single(card => card.Id == pair.Cards[0]);
            var second = model.Hand.Single(card => card.Id == pair.Cards[1]);
            Render(root, 1120, 740, Path.Combine(output, "stone-axe-empty.png"));
            var yes = (Button)window.FindName("PlayCardButton");
            var no = (Button)window.FindName("CancelActionButton");
            Assert(model.IsHandResponsePending && model.CanActFromHand && !model.HasCenterChoices &&
                ((Border)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed &&
                Equals(yes.Content, "是") && Equals(no.Content, "否") && !yes.IsEnabled && no.IsEnabled,
                "The native Axe must show only a disabled Yes and an enabled No before selecting its two-card cost.");
            model.ConfirmSelectedCommand.Execute(null);
            model.SelectCardCommand.Execute(first);
            Render(root, 1120, 740, Path.Combine(output, "stone-axe-one-card.png"));
            Assert(first.IsSelected && !yes.IsEnabled && model.HasSelection && Boundary(game) == before,
                "One selected card must leave Yes disabled without paying or answering.");
            model.ConfirmSelectedCommand.Execute(null);
            model.SelectCardCommand.Execute(second);
            root.UpdateLayout();
            Assert(first.IsSelected && second.IsSelected && yes.IsEnabled && Boundary(game) == before,
                "Exactly two hand cards must enable Yes while retaining the complete native boundary.");
            var third = model.Hand.First(card => card.IsPlayable && card.Id != first.Id && card.Id != second.Id);
            model.SelectCardCommand.Execute(third);
            Assert(!third.IsSelected && model.Hand.Count(card => card.IsSelected) == 2 && yes.IsEnabled,
                "A third card must not replace or extend the prepared two-card payment.");
            model.SelectedPlaybackSpeed = PlaybackSpeed.Fast;
            Assert(first.IsSelected && second.IsSelected && yes.IsEnabled && Boundary(game) == before,
                "A presentation refresh must retain the two selected materials without answering.");
            model.StartTutorialCommand.Execute(null);
            model.ExitTutorialCommand.Execute(null);
            first = model.Hand.Single(card => card.Id == pair.Cards[0]);
            second = model.Hand.Single(card => card.Id == pair.Cards[1]);
            Assert(first.IsSelected && second.IsSelected && yes.IsEnabled && Boundary(game) == before,
                "Returning from practice must preserve the suspended Axe draft and actual payment boundary.");
            model.SelectCardCommand.Execute(first);
            Assert(!first.IsSelected && second.IsSelected && !yes.IsEnabled,
                "Deselecting either payment must immediately disable Yes.");
            model.ClearSelectionCommand.Execute(null);
            Assert(!model.HasSelection && !yes.IsEnabled && Boundary(game) == before,
                "Clearing the draft must leave the actual Axe decision untouched.");
            model.SelectCardCommand.Execute(second);
            model.SelectCardCommand.Execute(first);
            Render(root, 1120, 740, Path.Combine(output, "stone-axe-two-cards.png"));
            Click(yes);
            Assert(game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == pair.Id &&
                answer.PromptId == prompt.PromptId && game.Revision == prompt.Revision + 1 &&
                game.Events.Select(item => item.Payload).OfType<StoneAxeResolvedEvent>().Last() is { Used: true } used &&
                used.DiscardedCardIds.SequenceEqual(pair.Cards) && !model.HasSelection,
                "Yes must submit the exact published pair once, independent of selection order, and clear the draft.");
            var after = Boundary(game);
            model.ConfirmSelectedCommand.Execute(null);
            Assert(Boundary(game) == after, "A retired Axe confirmation must not pay twice.");

            model.LoadManualGameCommand.Execute(null);
            game = Engine(model);
            before = Boundary(game);
            var equipmentPair = model.SkillChoices.First(choice => choice.Cards.Count == 2 && choice.Cards.Contains(witness.StoneAxeCardId));
            var equipment = model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == witness.StoneAxeCardId);
            var hand = model.Hand.Single(card => card.Id == equipmentPair.Cards.Single(id => id != witness.StoneAxeCardId));
            model.SelectCardCommand.Execute(hand);
            root.UpdateLayout();
            var equipmentControl = (ItemsControl)window.FindName("HumanEquipmentSlots");
            var equipmentButton = Find<Button>(equipmentControl).Single(button =>
                button.Command == model.SelectStoneAxeEquipmentCommand && ReferenceEquals(button.CommandParameter, equipment));
            Assert(equipmentButton.IsEnabled && equipmentButton.Visibility == Visibility.Visible,
                "The actual equipped Axe must be selectable as a native cost.");
            Click(equipmentButton);
            root.UpdateLayout();
            Assert(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == witness.StoneAxeCardId).IsSelected &&
                hand.IsSelected && yes.IsEnabled && Boundary(game) == before,
                "A hand card plus the equipped Axe must enable Yes without discarding either early.");
            Render(root, 1120, 740, Path.Combine(output, "stone-axe-equipment-cost.png"));
            Click(yes);
            Assert(game.AcceptedCommands.Last() is AnswerPromptCommand equippedAnswer && equippedAnswer.Choice == equipmentPair.Id &&
                !game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == witness.StoneAxeCardId),
                "Confirming a mixed-zone payment must submit the original pair and discard the equipped Axe.");

            foreach (var selected in new[] { false, true })
            {
                model.LoadManualGameCommand.Execute(null);
                game = Engine(model);
                before = Boundary(game);
                model.SelectStoneAxeEquipmentCommand.Execute(equipment);
                model.SelectCardCommand.Execute(first);
                Assert(!model.HasSelection && Boundary(game) == before,
                    "Retired equipment controls and hand cards must not select materials after reload.");
                if (selected) model.SelectCardCommand.Execute(model.Hand.First(card => card.IsPlayable));
                var payments = game.CardMovements.Count(move => move.Reason == CardMoveReasons.StoneAxeDiscard);
                var handIds = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToArray();
                var revision = game.Revision;
                Click(no);
                Assert(game.Revision == revision + 1 &&
                    game.CardMovements.Count(move => move.Reason == CardMoveReasons.StoneAxeDiscard) == payments &&
                    game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).SequenceEqual(handIds) &&
                    game.Events.Select(item => item.Payload).OfType<StoneAxeResolvedEvent>().Last() is { Used: false, DiscardedCardIds.Count: 0 } &&
                    !model.HasSelection && !yes.IsEnabled,
                    "No must decline once with or without a partial draft and retain all payment cards.");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static string Boundary(GameEngine game) => System.Text.Json.JsonSerializer.Serialize(game.CreateCheckpoint());
    private static void Click(Button button)
    {
        Assert(button.IsEnabled, "The intended native confirmation control must be enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
