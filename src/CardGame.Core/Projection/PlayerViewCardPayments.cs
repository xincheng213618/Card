namespace CardGame.Core;

public sealed partial class GameEngine
{
    // These producers collect an unpaid set on their frame and move it only
    // after the last selection. Publish its remaining count to the chooser.
    private PendingDecision ProjectCardPaymentCount(PendingDecision decision)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame) return decision;
        if (frame.PileEquipment is { Stage: "store-choice" } &&
                decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "game-domain") ||
            frame.PublicPilePreparation is { Stage: PublicPilePreparationStage.Choosing,
                Operation: SkillProgramEffectOp.StoreNonBasicOwnedPublicPile } &&
                decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "public-pile-preparation"))
            return decision with
            {
                RequiredCardCount = decision.Choices.SelectMany(choice => choice.Cards).Distinct().Count(),
                MinimumCardCount = 0
            };
        var action = decision.Choices.FirstOrDefault(choice => choice.Cards.Count == 1)?
            .Parameters.GetValueOrDefault("program-action");
        int? remaining = action switch
        {
            "lianzhu" when frame.LianZhu is { Stage: LianZhuStage.SelectingDiscard, CountPolicy: { } count } cost => count.Amount - cost.Selected.Count,
            "dynamic-discard-damage" when frame.DynamicDiscardDamage is { } cost => cost.FrozenTargetHp - cost.CardIds.Count,
            "game-hand-hp-discard" when frame.GameTargetHandHp is { } cost => cost.RequiredCount - cost.CardIds.Count,
            "draw-discard-category" when frame.DrawDiscardCategoryRefund is { } cost => cost.RequiredCount - cost.SelectedCardIds.Count,
            "capped-hand-discard" when frame.CappedHandRefresh is { } cost => cost.DiscardCount - cost.SelectedCardIds.Count,
            "dying-suits" when frame.DyingSuits is { } cost => cost.RequiredDiscardCount - cost.SelectedCardIds.Count,
            "prep-discard-ending" when frame.PrepDiscard is { } cost => cost.RequiredCount - cost.SelectedCardIds.Count,
            _ => null
        };
        return remaining is > 0 ? decision with { RequiredCardCount = remaining.Value } : decision;
    }
}
