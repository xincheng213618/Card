namespace CardGame.Core;
internal static class CaoXianComposition
{
    [ProgramCatalogValidation(1950)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Select(e => e.Op).ToArray();
        if (p.Id is not (CaoXianRuntime.Lingxi or CaoXianRuntime.Zhifou) && !ops.Any(o => o is SkillProgramEffectOp.StoreCaoXianLingxi or SkillProgramEffectOp.ResolveCaoXianZhifou or SkillProgramEffectOp.AdjustCaoXianLingxi)) return;
        var good = p.Id switch {
            CaoXianRuntime.Lingxi => p.Triggers is [{ Id: "play-start-wings", Window: SkillProgramTriggerWindow.PlayPhaseStarting, Optional: true, Subject: SkillProgramTriggerSubject.Owner, Effects: [{ Op: SkillProgramEffectOp.StoreCaoXianLingxi }] }, { Id: "play-end-wings", Window: SkillProgramTriggerWindow.PlayEnding, Optional: true, Subject: SkillProgramTriggerSubject.Owner, Effects: [{ Op: SkillProgramEffectOp.StoreCaoXianLingxi }] }, { Id: "actual-wings-removed", Window: SkillProgramTriggerWindow.CardsMoved, Optional: false, MovementOccurrence: SkillProgramMovementOccurrence.PerBatch, SourceZones: [CardZoneKind.PublicPersistentPile], Effects: [{ Op: SkillProgramEffectOp.AdjustCaoXianLingxi }] }],
            CaoXianRuntime.Zhifou => p.Triggers is [{ Id: "completed-use-remove-wings", Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: true, SingleActionInstance: true, IncludeResponseUses: true, Effects: [{ Op: SkillProgramEffectOp.ResolveCaoXianZhifou }] }], _ => false };
        if (!good || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.Triggers.Any(t => t.UsageLimit is not null || t.MarkerCost is not null || t.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always))) throw new InvalidOperationException(path + ": Cao Xian requires the exact native phase stores, completed-Use wing removal and actual-pile-loss Hand adjustment.");
    }
}
