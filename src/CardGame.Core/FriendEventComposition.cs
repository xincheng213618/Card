namespace CardGame.Core;

internal static class FriendEventComposition
{
    internal static bool IsOperation(SkillProgramEffectOp operation) => operation is >= SkillProgramEffectOp.RecastOwnedCardsAndDuel and <= SkillProgramEffectOp.ConfigureFriendUseSuits;
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (!activation.Effects.Any(e => IsOperation(e.Op))) return;
        if (activation.Effects is not [{ } effect] || effect.Op is not (SkillProgramEffectOp.RecastOwnedCardsAndDuel or SkillProgramEffectOp.DiscardMaximumAndPunishHigherHand) ||
            effect.Target != SkillProgramEffectTarget.Owner || activation.MinTargets != 1 || activation.MaxTargets != 1 || activation.TargetKind != SkillProgramTargetKind.OtherLiving ||
            activation.UsesPerPhase != 1 || activation.UsesPerTurn is not null || activation.UsesPerGame is not null || activation.Condition.Kind != SkillProgramConditionKind.Always ||
            activation.MarkerCost is not null || activation.SelectedCardsDistinctSuits || activation.SelectedCardsSameSuit || activation.CardCountExpression is not null)
            throw new InvalidOperationException($"Invalid skill program at {path}: friend active operations require one exact unconditional once-per-Play other-target activation.");
        if (effect.Op == SkillProgramEffectOp.RecastOwnedCardsAndDuel &&
            (activation.MinCards != 0 || activation.MaxCards != int.MaxValue || !activation.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment])) ||
            effect.Op == SkillProgramEffectOp.DiscardMaximumAndPunishHigherHand &&
            (activation.MinCards != 1 || activation.MaxCards != 1 || !activation.SourceZones.SequenceEqual([CardZoneKind.Hand])))
            throw new InvalidOperationException($"Invalid skill program at {path}: friend active operations lost their exact physical card bounds or zones.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => IsOperation(e.Op))) return;
        if (trigger.Effects is not [{ } effect] || effect.Target != SkillProgramEffectTarget.Owner || trigger.UsageScope is not null || trigger.MarkerCost is not null)
            throw new InvalidOperationException($"Invalid skill program at {path}: friend event operations require one exact owning trigger.");
        var valid = effect.Op switch
        {
            SkillProgramEffectOp.DiscardAscendingMaximumAndDraw => trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied && trigger.Optional &&
                trigger.Subject is SkillProgramTriggerSubject.DamageSource or SkillProgramTriggerSubject.Owner && trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage,
            SkillProgramEffectOp.HoldUsedSuitHandAndDraw => trigger.Window == SkillProgramTriggerWindow.CardUseCommitted && trigger.Optional &&
                trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && trigger.IncludeResponseUses && trigger.Condition.Kind == SkillProgramTriggerConditionKind.CardActionFromOwnerHand,
            SkillProgramEffectOp.ConfigureFriendDuelCategories or SkillProgramEffectOp.ConfigureFriendUseSuits => trigger.Window == SkillProgramTriggerWindow.GameStarting && !trigger.Optional && trigger.Subject == SkillProgramTriggerSubject.Owner,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Invalid skill program at {path}: friend event operation lost its exact rule window, subject or predicate.");
    }
}
