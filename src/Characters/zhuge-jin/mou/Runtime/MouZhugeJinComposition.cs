namespace CardGame.Core;
internal static class MouZhugeJinComposition
{
    internal const string Taozhou = "mou:taozhou", Houde = "mou:houde", Zijin = "mou:zijin";
    [ProgramCatalogValidation(1780)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouZhugeJinTaozhou or SkillProgramEffectOp.ResolveMouZhugeJinHoude or SkillProgramEffectOp.ResolveMouZhugeJinZijin;
        var ours = p.Id is Taozhou or Houde or Zijin;
        if (!ours && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(Reserved)) return;
        var valid = p.Id == Taozhou ? p.Triggers.Count == 0 && p.Activations is [{ Id: "secret-number-and-give", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null } a] &&
            a.Effects is [{ Op: SkillProgramEffectOp.ResolveMouZhugeJinTaozhou, Target: SkillProgramEffectTarget.Owner }] :
            p.Activations.Count == 0 && p.Triggers is [var t] && !t.Optional && t.Subject is null &&
            (p.Id == Houde ? t.Id == "first-qualified-target" && t.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouZhugeJinHoude, Target: SkillProgramEffectTarget.Owner }] :
                t.Id == "undamaging-true-use" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.IncludeResponseUses &&
                t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouZhugeJinZijin, Target: SkillProgramEffectTarget.Owner }]);
        if (!ours || !valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Mou Zhuge Jin requires his complete private Taozhou, first-target Houde and derived true-Use Zijin contracts.");
    }
}
