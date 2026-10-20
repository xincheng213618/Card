namespace CardGame.Core;
internal static class MouGanNingMobileComposition
{
    internal const string Qixi = "mou-mobile:qixi", Fenwei = "mou-mobile:fenwei";
    [ProgramCatalogValidation(1410)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Where(e => e.Op is SkillProgramEffectOp.ResolveMouGanNingQixi or SkillProgramEffectOp.ResolveMouGanNingFenwei or SkillProgramEffectOp.ResolveMouGanNingWeiTarget).ToArray();
        if (p.Id is not (Qixi or Fenwei)) { if (reserved.Length != 0) throw new InvalidOperationException(path + ": mobile Gan Ning operations require their own source."); return; }
        var q = p.Id == Qixi;
        if (p.Activations is not [var a] || a.Id != (q ? "guess-suits" : "place-wei") || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != (q ? 1 : 0) || a.MaxTargets != (q ? 1 : 0) ||
            a.Effects is not [var e] || e.Op != (q ? SkillProgramEffectOp.ResolveMouGanNingQixi : SkillProgramEffectOp.ResolveMouGanNingFenwei) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 ||
            (q ? p.Triggers.Count != 0 || a.UsesPerPhase != 1 : a.UsesPerGame != 1 || p.Triggers is not [var t] || t.Id != "spend-wei" || t.Window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Optional || t.Effects is not [var effect] || effect.Op != SkillProgramEffectOp.ResolveMouGanNingWeiTarget || effect.Target != SkillProgramEffectTarget.Owner || effect.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path + ": mobile Gan Ning requires complete guess and physical Wei bindings.");
    }
}
