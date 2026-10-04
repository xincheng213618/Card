namespace CardGame.Core;

internal static class EquipmentPairDyingCardComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        SkillProgramTriggerSubject? subject = null)
    {
        var pair = effects.Any(e => e.Op == SkillProgramEffectOp.SelectEquipmentPairAndPayment);
        var dying = effects.Any(e => e.Op == SkillProgramEffectOp.SelectDyingOwnedCard);
        if (!pair && !dying) return;
        var op = pair ? SkillProgramEffectOp.SelectEquipmentPairAndPayment : SkillProgramEffectOp.SelectDyingOwnedCard;
        if (pair && dying || effects.Count != 4 || effects[0].Op != op ||
            effects[0].Condition.Kind != SkillProgramConditionKind.Always ||
            effects[1] is not { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
                Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true } || effects[1].SourceBind != effects[0].ResultBind ||
            effects[1].Condition.Kind != SkillProgramConditionKind.Always ||
            effects[2] is not { Op: SkillProgramEffectOp.AwaitBoundCardMovements, Target: SkillProgramEffectTarget.Owner } ||
            effects[2].Condition.Kind != SkillProgramConditionKind.Always ||
            pair && (window is not null || effects[3] is not { Op: SkillProgramEffectOp.ExchangeSelectedTargetEquipment, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }) ||
            dying && (window != SkillProgramTriggerWindow.DyingEntering || subject is not null && subject != SkillProgramTriggerSubject.Any ||
                effects[3] is not { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 } ||
                effects[3].Condition.Kind != SkillProgramConditionKind.BoundCardsMatchCategories || effects[3].Condition.SourceBind != effects[0].ResultBind ||
                !effects[3].Condition.CardCategories.Order().SequenceEqual(new[] { SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }.Order())))
            throw new InvalidOperationException($"Invalid skill program at {path}: paid equipment pair or dying owned card requires its exact four-node payment/await/benefit composition.");
    }
}
