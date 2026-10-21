namespace CardGame.Core;
internal static class MouHuLieComposition
{
    [ProgramCatalogValidation(1970)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Select(e => e.Op);
        if (p.Id is not (MouHuLieRuntime.Chuanyu or MouHuLieRuntime.Yitou) && !ops.Any(o => o is SkillProgramEffectOp.StartMouHuLieChuanyu or SkillProgramEffectOp.RegiveMouHuLieChuanyu or SkillProgramEffectOp.EndMouHuLieChuanyu or SkillProgramEffectOp.ResolveMouHuLieYitou or SkillProgramEffectOp.DrawMouHuLieYitou)) return;
        var good = p.Id switch {
            MouHuLieRuntime.Chuanyu => p.Triggers is [{ Id: "round-gift", Window: SkillProgramTriggerWindow.RoundStarting, Optional: true, Effects: [{ Op: SkillProgramEffectOp.StartMouHuLieChuanyu }] }, { Id: "true-used-yu-discard", Window: SkillProgramTriggerWindow.DiscardPileReceived, Optional: true, MovementOccurrence: null, Effects: [{ Op: SkillProgramEffectOp.RegiveMouHuLieChuanyu }] }, { Id: "round-clear-and-slashes", Window: SkillProgramTriggerWindow.RoundEnding, Optional: false, Effects: [{ Op: SkillProgramEffectOp.EndMouHuLieChuanyu }] }],
            MouHuLieRuntime.Yitou => p.Triggers is [{ Id: "other-largest-play", Window: SkillProgramTriggerWindow.PlayPhaseStarting, Optional: true, TurnOwnerScope: SkillProgramTurnOwnerScope.OtherLiving, Effects: [{ Op: SkillProgramEffectOp.ResolveMouHuLieYitou }] }, { Id: "issued-recipient-damage", Window: SkillProgramTriggerWindow.AfterDamageApplied, Optional: false, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Effects: [{ Op: SkillProgramEffectOp.DrawMouHuLieYitou }] }], _ => false };
        if (!good || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.Triggers.Any(t => t.UsageLimit is not null || t.MarkerCost is not null || t.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always))) throw new InvalidOperationException(path + ": Hu Lie requires his exact native round, true-used physical entity, all-Hand gift and issued damage-return contracts.");
    }
}
