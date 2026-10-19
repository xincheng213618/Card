namespace CardGame.Core;

// These operations share private entity receipts and a named, persistent cycle.
// Reject transplanted or partially reconfigured bindings at content load time.
internal static class WuZhangFeiComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Id is "wumiao:zisheng" or "wumiao:xianlue" or "wumiao:haoxian";
        if (!reserved && !p.Activations.SelectMany(a => a.Effects)
                .Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => IsOwnedOperation(e.Op))) return;
        if (!reserved || !PlainProgram(p) || !ValidBinding(p))
            throw new InvalidOperationException(path + ": Wu Zhang Fei requires the complete source-bound Use/movement triggers, private per-target observation, or native limited collection binding.");
    }

    private static bool IsOwnedOperation(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.ResolveWuZhangFeiUseSample or
        SkillProgramEffectOp.ResolveWuZhangFeiXianlue or
        SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage or
        SkillProgramEffectOp.ResolveWuZhangFeiHaoxian;

    private static bool PlainProgram(SkillProgram p) =>
        p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 &&
        p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 &&
        p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 &&
        p.BooleanStates.Count == 0 && p.Contributions.Count == 0 &&
        !p.LordSkillProjection && !p.CannotChallengeDeclarations;

    private static bool ValidBinding(SkillProgram p) => p.Id switch
    {
        "wumiao:zisheng" => p.Activations.Count == 0 && p.Triggers.Count == 2 &&
            p.Triggers.SingleOrDefault(t => t.Id == "rank-use-sample") is { } use &&
            ValidCommittedUseTrigger(use) &&
            p.Triggers.SingleOrDefault(t => t.Id == "retaliate-foreign-cards") is { } movement &&
            PlainTrigger(movement) && movement.Window == SkillProgramTriggerWindow.CardsMoved &&
            movement.Subject == SkillProgramTriggerSubject.Owner && movement.OwnerRelation is null &&
            !movement.IncludeResponseUses && movement.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
            OwnEffect(movement.Effects, SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage),
        "wumiao:xianlue" => p.Triggers.Count == 0 && p.Activations is [var observe] &&
            PlainActivation(observe, "observe-ranks", 1, null) &&
            OwnEffect(observe.Effects, SkillProgramEffectOp.ResolveWuZhangFeiXianlue),
        "wumiao:haoxian" => p.Triggers.Count == 0 && p.Activations is [var collect] &&
            PlainActivation(collect, "collect-rank-three", 0, 1) &&
            OwnEffect(collect.Effects, SkillProgramEffectOp.ResolveWuZhangFeiHaoxian),
        _ => false
    };

    internal static bool ValidCommittedUseTrigger(SkillProgramTrigger t) =>
        t.Id == "rank-use-sample" && PlainTrigger(t) &&
        t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.Subject is null &&
        t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.IncludeResponseUses &&
        t.MovementOccurrence is null && OwnEffect(t.Effects, SkillProgramEffectOp.ResolveWuZhangFeiUseSample);

    private static bool OwnEffect(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) =>
        effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;

    private static bool PlainActivation(SkillProgramActivation a, string id, int targets, int? gameLimit) =>
        a.Id == id && a.UsageGroup == id && a.MinCards == 0 && a.MaxCards == 0 &&
        a.MinTargets == targets && a.MaxTargets == targets &&
        a.TargetKind == SkillProgramTargetKind.OtherLiving && a.Condition.Kind == SkillProgramConditionKind.Always &&
        a.UsesPerTurn is null && a.UsesPerPhase is null && a.UsesPerGame == gameLimit &&
        a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && !a.SelectedCardsSameSuit &&
        !a.SelectedCardsDistinctSuits && a.EquipmentSlots.Count == 0 && !a.TargetRequiresEmptyEquipmentSlot &&
        !a.OtherCharactersOnly && !a.ContinueAfterOwnerDeath && a.MarkerCost is null &&
        a.CardCountExpression is null && a.CategoryTargetLedgerId is null && a.TargetPhaseLedgerId is null &&
        a.CardKinds.Count == 0 && a.CardSuits.Count == 0 && a.CardCategories.Count == 0;

    private static bool PlainTrigger(SkillProgramTrigger t) =>
        t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Priority == 0 &&
        t.UsageLimit is null && t.UsageScope is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.JudgmentReasons.Count == 0 && t.JudgmentSource is null && t.MinimumRank == 1 && t.MaximumRank == 13 &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive &&
        !t.MovementDiscardOnly && !t.SingleActionInstance && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded;
}

internal sealed class WuZhangFeiCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "wumiao:zisheng";
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (!WuZhangFeiComposition.ValidCommittedUseTrigger(trigger))
            throw new InvalidOperationException("Zisheng requires its exact native committed true-Use trigger, including response Uses.");
    }
}
