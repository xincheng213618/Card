namespace CardGame.Core;
internal static class MouCaoShuangAnniversaryComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan or SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption or SkillProgramEffectOp.ResolveMouCaoShuangFanshi or SkillProgramEffectOp.ResolveMouCaoShuangFudou;
    internal static bool Committed(SkillProgramTrigger t) => t.Id == "use-item" && t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.IncludeResponseUses && !t.Optional &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
    [ProgramCatalogValidation(1770)]
    internal static void Validate(string path, SkillProgram p)
    {
        var own = p.Id is MouCaoShuangAnniversaryRuntime.Jianzhuan or MouCaoShuangAnniversaryRuntime.Fanshi or MouCaoShuangAnniversaryRuntime.Fudou;
        if (!own && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        bool T(SkillProgramTrigger t, string id, SkillProgramTriggerWindow w, SkillProgramEffectOp op, bool optional) => t.Id == id && t.Window == w && t.Optional == optional &&
            t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        if (!own || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            (p.Id == MouCaoShuangAnniversaryRuntime.Jianzhuan ? p.Triggers.Count != 2 || !Committed(p.Triggers[0]) || !T(p.Triggers[1], "remove-completed-item", SkillProgramTriggerWindow.PlayEnding, SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption, false) || p.Triggers[1].Subject != SkillProgramTriggerSubject.Owner
            : p.Id == MouCaoShuangAnniversaryRuntime.Fanshi ? p.Triggers is not [var a] || !T(a, "awaken-remaining", SkillProgramTriggerWindow.TurnEnding, SkillProgramEffectOp.ResolveMouCaoShuangFanshi, false) || a.Subject != SkillProgramTriggerSubject.Owner || a.UsageScope != SkillUsageScope.Game || a.UsageLimit != 1
            : p.Triggers is not [var b] || !T(b, "single-real-target", SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ResolveMouCaoShuangFudou, true) || b.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor))
            throw new InvalidOperationException(path + ": Mou Cao Shuang requires actual Use items, completed-Play random deletion, real Ending awakening and the unique finalized-target Fudou.");
    }
}
internal sealed class MouCaoShuangAnniversaryCommittedContract : IProgramCommittedUseContract
{
    public string SkillId => MouCaoShuangAnniversaryRuntime.Jianzhuan;
    public void ValidateTrigger(SkillProgramTrigger t) { if (!MouCaoShuangAnniversaryComposition.Committed(t)) throw new InvalidOperationException("Jianzhuan requires the actor's actual Use including native true response Uses."); }
}
