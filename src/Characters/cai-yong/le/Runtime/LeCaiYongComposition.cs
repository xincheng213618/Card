namespace CardGame.Core;

// Entity marks and after-Use receipts belong to these complete printed bindings.
internal static class LeCaiYongComposition
{
    [ProgramCatalogValidation(910)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Id is "le:jiaowei" or "le:feibai";
        var operations = p.Activations.SelectMany(a => a.Effects)
            .Concat(p.Triggers.SelectMany(t => t.Effects));
        if (!reserved && !operations.Any(e => e.Op is SkillProgramEffectOp.InitializeLeCaiYongJiaowei or
                SkillProgramEffectOp.ResolveLeCaiYongFeibai)) return;
        if (!IsJiaoweiProgram(p) && !IsFeibaiProgram(p))
            throw new InvalidOperationException(path + ": Le Cai Yong requires its complete initial-entity or native after-Use binding.");
    }

    internal static bool IsJiaoweiProgram(SkillProgram p) =>
        p.Id == "le:jiaowei" && PlainProgram(p) && p.Triggers is [var t] &&
        t.Id == "mark-initial-entities" && PlainTrigger(t) &&
        t.Window == SkillProgramTriggerWindow.GameStarting &&
        t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && !t.Optional &&
        !t.IncludeResponseUses && OwnEffect(t, SkillProgramEffectOp.InitializeLeCaiYongJiaowei);

    internal static bool IsFeibaiProgram(SkillProgram p) =>
        p.Id == "le:feibai" && PlainProgram(p) && p.Triggers is [var t] && IsFeibaiTrigger(t);

    internal static bool IsFeibaiTrigger(SkillProgramTrigger t) =>
        t.Id == "after-use" && PlainTrigger(t) && t.Window == SkillProgramTriggerWindow.CardUseCompleted &&
        t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
        t.Optional && t.IncludeResponseUses && OwnEffect(t, SkillProgramEffectOp.ResolveLeCaiYongFeibai);

    private static bool PlainProgram(SkillProgram p) =>
        p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 &&
        p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 &&
        p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 &&
        p.BooleanStates.Count == 0 && p.Contributions.Count == 0 &&
        !p.LordSkillProjection && !p.CannotChallengeDeclarations;

    private static bool OwnEffect(SkillProgramTrigger t, SkillProgramEffectOp op) =>
        t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] &&
        e.Op == op;

    private static bool PlainTrigger(SkillProgramTrigger t) =>
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Priority == 0 &&
        t.UsageLimit is null && t.UsageScope is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.JudgmentReasons.Count == 0 && t.JudgmentSource is null && t.MinimumRank == 1 && t.MaximumRank == 13 &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive && t.MovementOccurrence is null &&
        !t.MovementDiscardOnly && !t.SingleActionInstance && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded;
}
