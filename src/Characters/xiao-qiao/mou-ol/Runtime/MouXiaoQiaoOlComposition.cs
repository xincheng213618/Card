namespace CardGame.Core;

internal static class MouXiaoQiaoOlComposition
{
    [ProgramCatalogValidation(1620)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Owned(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouXiaoQiaoMiluo or
            SkillProgramEffectOp.ResolveMouXiaoQiaoMiluoRoundEnd or SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan or SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal;
        var ours = p.Id is MouXiaoQiaoOlRuntime.Miluo or MouXiaoQiaoOlRuntime.Jueyan;
        if (!ours)
        {
            if (p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Owned(e.Op)))
                throw new InvalidOperationException(path + ": retained-Hand Jueyan and temporary Miluo require their actual native source bindings.");
            return;
        }
        if (p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 ||
            p.Activations is not [var a] || p.Triggers is not [var t] || a.Effects.Count != 1 || t.Effects.Count != 1 ||
            a.Effects[0].Target != SkillProgramEffectTarget.Owner || t.Effects[0].Target != SkillProgramEffectTarget.Owner ||
            a.Effects[0].Condition.Kind != SkillProgramConditionKind.Always || t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always ||
            a.MinTargets != 0 || a.MaxTargets != 0 || a.UsesPerTurn is not null || a.UsesPerGame is not null || t.Optional ||
            t.Subject != SkillProgramTriggerSubject.Owner || t.UsageLimit is not null || t.MarkerCost is not null)
            throw new InvalidOperationException(path + ": Mou Xiao Qiao requires the complete native gift/use and real Round/reveal bindings.");
        var valid = p.Id == MouXiaoQiaoOlRuntime.Miluo
            ? a.Id == "reveal-and-give" && a.MinCards == 0 && a.MaxCards == 0 && a.UsesPerPhase == 1 &&
                a.Effects[0].Op == SkillProgramEffectOp.ResolveMouXiaoQiaoMiluo && t.Id == "round-end-consequence" &&
                t.Window == SkillProgramTriggerWindow.RoundEnding && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouXiaoQiaoMiluoRoundEnd
            : a.Id == "retained-hand-use" && a.MinCards == 1 && a.MaxCards == 1 && a.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                a.UsesPerPhase is null && a.Effects[0].Op == SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan && t.Id == "public-hand-first-suit" &&
                t.Window == SkillProgramTriggerWindow.PublicHandRevealed && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal;
        if (!valid) throw new InvalidOperationException(path + ": Mou Xiao Qiao changed a reserved payment, target or Round budget binding.");
    }
}
