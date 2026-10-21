namespace CardGame.Core;
internal static class MouZhangXiuAnniversaryComposition
{
    [ProgramCatalogValidation(1880)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = new[] { SkillProgramEffectOp.ResolveMouZhangXiuFuxi, SkillProgramEffectOp.ResolveMouZhangXiuHaoyi };
        if (p.Id is not (MouZhangXiuAnniversaryRuntime.Fuxi or MouZhangXiuAnniversaryRuntime.Haoyi) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => ops.Contains(e.Op))) return;
        var fuxi = p.Id == MouZhangXiuAnniversaryRuntime.Fuxi;
        if (p.Activations.Count != 0 || p.Triggers is not [var t] || t.Id != (fuxi ? "other-largest-hand" : "turn-damage-card-claim") || t.Window != (fuxi ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.TurnEnding) || !t.Optional || t.TurnOwnerScope != (fuxi ? SkillProgramTurnOwnerScope.OtherLiving : SkillProgramTurnOwnerScope.Own) || t.Effects is not [{ Op: var op, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] || op != ops[fuxi ? 0 : 1] || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0)
            throw new InvalidOperationException(path + ": Mou Zhang Xiu requires the native other-Play exchange/Slash and actual-turn undamaging physical-card claim with exact acquired-entity distribution.");
    }
}
