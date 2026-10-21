namespace CardGame.Core;
internal static class MouXuShengComposition
{
    internal const string Qinqiang = "mou:qinqiang", Yizhen = "mou:yizhen";
    internal static bool Committed(SkillProgramTrigger t) => t.Id == "true-hand-use" && t.Window == SkillProgramTriggerWindow.CardUseCommitted &&
        t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.Subject is null && t.Optional && t.IncludeResponseUses && !t.SingleActionInstance &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 &&
        t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouXuShengQinqiang, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
    [ProgramCatalogValidation(1710)]
    internal static void Validate(string path, SkillProgram p)
    {
        var ours = p.Id is Qinqiang or Yizhen;
        if (!ours && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMouXuShengQinqiang or SkillProgramEffectOp.ResolveMouXuShengYizhen)) return;
        if (!ours || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Triggers is not [var t] || !(p.Id == Qinqiang ? Committed(t) : t.Id == "mutual-private-hand" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                t.Subject == SkillProgramTriggerSubject.Owner && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && t.RequireDamageSource == true &&
                t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouXuShengYizhen, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]))
            throw new InvalidOperationException(path + ": Mou Xu Sheng requires his full true-Use and sourced-damage private-Hand contracts.");
    }
}
internal sealed class MouXuShengCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => MouXuShengComposition.Qinqiang;
    public void ValidateTrigger(SkillProgramTrigger trigger)
    { if (!MouXuShengComposition.Committed(trigger)) throw new InvalidOperationException("Qinqiang requires its exact native committed true-Use binding."); }
}
