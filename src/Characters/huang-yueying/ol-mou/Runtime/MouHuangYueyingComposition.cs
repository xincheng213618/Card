namespace CardGame.Core;

internal sealed class MouBingcaiCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "mou-ol:bingcai";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "first-basic-recast" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Subject is not null ||
            !t.Optional || !t.IncludeResponseUses || !t.SingleActionInstance || !t.AllowNoEventTarget ||
            t.CardCategories is not [SkillProgramCardCategory.Basic] || t.CardKinds.Count != 0 ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.DamageOccurrence is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouHuangYueyingBingcai,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Bingcai requires one optional global Basic true-use observer, including native response Uses.");
    }
}

internal static class MouHuangYueyingComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        bool HasOwnOperation(ProgramInstructionFeatures features) =>
            features.HasOperation(SkillProgramEffectOp.ResolveMouHuangYueyingLixian) ||
            features.HasOperation(SkillProgramEffectOp.ResolveMouHuangYueyingBingcai);
        if (p.Id is not ("mou-ol:lixian" or "mou-ol:bingcai") &&
            !p.Activations.Any(a => HasOwnOperation(ProgramInstructionResolver.Default.Features(a))) &&
            !p.Triggers.Any(t => HasOwnOperation(ProgramInstructionResolver.Default.Features(t)))) return;
        if (p.Id is not ("mou-ol:lixian" or "mou-ol:bingcai") || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.Count != (p.Id == "mou-ol:lixian" ? 3 : 1) || p.Triggers.Any(HasExtraFilter))
            throw new InvalidOperationException(path + ": OL Mou Huang Yueying requires her complete phase/damage and global first-Basic contracts.");
        if (p.Id == "mou-ol:bingcai") { new MouBingcaiCommittedUseContract().ValidateTrigger(p.Triggers.Single()); return; }
        foreach (var t in p.Triggers)
        {
            var expected = t.Id switch
            {
                "after-damage" => SkillProgramTriggerWindow.AfterDamageApplied,
                "unlocked-preparation" => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                "unlocked-ending" => SkillProgramTriggerWindow.TurnEnding,
                _ => throw new InvalidOperationException(path + ": Lixian cannot invent a trigger boundary.")
            };
            if (t.Window != expected || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional ||
                t.OwnerRelation is not null || t.SourceSkillId is not null || t.IncludeResponseUses || t.SingleActionInstance ||
                t.AllowNoEventTarget || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
                t.DamageOccurrence != (expected == SkillProgramTriggerWindow.AfterDamageApplied ? SkillProgramDamageOccurrence.PerDamage : (SkillProgramDamageOccurrence?)null) ||
                t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouHuangYueyingLixian,
                    Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Lixian requires one real material Use after each damage or actually unlocked owner phase.");
        }
    }

    private static bool HasExtraFilter(SkillProgramTrigger t) =>
        t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Priority != 0 || t.UsageLimit is not null || t.UsageScope is not null ||
        t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.OrderGroup is not null ||
        t.MarkerCost is not null || t.SourceViewAsId is not null || t.DamageCardKinds.Count != 0 || t.Suits.Count != 0 ||
        t.MinimumRank != 1 || t.MaximumRank != 13 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null ||
        t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
        t.ExcludedReasons.Count != 0 || t.MovementOccurrence is not null || t.GainPhaseQualification is not null ||
        t.EvaluateConditionAtResolution || t.NoDyingAtActivation || t.IgnoreOwnSkillMovements || t.DeferredTurnEndOnly ||
        t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
        t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
        t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || t.MovementDiscardOnly;
}
