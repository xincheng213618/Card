namespace CardGame.Core;

internal static class MouLiuChengComposition
{
    internal const string Lueying = "mou-mobile:lueying", Yingwu = "mou-mobile:yingwu";
    internal static bool IsSkill(string id) => id is Lueying or Yingwu;
    private static bool Trigger(SkillProgramTrigger t) => !t.Optional && t.Priority == 0 && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
        t.Subject is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 && t.Suits.Count == 0 &&
        t.RequireDamageSource is null && t.RequireNoCardConversion is null && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.MarkerCost is null && t.UsageScope is null && t.UsageLimit is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 && t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.GainPhaseQualification is null && t.ChoiceGroup is null && t.OrderGroup is null && t.DamageOccurrence is null && t.MovementOccurrence is null &&
        !t.DeferredTurnEndOnly && !t.NoDyingAtActivation && !t.OnlyDesignatedCardTargets && !t.AllowNoEventTarget;
    internal static bool IsProgram(SkillProgram p) => IsSkill(p.Id) && p.Activations.Count == 0 && p.ViewAs.Count == 0 &&
        p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 &&
        p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0 &&
        !p.LordSkillProjection && !p.CannotChallengeDeclarations && p.Triggers.Count == 2 &&
        p.Triggers.SingleOrDefault(t => t.Id == "after-designation") is { } gain && Trigger(gain) &&
        gain.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && !gain.SingleActionInstance && !gain.IncludeResponseUses && Exact(gain, SkillProgramEffectOp.AccrueMouLiuChengChui) &&
        p.Triggers.SingleOrDefault(t => t.Id == "after-use") is { } complete && Trigger(complete) &&
        complete.Window == SkillProgramTriggerWindow.CardUseCompleted && complete.SingleActionInstance && complete.IncludeResponseUses && Exact(complete, SkillProgramEffectOp.ResolveMouLiuChengCompletion);
    private static bool Exact(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.Amount == 0 && e.TargetReference is null && e.ActorReference is null &&
        e.CardOwnerRef is null && e.ChooserRef is null && e.SourceBind is null && e.ResultBind is null && e.NumberExpression is null && e.Zones.Count == 0 && !e.AllowDecline && !e.AwaitMovementTriggers;
    [ProgramCatalogValidation(960)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.AccrueMouLiuChengChui or SkillProgramEffectOp.ResolveMouLiuChengCompletion);
        if ((IsSkill(p.Id) || reserved) && !IsProgram(p)) throw new InvalidOperationException(path + ": mobile Liu Cheng requires both exact designation and completed true-use clauses, independent named phase quotas and shared actual Chui payments.");
    }
}
