using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Core.Tests;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BoundaryCaoXiuMobileUiChecks
{
    public static void ActualTargetHandBatchAndDamageChoice(string output)
    {
        foreach (var bonus in new[] { false, true })
        {
            var scene = BoundaryCaoXiuMobileChecks.CreateQingxiChoice(humanTarget: true);
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(scene.Game.CreateCheckpoint())), scene.Registry);
            using var model = new MainViewModel(false, scene.Game.Seed, false, new MemorySaveStore(), contentRegistry: scene.Registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            // The local save picker intentionally accepts seat zero only. Attach
            // the validated target engine through the existing UI fixture seam.
            typeof(MainViewModel).GetMethod("ReplaceEngine", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [restored]);
            model.IsNewGameSetupOpen = false;
            var game = Program.Engine(model);
            var prompt = game.CreateSnapshot(2).PendingDecision!;
            var required = prompt.RequiredCardCount;
            var source = prompt.SourceSeat;
            Program.Assert(game.State.HumanSeat == 2 && prompt.PlayerSeat == 2 && source is >= 0 and <= 3 && source != 2 && required is >= 2 and <= 3 &&
                model.CurrentDecisionContext?.TargetSeat == 2 && model.IsOwnedCardSelectionPending &&
                model.Hand.Count(card => card.IsPlayable) >= required && !model.CanConfirmSelected,
                "Cold loading the actual target's view must expose its legal multi-card hand payment and an initially disabled batch confirmation.");
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, bonus ? "boundary-cao-xiu-target-bonus" : "boundary-cao-xiu-target-hand", 1120);
            var commands = game.AcceptedCommands.Count;
            var movements = game.CardMovements.Count;
            var originals = model.Hand.Where(card => card.IsPlayable).Take(required).Select(card => card.Id).ToArray();
            var chooseBonus = model.CenterSkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("branch") == "bonus");
            var bonusButton = Program.Find<Button>(host.Root).Single(button =>
                ReferenceEquals(button.Command, model.SelectSkillChoiceCommand) && ReferenceEquals(button.CommandParameter, chooseBonus) &&
                button.Visibility == Visibility.Visible && button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0);
            Program.Assert(bonusButton.Command!.CanExecute(bonusButton.CommandParameter), "The original add-damage choice must have an enabled bound button alongside the hand selector.");
            if (bonus)
            {
                Click(bonusButton);
                Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.CardMovements.Count == movements &&
                    game.Events.Select(e => e.Payload).OfType<BoundaryCaoXiuQingxiResolvedEvent>().Single() is
                        { TargetSeat: 2, Discarded: false, DamageBefore: 1, DamageAfter: 2 } && !model.IsOwnedCardSelectionPending,
                    "The real target's bound bonus button must submit one original answer, preserve its hand, and increase the pending damage once.");
                continue;
            }

            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == originals[0]));
            Program.Assert(model.Hand.Count(card => card.IsSelected) == 1 && !model.CanConfirmSelected &&
                game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements,
                "One local target hand selection must remain unpaid and cannot confirm an incomplete multi-card cost.");
            foreach (var id in originals.Skip(1)) model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == id));
            Program.Assert(model.CanConfirmSelected && model.Hand.Count(card => card.IsSelected) == required &&
                game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements,
                "Selecting all required materials only prepares the exact batch; it must not stage native answers or move any entity yet.");
            host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); host.Root.UpdateLayout();
            Click((Button)host.Window.FindName("PlayCardButton"));
            var paid = game.CardMovements.Skip(movements).Where(move => move.Reason.Value == "skill-program.boundary-qingxi.discard").ToArray();
            var outcome = game.Events.Select(e => e.Payload).OfType<BoundaryCaoXiuQingxiResolvedEvent>().Single();
            Program.Assert(paid.Length == required && paid.Select(move => move.CardId).ToHashSet().SetEquals(originals) &&
                paid.All(move => move.From == CardLocation.Hand(2) && move.To == CardLocation.DiscardPile) &&
                outcome is { TargetSeat: 2, Discarded: true, DamageBefore: 1, DamageAfter: 1 } && outcome.RequiredCount == required && outcome.Source.OwnerSeat == source &&
                game.AcceptedCommands.Count == commands + required + 1 && !model.IsOwnedCardSelectionPending && !model.HasSelection,
                "One actual confirmation must submit every current native pick and its finish, pay the exact required original entities once, and enter the already-paid child.");
            var afterCommands = game.AcceptedCommands.Count;
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Count == afterCommands &&
                game.CardMovements.Count(move => move.Reason.Value == "skill-program.boundary-qingxi.discard") == required,
                "An extra UI confirmation cannot replay the completed hand payment while the movement child is pending.");
        }
    }

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter),
            "The actual bound Qingxi control must be visible and enabled before its click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
