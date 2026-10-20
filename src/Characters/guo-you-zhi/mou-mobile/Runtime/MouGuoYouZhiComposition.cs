namespace CardGame.Core;
internal static class MouGuoYouZhiComposition
{
    internal const string Bique = "mou-mobile:bique", Zhongchun = "mou-mobile:zhongchun";
    [ProgramCatalogValidation(1460)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        bool Own(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouGuoYouZhiBique or SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun;
        if (p.Id is not (Bique or Zhongchun))
        { if (p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(Own)) throw new InvalidOperationException(path + ": Guo You Zhi requires its own source."); return; }
        if (p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0) throw new InvalidOperationException(path + ": unexpected Guo You Zhi policy.");
        if (p.Id == Bique)
        {
            if (p.Triggers.Count != 0 || p.Activations is not [var a] || a.Id != "inspect-and-give" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.Effects is not [var e] || e.Op != SkillProgramEffectOp.ResolveMouGuoYouZhiBique || e.Target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException(path + ": Bique requires one real private inspection per Play.");
        }
        else
        {
            if (p.Activations.Count != 0 || p.Triggers.Count != 2 || p.Triggers.Select(t => t.Id).Distinct().Count() != 2) throw new InvalidOperationException(path + ": Zhongchun requires both actual Ending bindings.");
            foreach (var t in p.Triggers)
                if (t.Id is not ("own-ending" or "other-ending") || t.Window != SkillProgramTriggerWindow.TurnEnding || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || t.TurnOwnerScope != (t.Id == "own-ending" ? SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving) || t.Effects is not [var e] || e.Op != SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun || e.Target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException(path + ": Zhongchun must preserve the real Ending actor.");
        }
    }
}
