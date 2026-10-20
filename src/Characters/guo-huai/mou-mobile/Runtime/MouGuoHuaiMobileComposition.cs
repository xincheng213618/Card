namespace CardGame.Core;
internal static class MouGuoHuaiMobileComposition
{
    internal const string Jingce = "mou-mobile:jingce";
    [ProgramCatalogValidation(1420)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        bool Own(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart or SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction or SkillProgramEffectOp.ResolveMouGuoHuaiJingceReward;
        if (p.Id != Jingce) { if (p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(Own)) throw new InvalidOperationException(path + ": Jingce operations require their original source."); return; }
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.Triggers.Count != 3) throw new InvalidOperationException(path + ": Jingce requires the complete three native bindings.");
        foreach (var t in p.Triggers)
        {
            var expected = t.Id switch { "start-jingce" => (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart), "predict-jingce" => (SkillProgramTriggerWindow.AfterTurnEnded, SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction), "verify-first-gain" => (SkillProgramTriggerWindow.CardsGained, SkillProgramEffectOp.ResolveMouGuoHuaiJingceReward), _ => throw new InvalidOperationException(path + ": unexpected Jingce binding.") };
            if (t.Window != expected.Item1 || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.Effects is not [var e] || e.Op != expected.Item2 || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always) throw new InvalidOperationException(path + ": Jingce requires its complete source-bound native cursor.");
        }
    }
}
