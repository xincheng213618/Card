namespace CardGame.Core;

internal static class MouLuBuMobileComposition
{
    internal const string Wushuang = "mou-mobile:wushuang", Liyu = "mou-mobile:liyu";
    [ProgramCatalogValidation(1380)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        bool Reserved(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouLuBuWushuangBonus or SkillProgramEffectOp.ResolveMouLuBuLiyu;
        if (p.Id is not (Wushuang or Liyu))
        { if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(Reserved)) throw new InvalidOperationException(path + ": mobile Lu Bu operations require their original complete source."); return; }
        var bonus = p.Id == Wushuang;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Triggers is not [var t] ||
            t.Id != (bonus ? "no-response-damage" : "obtain-draw-and-choose") || t.Subject != SkillProgramTriggerSubject.DamageSource ||
            t.Window != (bonus ? SkillProgramTriggerWindow.BeforeDamageApplied : SkillProgramTriggerWindow.AfterDamageApplied) || t.Optional == bonus ||
            !bonus && t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.UsageLimit is not null ||
            t.Effects is not [var e] || e.Op != (bonus ? SkillProgramEffectOp.ResolveMouLuBuWushuangBonus : SkillProgramEffectOp.ResolveMouLuBuLiyu) ||
            e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": mobile Lu Bu requires the complete canonical damage bindings.");
        if (bonus ? p.CardPolicies is not [var slash, var duel] || slash.Kind != SkillProgramCardPolicyKind.MinimumResponseCount || slash.Value != 2 ||
            !slash.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) || !slash.RequiredCardKinds.SequenceEqual([CardKind.Dodge]) ||
            duel.Kind != SkillProgramCardPolicyKind.MinimumResponseCount || duel.Value != 2 || !duel.CardKinds.SequenceEqual([CardKind.Duel]) || !duel.RequiredCardKinds.SequenceEqual([CardKind.Slash]) : p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": mobile Wushuang must retain both native double-response policies.");
    }
}
