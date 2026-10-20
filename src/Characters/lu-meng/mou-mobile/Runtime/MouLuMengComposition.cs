namespace CardGame.Core;

internal static class MouLuMengComposition
{
    internal const string Keji = "mou-mobile:keji", Dujiang = "mou-mobile:dujiang", Duojing = "mou-mobile:duojing";
    private static bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.TargetReference is null && e.ActorReference is null && e.NumberExpression is null && e.SourceBind is null && e.ResultBind is null &&
        e.Condition.Kind == SkillProgramConditionKind.Always;
    private static bool Empty(SkillProgram p) => p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 &&
        p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 &&
        p.BooleanStates.Count == 0 && p.Contributions.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    internal static bool IsKeji(SkillProgram p) => p.Id == Keji && Empty(p) && p.Triggers.Count == 0 && p.Activations.Count == 2 &&
        p.Activations.All(a => a.Id is "discard-hand" or "lose-hp") && p.Activations.Select(a => a.Id).Distinct().Count() == 2 &&
        p.Activations.All(a => a.UsageGroup == a.Id && a.MinCards == (a.Id == "discard-hand" ? 1 : 0) && a.MaxCards == a.MinCards &&
            !a.OtherCharactersOnly && a.CardKinds.Count == 0 && a.CardSuits.Count == 0 && a.CardCategories.Count == 0 &&
            a.EquipmentSlots.Count == 0 && a.CardCountExpression is null && a.CategoryTargetLedgerId is null && a.TargetPhaseLedgerId is null &&
            a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.MinTargets == 0 && a.MaxTargets == 0 &&
            a.TargetKind == SkillProgramTargetKind.AnyLiving && a.UsesPerPhase == 1 && a.UsesPerTurn is null && a.UsesPerGame is null &&
            a.MarkerCost is null && !a.ContinueAfterOwnerDeath && a.Condition.Kind == SkillProgramConditionKind.Always &&
            a.Effects is [var first, var cost, var returned, var armor] && Plain(first, SkillProgramEffectOp.ResolveMouLuMengKeji) &&
            Plain(returned, SkillProgramEffectOp.ResolveMouLuMengKeji) && Plain(armor, SkillProgramEffectOp.GrantArmor) &&
            armor.Amount == (a.Id == "discard-hand" ? 1 : 2) &&
            cost.Amount == 1 && (a.Id == "discard-hand" ? Plain(cost, SkillProgramEffectOp.DiscardSelected) : Plain(cost, SkillProgramEffectOp.LoseHp)));
    private static bool TriggerBase(SkillProgramTrigger t) => !t.Optional && t.Priority == 0 && t.MarkerCost is null &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.DamageOccurrence is null && t.MovementOccurrence is null &&
        t.SourceZones.Count == 0 && t.MovementReasons.Count == 0 && t.DamageCardKinds.Count == 0 && t.CardCategories.Count == 0 &&
        t.SourceSkillId is null && t.SourceViewAsId is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.GainPhaseQualification is null && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        t.ChoiceGroup is null && t.OrderGroup is null && !t.AllowNoEventTarget && !t.NoDyingAtActivation && !t.OnlyDesignatedCardTargets &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && !t.DeferredTurnEndOnly;
    internal static bool IsDujiang(SkillProgram p) => p.Id == Dujiang && Empty(p) && p.Activations.Count == 0 && p.Triggers is [var t] &&
        TriggerBase(t) && t.Id == "armor-awakening" && t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
        t.CardKinds.Count == 0 && !t.SingleActionInstance && !t.IncludeResponseUses &&
        t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && t.UsageScope == SkillUsageScope.Game && t.UsageLimit == 1 &&
        t.Effects is [var awake, var grant] && Plain(awake, SkillProgramEffectOp.ResolveMouLuMengDujiang) &&
        Plain(grant, SkillProgramEffectOp.GrantSkills) && grant.SkillIds.SequenceEqual([Duojing]);
    internal static bool IsDuojing(SkillProgram p) => p.Id == Duojing && Empty(p) && p.Activations.Count == 0 && p.Triggers is [var t] &&
        TriggerBase(t) && t.Id == "after-slash-targets" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
        t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.SingleActionInstance && !t.IncludeResponseUses &&
        t.UsageScope is null && t.UsageLimit is null && t.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
        t.Effects is [var e] && Plain(e, SkillProgramEffectOp.ResolveMouLuMengDuojing);
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.ResolveMouLuMengKeji or SkillProgramEffectOp.ResolveMouLuMengDujiang or SkillProgramEffectOp.ResolveMouLuMengDuojing);
        if ((reserved || p.Id is Keji or Dujiang or Duojing) && !IsKeji(p) && !IsDujiang(p) && !IsDuojing(p))
            throw new InvalidOperationException($"Invalid skill program at {path}: mobile Lu Meng requires its complete exact cost, awakening or paid Slash-target contract.");
    }
}
