namespace CardGame.Core;

internal static class MouChengYuComposition
{
    internal const string Lidan = "mou-ol:lidan", Danchi = "mou-ol:danchi";
    [ProgramCatalogValidation(1630)]
    internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.ResolveMouChengYuLidan or SkillProgramEffectOp.ResolveMouChengYuDanchi);
        if (p.Id is not (Lidan or Danchi) && !owns) return;
        static bool T(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramEffectOp op, bool optional) =>
            t.Id == id && t.Window == window && t.Optional == optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
            t.Effects is [{ Target: SkillProgramEffectTarget.Owner } e] && e.Op == op && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.Id switch
        {
            Lidan => p.Triggers is [var completed, var targets] &&
                T(completed, "alternating-use-permit", SkillProgramTriggerWindow.CardUseCompleted, SkillProgramEffectOp.ResolveMouChengYuLidan, false) &&
                completed.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && completed.IncludeResponseUses &&
                T(targets, "next-matching-targets", SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ResolveMouChengYuLidan, false) &&
                targets.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor,
            Danchi => p.Triggers is [var damage, var completed] &&
                T(damage, "nearby-damage-type", SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramEffectOp.ResolveMouChengYuDanchi, true) &&
                damage.Subject == SkillProgramTriggerSubject.Any && damage.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                damage.UsageScope == SkillUsageScope.Turn && damage.UsageLimit == 1 &&
                T(completed, "source-next-use", SkillProgramTriggerWindow.CardUseCompleted, SkillProgramEffectOp.ResolveMouChengYuDanchi, false) &&
                completed.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && completed.IncludeResponseUses,
            _ => false
        };
        if (!valid || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.Contributions.Count != 0 || p.HpSkillGrants.Count != 0)
            throw new InvalidOperationException(path + ": OL Mou Cheng Yu requires complete alternating target permissions and the actual damage-source next-use promise.");
    }
}
