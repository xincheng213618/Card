namespace CardGame.Core;

internal static class MoZhangFeiComposition
{
    [ProgramCatalogValidation(1590)]
    internal static void Validate(string path, SkillProgram p)
    {
        var relevant = p.Id is MoZhangFeiRuntime.Zhuohun or MoZhangFeiRuntime.ModifiedZhuohun or MoZhangFeiRuntime.Chenshi;
        static bool OwnedOp(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMoZhangFeiZhuohun or
            SkillProgramEffectOp.ResolveMoZhangFeiChenshi or SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw;
        if (!relevant)
        {
            if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => OwnedOp(e.Op)))
                throw new InvalidOperationException(path + ": Mo Zhang Fei operations require their own native color, damage or draw binding.");
            return;
        }
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.Any(t => t.Optional || t.Effects.Count != 1 || t.Effects[0].Target != SkillProgramEffectTarget.Owner ||
                t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always || !OwnedOp(t.Effects[0].Op)))
            throw new InvalidOperationException(path + ": Mo Zhang Fei requires native locked trigger operations with no alternate payment or view-as.");
        var expected = p.Id == MoZhangFeiRuntime.Chenshi ? 1 : p.Id == MoZhangFeiRuntime.ModifiedZhuohun ? 2 : 1;
        if (p.Triggers.Count != expected || (p.Id == MoZhangFeiRuntime.Chenshi ? p.CardIdentities.Count != 0 :
            p.CardIdentities is not [var identity] || identity.Id != "own-turn-dodge-as-slash" ||
                identity.OutputKind != CardKind.Slash || identity.Condition.Kind != SkillProgramConditionKind.OwnTurn ||
                !identity.InputKinds.SequenceEqual([CardKind.Dodge]) || identity.InputSuits.Count != 0 ||
                !identity.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.WoodenOxGrain])))
            throw new InvalidOperationException(path + ": Zhuohun must retain its mandatory own-turn Dodge identity in both forms.");
        foreach (var t in p.Triggers)
        {
            var valid = t.Id switch
            {
                MoZhangFeiRuntime.ColorBinding => t.Window == SkillProgramTriggerWindow.AfterHealthChanged &&
                    t.Effects[0].Op == SkillProgramEffectOp.ResolveMoZhangFeiZhuohun && p.Id != MoZhangFeiRuntime.Chenshi,
                "first-three-damage" => t.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                    t.Effects[0].Op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi && p.Id == MoZhangFeiRuntime.Chenshi,
                MoZhangFeiRuntime.GreenBinding => t.Window == SkillProgramTriggerWindow.NativeDrawStarting &&
                    t.Effects[0].Op == SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw && p.Id == MoZhangFeiRuntime.ModifiedZhuohun,
                _ => false
            };
            if (!valid) throw new InvalidOperationException(path + ": Mo Zhang Fei lost a complete native trigger binding.");
        }
    }
}
