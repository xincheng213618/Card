namespace CardGame.Core;
internal static class MouChenLinComposition
{
    [ProgramCatalogValidation(1920)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id == MouChenLinRuntime.Yaozuo)
        {
            if (p.Triggers.Count != 0 || p.Activations is not [{ Id: "invite-authors", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 1, Effects: [{ Op: SkillProgramEffectOp.ResolveMouChenLinYaozuo, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }] || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0) throw new InvalidOperationException(path + ": Yaozuo requires its exact actual-Play participant invitation and true successful-Give race receipt.");
            return;
        }
        if (p.Id != MouChenLinRuntime.Zhuanwen && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMouChenLinZhuanwen or SkillProgramEffectOp.ResolveMouChenLinYaozuo)) return;
        if (p.Id != MouChenLinRuntime.Zhuanwen || p.Activations.Count != 0 || p.Triggers is not [{ Id: "ending-top-written-cards", Window: SkillProgramTriggerWindow.TurnEnding, Optional: true, TurnOwnerScope: SkillProgramTurnOwnerScope.Own, Effects: [{ Op: SkillProgramEffectOp.ResolveMouChenLinZhuanwen, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }] || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0) throw new InvalidOperationException(path + ": Zhuanwen requires its own actual Ending top-X native physical queue, fixed admitted target and original-order retained top segment.");
    }
}
