namespace CardGame.Core;
internal static class MouCaoHongComposition
{
    internal const string Yingjia = "mou:yingjia", Xianju = "mou:xianju";
    [ProgramCatalogValidation(1810)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouCaoHongYingjia or SkillProgramEffectOp.ResolveMouCaoHongXianju or SkillProgramEffectOp.SettleMouCaoHongXianju;
        var ours = p.Id is Yingjia or Xianju;
        if (!ours && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(Reserved)) return;
        var valid = p.Id == Yingjia ? p.Activations.Count == 0 && p.Triggers is [{ Id: "used-other-targets", Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: false, IncludeResponseUses: false } t] &&
            t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouCaoHongYingjia, Target: SkillProgramEffectTarget.Owner }] :
            p.Activations is [{ Id: "obtain-in-range", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, UsesPerPhase: 1 } a] && a.Effects is [{ Op: SkillProgramEffectOp.ResolveMouCaoHongXianju, Target: SkillProgramEffectTarget.Owner }] &&
            p.Triggers is [{ Id: "issued-play-end", Window: SkillProgramTriggerWindow.PlayEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: false } end] && end.Effects is [{ Op: SkillProgramEffectOp.SettleMouCaoHongXianju, Target: SkillProgramEffectTarget.Owner }];
        if (!ours || !valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Mou Cao Hong requires his true target-use, complete range obtain and issued actual-Play ending cost contracts.");
    }
}
