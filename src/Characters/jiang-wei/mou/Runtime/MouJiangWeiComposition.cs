namespace CardGame.Core;
internal static class MouJiangWeiAnniversaryComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.InitializeMouJiangWeiJuemou or SkillProgramEffectOp.ResolveMouJiangWeiJuemou or SkillProgramEffectOp.RestoreMouJiangWeiJuemou or SkillProgramEffectOp.ResolveMouJiangWeiFuzhan;
    internal static bool Committed(SkillProgramTrigger t) => t.Id == "trick-use" && t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.IncludeResponseUses && t.Optional &&
        t.CardCategories.SequenceEqual([SkillProgramCardCategory.Trick]) && t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouJiangWeiJuemou, Target: SkillProgramEffectTarget.Owner }];
    [ProgramCatalogValidation(1740)]
    internal static void Validate(string path, SkillProgram p)
    {
        var own = p.Id is MouJiangWeiAnniversaryRuntime.Juemou or MouJiangWeiAnniversaryRuntime.Fuzhan;
        if (!own && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        bool T(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramEffectOp op, bool optional) => t.Id == id && t.Window == window && t.Optional == optional &&
            t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } effect] && effect.Op == op;
        if (!own || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 ||
            (p.Id == MouJiangWeiAnniversaryRuntime.Juemou ? p.BooleanStates.Count != 2 || p.BooleanStates.Any(s => s.Id is not ("yin" or "modified") || s.InitialValue) || p.Triggers.Count != 5 ||
                !T(p.Triggers[0], "opening-state", SkillProgramTriggerWindow.GameStarting, SkillProgramEffectOp.InitializeMouJiangWeiJuemou, false) || !Committed(p.Triggers[1]) ||
                !T(p.Triggers[2], "modified-start", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.ResolveMouJiangWeiJuemou, true) ||
                !T(p.Triggers[3], "modified-ending", SkillProgramTriggerWindow.AfterTurnEnded, SkillProgramEffectOp.ResolveMouJiangWeiJuemou, true) ||
                !T(p.Triggers[4], "causal-self-dying", SkillProgramTriggerWindow.DyingEntering, SkillProgramEffectOp.RestoreMouJiangWeiJuemou, false)
              : p.BooleanStates.Count != 0 || p.Triggers is not [var t] || !T(t, "survived-dying", SkillProgramTriggerWindow.DyingExited, SkillProgramEffectOp.ResolveMouJiangWeiFuzhan, true) ||
                t.Subject != SkillProgramTriggerSubject.Any || t.UsageScope != SkillUsageScope.Game || t.UsageLimit != 1))
            throw new InvalidOperationException(path + ": Mou Jiang Wei requires the full native Yin/Yang, causal self-Dying recovery and limited survived-Dying modification.");
    }
}
internal sealed class MouJiangWeiAnniversaryCommittedContract : IProgramCommittedUseContract
{
    public string SkillId => MouJiangWeiAnniversaryRuntime.Juemou;
    public void ValidateTrigger(SkillProgramTrigger trigger) { if (!MouJiangWeiAnniversaryComposition.Committed(trigger)) throw new InvalidOperationException("Juemou requires the actual actor's true Trick Use, including true response Uses."); }
}
