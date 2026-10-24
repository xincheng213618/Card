using CardGame.Core;
using CardGame.Wpf.ViewModels;

internal static class ShiLuSuDiscardUiChecks
{
    private const string Dimeng = "mobile:shi-dimeng", DiscardReason = "skill.mobile-shi-lu-su.dimeng.discard";

    public static void DimengTwoHandCardsRemainUnpaidUntilSingleConfirmation(string output)
    {
        var scene = ShiFifthBatchUiChecks.CreateScene("mobile:shi-lu-su", wounded: true);
        var game = scene.Game;
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        using var model = ShiFifthBatchUiChecks.Model(game, scene.Registry);
        game = Program.Engine(model);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var own = Hand(0); var foreign = Hand(1);
        Program.Assert(own.Length == 2 && foreign.Length == 4 && game.CreateSnapshot(0).Players.Single(p => p.Seat == 0).Hp == 1,
            "The actual wounded Lu Su scene has two own cards and four opponent cards before the exchange.");
        var chip = model.HumanSkillCards.Single(s => s.ContentId == Dimeng);
        Program.Assert(chip.IsAvailable, "The real Dimeng rail entry must expose its native phase activation.");
        model.ActivateHumanSkillCommand.Execute(chip);
        PickTarget(0); PickTarget(1);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c => Branch(c) == "discard") == true);
        ShiFifthBatchUiChecks.Refresh(model);
        Program.Assert(Hand(0).SequenceEqual(foreign) && Hand(1).SequenceEqual(own) &&
            game.Events.Select(e => e.Payload).OfType<ShiLsExchangeCompletedEvent>().Count() == 1,
            "Selecting Lu Su and the opponent in the UI exchanges both original hands before either benefit is selected.");
        Program.Assert(model.HasDockSkillChoices, "The native post-exchange discard branch remains available in the real action dock.");
        model.DockSkillChoiceCommand.Execute(model.DockSkillChoices.Single(c => Branch(c) == "discard"));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Dimeng).Id;
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && model.IsOwnedCardSelectionPending && !model.IsCardPaymentSelectionPending &&
            game.CreateSnapshot(0).PendingDecision is { RequiredCardCount: 2, MinimumCardCount: 2 } &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Dimeng).Id == frame,
            "Cold loading the unpaid original Dimeng collector restores its two-card owned selection on the same frame.");
        var ids = model.Hand.Where(c => c.IsPlayable).Take(2).Select(c => c.Id).ToArray();
        Program.Assert(ids.Length == 2, "Both physical cards are selectable in the shared owned-card UI.");
        var commands = game.AcceptedCommands.Count; var moves = game.CardMovements.Count;
        Select(ids[0]);
        Program.Assert(!model.CanConfirmSelected && model.Hand.Count(c => c.IsSelected) == 1 && Unpaid(),
            "The first local card is retained without accepting or paying a singleton, and cannot satisfy X2.");
        Select(ids[1]); ShiFifthBatchUiChecks.Refresh(model);
        Program.Assert(model.CanConfirmSelected && model.Hand.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet().SetEquals(ids) && Unpaid(),
            "Both hand selections survive refresh together and remain entirely unpaid before confirmation.");
        model.CancelActionCommand.Execute(null);
        Program.Assert(model.IsOwnedCardSelectionPending && model.Hand.All(c => !c.IsSelected) && !model.HasSelection && Unpaid(),
            "Cancel clears the complete local draft while preserving the unpaid original Dimeng collector.");
        foreach (var id in ids) Select(id);
        using (var host = new NativeResponseStatusChecks.NativeHost(model)) host.Capture(output, "mobile-shi-lu-su-dimeng-two-card-draft", 1120);
        model.ConfirmSelectedCommand.Execute(null);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        ShiFifthBatchUiChecks.Refresh(model);
        var paid = game.CardMovements.Skip(moves).Where(m => m.Reason.Value == DiscardReason).ToArray();
        var invocation = game.Events.Select(e => e.Payload).OfType<NativeLogicalDiscardInvocationRecordedEvent>()
            .Single(e => e.ParentFrameId == frame && e.Reason.Value == DiscardReason);
        Program.Assert(paid.Length == 2 && paid.Select(m => m.CardId).ToHashSet().SetEquals(ids) &&
            paid.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) &&
            invocation is { InitiatorSeat: 0, ExecutorSeat: 0, ActualCount: 2, HandCount: 2 } &&
            game.Events.Select(e => e.Payload).OfType<ShiLsDiscardPaidEvent>().Single() is { Count: 2 } payment && payment.FrameId == frame &&
            model.Hand.All(c => !c.IsSelected) && !model.HumanSkillCards.Single(s => s.ContentId == Dimeng).IsAvailable,
            "One primary confirmation pays the exact two original hand entities in one logical discard and spends Dimeng once.");
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && game.CardMovements.Count(m => m.Reason.Value == DiscardReason) == 2 &&
            game.Events.Select(e => e.Payload).OfType<ShiLsDiscardPaidEvent>().Count() == 1 &&
            game.Events.Select(e => e.Payload).OfType<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.Reason.Value == DiscardReason) == 1 &&
            !model.HumanSkillCards.Single(s => s.ContentId == Dimeng).IsAvailable,
            "A cold UI load preserves the completed payment and spent phase entry without discarding either entity twice.");

        int[] Hand(int seat) => game.CreateSnapshot(seat).Players.Single(p => p.Seat == seat).Hand.Select(c => c.Id).Order().ToArray();
        bool Unpaid() => game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves &&
            !game.Events.Select(e => e.Payload).OfType<ShiLsDiscardPaidEvent>().Any();
        void Select(int id) => model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
        void PickTarget(int seat)
        {
            Program.Assert(model.IsSkillTargetSelectionPending && model.Seats.Single(s => s.Seat == seat).IsLegalTarget,
                "The native Dimeng pair chooser must accept this published participant through the real portrait command.");
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == seat));
            Program.Assert(model.CanConfirmSelected, "A selected native participant enables the primary confirmation.");
            model.ConfirmSelectedCommand.Execute(null);
        }
    }

    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch");
}
