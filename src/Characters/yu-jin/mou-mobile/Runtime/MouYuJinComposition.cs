namespace CardGame.Core;

internal static class MouYuJinComposition
{
    internal const string Xiayuan = "mou-mobile:xiayuan", Jieyue = "mou-mobile:jieyue";
    private static bool Empty(SkillProgram p) => p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 &&
        p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 &&
        p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    private static bool Plain(SkillProgramEffect e, SkillProgramEffectOp op, SkillProgramEffectTarget target = SkillProgramEffectTarget.Owner) =>
        e.Op == op && e.Target == target && e.TargetReference is null && e.ActorReference is null &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.NumberExpression is null && e.CardOwnerRef is null && e.ChooserRef is null;
    private static bool Custom(SkillProgramEffect e, SkillProgramEffectOp op) => Plain(e, op) && e.Amount == 0 &&
        e.SourceBind is null && e.ResultBind is null && e.Zones.Count == 0 && !e.AllowDecline && !e.AwaitMovementTriggers;
    private static bool Trigger(SkillProgramTrigger t) => t.Optional && t.Priority == 0 && t.MarkerCost is null &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.OwnerRelation is null && t.MovementOccurrence is null &&
        t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 && t.CardKinds.Count == 0 &&
        t.DamageCardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.GainPhaseQualification is null && t.RequireDamageSource is null &&
        t.RequireNoCardConversion is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.AllowNoEventTarget &&
        !t.NoDyingAtActivation && !t.OnlyDesignatedCardTargets && !t.DeferredTurnEndOnly && !t.IncludeResponseUses && !t.SingleActionInstance &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own;
    internal static bool IsXiayuan(SkillProgram p) => p.Id == Xiayuan && Empty(p) && p.Triggers is [var t] && Trigger(t) &&
        t.Id == "restore-after-armor-loss" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
        t.Subject == SkillProgramTriggerSubject.Any && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
        t.UsageScope == SkillUsageScope.Round && t.UsageLimit == 1 && t.Effects is [var first, var selection, var cost, var last] &&
        Custom(first, SkillProgramEffectOp.ResolveMouYuJinXiayuan) && Custom(last, SkillProgramEffectOp.ResolveMouYuJinXiayuan) &&
        Plain(selection, SkillProgramEffectOp.SelectOwnedCards) && selection.MinimumCards == 2 && selection.MaximumCards == 2 &&
        selection.Amount == 0 && selection.RequireExactCount is null && !selection.AllowDecline && selection.ResultBind == "xiayuan-cost" &&
        selection.SourceBind is null && selection.Zones.SequenceEqual([CardZoneKind.Hand]) && selection.CardKinds.Count == 0 && selection.Suits.Count == 0 &&
        Plain(cost, SkillProgramEffectOp.MoveBoundCards) && cost.SourceBind == "xiayuan-cost" && cost.ResultBind is null &&
        cost.Destination == SkillProgramCardDestination.DiscardPile && cost.AwaitMovementTriggers && cost.ExceptBind is null;
    internal static bool IsJieyue(SkillProgram p) => p.Id == Jieyue && Empty(p) && p.Triggers is [var t] && Trigger(t) &&
        t.Id == "ending-armor-and-gift" && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner &&
        t.DamageOccurrence is null && t.UsageScope is null && t.UsageLimit is null &&
        t.Effects is [var target, var first, var grant, var last] && Plain(target, SkillProgramEffectOp.SelectTarget) &&
        target.TargetKind == SkillProgramTargetKind.OtherLiving && target.SourceBind is null && target.ResultBind is null &&
        Custom(first, SkillProgramEffectOp.ResolveMouYuJinJieyue) && Custom(last, SkillProgramEffectOp.ResolveMouYuJinJieyue) &&
        Plain(grant, SkillProgramEffectOp.GrantArmor, SkillProgramEffectTarget.SelectedTarget) && grant.Amount == 1 &&
        grant.SourceBind is null && grant.ResultBind is null;
    [ProgramCatalogValidation(950)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.ResolveMouYuJinXiayuan or SkillProgramEffectOp.ResolveMouYuJinJieyue);
        if ((reserved || p.Id is Xiayuan or Jieyue) && !IsXiayuan(p) && !IsJieyue(p))
            throw new InvalidOperationException($"Invalid skill program at {path}: mobile Yu Jin requires the complete original Armor-payment, exact Hand2 cost or Ending grant-before-independent-gift contract.");
    }
}
