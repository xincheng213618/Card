namespace CardGame.Core;

internal static class MouChenGongComposition
{
    internal const string Mingce = "mou-mobile:mingce", Zhichi = "mou-mobile:zhichi";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouChenGongMingceGift or SkillProgramEffectOp.ResolveMouChenGongMingceRelease;
    private static bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.Amount == 0 && e.ActorReference is null && e.TargetReference is null &&
        e.CardOwnerRef is null && e.ChooserRef is null && e.SourceBind is null && e.ResultBind is null && e.NumberExpression is null && e.Zones.Count == 0 && !e.AllowDecline && !e.AwaitMovementTriggers;
    private static bool Empty(SkillProgram p) => p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 &&
        p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.Contributions.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    private static bool Unfiltered(SkillProgramTrigger t) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.OwnerRelation is null && t.UsageScope is null && t.UsageLimit is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.GainPhaseQualification is null &&
        t.MarkerCost is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 && t.JudgmentReasons.Count == 0 && t.Suits.Count == 0 &&
        t.SourceSkillId is null && t.SourceViewAsId is null && t.JudgmentSource is null && t.MovementOccurrence is null && t.HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerEvent &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive && t.MinimumRank == 1 && t.MaximumRank == 13 && t.ChoiceGroup is null && t.OrderGroup is null &&
        t.RequireDamageSource is null && t.RequireNoCardConversion is null && !t.AllowNoEventTarget && !t.IncludeResponseUses && !t.SingleActionInstance &&
        !t.NoDyingAtActivation && !t.DeferredTurnEndOnly && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded && !t.EvaluateConditionAtResolution &&
        !t.IgnoreOwnSkillMovements && !t.MovementDiscardOnly && t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Other;
    internal static bool IsMingce(SkillProgram p) => p.Id == Mingce && Empty(p) && p.BooleanStates.Count == 0 && p.Activations is [var a] &&
        a.Id == "gift-and-plan" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 &&
        a.TargetKind == SkillProgramTargetKind.OtherLiving && a.UsesPerPhase == 1 && a.UsesPerTurn is null && a.UsesPerGame is null && a.Condition.Kind == SkillProgramConditionKind.Always &&
        a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.UsageGroup == a.Id && a.MarkerCost is null && !a.ContinueAfterOwnerDeath && !a.OtherCharactersOnly &&
        !a.SelectedCardsSameSuit && !a.SelectedCardsDistinctSuits && !a.TargetRequiresEmptyEquipmentSlot && a.EquipmentSlots.Count == 0 && a.CardCountExpression is null &&
        a.CardKinds.Count == 0 && a.CardSuits.Count == 0 && a.CardCategories.Count == 0 && a.CategoryTargetLedgerId is null && a.TargetPhaseLedgerId is null &&
        a.Effects is [var gift] && Plain(gift, SkillProgramEffectOp.ResolveMouChenGongMingceGift) && p.Triggers is [var t] &&
        t.Id == "release-plan" && t.Window == SkillProgramTriggerWindow.PlayPhaseStarting && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.DamageOccurrence is null && Unfiltered(t) &&
        t.Effects is [var release] && Plain(release, SkillProgramEffectOp.ResolveMouChenGongMingceRelease);
    internal static bool IsZhichi(SkillProgram p) => p.Id == Zhichi && Empty(p) && p.Activations.Count == 0 && p.BooleanStates is [var state] &&
        state.Id == "zhichi-issued-this-turn" && !state.InitialValue && state.ResetScope == SkillProgramStateResetScope.Turn && state.Visibility == SkillProgramStateVisibility.Public &&
        state.ReacquirePolicy == SkillProgramStateReacquirePolicy.PreserveUntilGameEnd &&
        p.Triggers.Count == 2 && p.Triggers[0] is { Id: "arm-protection", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Owner, Optional: false } arm &&
        arm.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && arm.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && Unfiltered(arm) &&
        arm.Effects is [{ StateId: "zhichi-issued-this-turn", BooleanValue: true } set] && Plain(set, SkillProgramEffectOp.SetBooleanState) &&
        p.Triggers[1] is { Id: "prevent-later-damage", Window: SkillProgramTriggerWindow.BeforeDamageApplied, Subject: SkillProgramTriggerSubject.DamageTarget, Optional: false } prevent &&
        prevent.DamageOccurrence is null && prevent.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && Unfiltered(prevent) &&
        prevent.Effects is [var e] && Plain(e, SkillProgramEffectOp.PreventCurrentDamage);
    // SkillProgramCatalog validates immutable definition shape once; runtime keeps dynamic receipts and hashes.
    [ProgramCatalogValidation(980)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if ((p.Id == Mingce || p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) && !IsMingce(p))
            throw new InvalidOperationException(path + ": Mingce requires the complete actual-Play paid HE gift, recipient native loss/draw, permanent Ce and later damage-then-clear contract.");
        if (p.Id == Zhichi && !IsZhichi(p)) throw new InvalidOperationException(path + ": Zhichi requires its real after-damage issued protection and native before-damage prevention.");
    }
}
