namespace CardGame.Core;

internal static class MouXiahouShiComposition
{
    internal const string Yanyu = "mou-mobile:yanyu", Qiaoshi = "mou-mobile:qiaoshi";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard or SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport or SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi;
    private static bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.Amount == 0 && e.ActorReference is null && e.TargetReference is null && e.SourceRef is null &&
        e.CardOwnerRef is null && e.ChooserRef is null && e.SourceBind is null && e.ResultBind is null && e.NumberExpression is null && e.StateId is null &&
        e.Zones.Count == 0 && e.CardKinds.Count == 0 && e.CardCategories.Count == 0 && e.Options.Count == 0 && !e.AllowDecline && !e.AwaitMovementTriggers;
    private static bool Empty(SkillProgram p) => p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 &&
        p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.Contributions.Count == 0 &&
        p.BooleanStates.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    private static bool Unfiltered(SkillProgramTrigger t, bool damage) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.OwnerRelation is null && t.UsageScope is null && t.UsageLimit is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.GainPhaseQualification is null &&
        t.MarkerCost is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 && t.JudgmentReasons.Count == 0 && t.Suits.Count == 0 &&
        t.SourceSkillId is null && t.SourceViewAsId is null && t.JudgmentSource is null && t.MovementOccurrence is null && t.HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerEvent &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive && t.MinimumRank == 1 && t.MaximumRank == 13 && t.ChoiceGroup is null && t.OrderGroup is null &&
        t.RequireDamageSource == (damage ? true : null) && t.RequireNoCardConversion is null && !t.AllowNoEventTarget && !t.IncludeResponseUses && !t.SingleActionInstance &&
        !t.NoDyingAtActivation && !t.DeferredTurnEndOnly && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded && !t.EvaluateConditionAtResolution &&
        !t.IgnoreOwnSkillMovements && !t.MovementDiscardOnly && t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Other;
    internal static bool IsYanyu(SkillProgram p) => p.Id == Yanyu && Empty(p) && p.Activations is [var a] &&
        a.Id == "discard-slash" && a.MinCards == 1 && a.MaxCards == 1 && a.MinTargets == 0 && a.MaxTargets == 0 &&
        a.TargetKind == SkillProgramTargetKind.AnyLiving && a.UsesPerPhase == 2 && a.UsesPerTurn is null && a.UsesPerGame is null && a.Condition.Kind == SkillProgramConditionKind.Always &&
        a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && a.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
        a.UsageGroup == a.Id && a.MarkerCost is null && !a.ContinueAfterOwnerDeath && !a.OtherCharactersOnly && !a.SelectedCardsSameSuit && !a.SelectedCardsDistinctSuits &&
        !a.TargetRequiresEmptyEquipmentSlot && a.EquipmentSlots.Count == 0 && a.CardCountExpression is null && a.CardSuits.Count == 0 && a.CardCategories.Count == 0 &&
        a.CategoryTargetLedgerId is null && a.TargetPhaseLedgerId is null && a.Effects is [var discard] && Plain(discard, SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard) &&
        p.Triggers is [var t] && t.Id == "support-at-play-ending" && t.Window == SkillProgramTriggerWindow.PlayEnding && t.Subject == SkillProgramTriggerSubject.Owner &&
        t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.DamageOccurrence is null && Unfiltered(t, false) &&
        t.Effects is [var support] && Plain(support, SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport);
    internal static bool IsQiaoshi(SkillProgram p) => p.Id == Qiaoshi && Empty(p) && p.Activations.Count == 0 && p.Triggers is [var t] &&
        t.Id == "foreign-damage-source-choice" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Owner &&
        !t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && Unfiltered(t, true) &&
        t.Effects is [var e] && Plain(e, SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi);
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var ops = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).ToArray();
        if ((p.Id == Yanyu || ops.Any(e => e.Op is SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard or SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport)) && !IsYanyu(p))
            throw new InvalidOperationException(path + ": Yanyu requires its complete actual-Play twice native discard and whole-actual-turn triple support contract.");
        if ((p.Id == Qiaoshi || ops.Any(e => e.Op == SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi)) && !IsQiaoshi(p))
            throw new InvalidOperationException(path + ": Qiaoshi requires its real foreign damage-source choice, native recovery and subsequent source draw contract.");
    }
}
