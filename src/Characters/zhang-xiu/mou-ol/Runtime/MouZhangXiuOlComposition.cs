namespace CardGame.Core;
internal static class MouZhangXiuOlComposition
{
    internal const string Qiulie = "mou-ol:qiulie", Zhuijiao = "mou-ol:zhuijiao";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouZhangXiuQiulie or SkillProgramEffectOp.UseMouZhangXiuQiulieSlash or SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash or SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao or SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao;
    internal static ActualDiscardRecoveryPhaseKind? Phase(SkillProgramTriggerWindow window) => window switch
    {
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => ActualDiscardRecoveryPhaseKind.Preparation,
        SkillProgramTriggerWindow.JudgmentPhaseStarting => ActualDiscardRecoveryPhaseKind.Judgment,
        SkillProgramTriggerWindow.DrawPhaseStarting => ActualDiscardRecoveryPhaseKind.Draw,
        SkillProgramTriggerWindow.PlayPhaseStarting => ActualDiscardRecoveryPhaseKind.Play,
        SkillProgramTriggerWindow.DiscardPhaseStarting => ActualDiscardRecoveryPhaseKind.Discard,
        SkillProgramTriggerWindow.TurnEnding => ActualDiscardRecoveryPhaseKind.Ending,
        _ => null
    };
    [ProgramCatalogValidation(1520)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (program.Id is not (Qiulie or Zhuijiao) && !program.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op))) return;
        var valid = program.Activations.Count == 0 && program.ViewAs.Count == 0 && program.Modifiers.Count == 0 && program.CardPolicies.Count == 0 && program.BooleanStates.Count == 0 && program.Triggers.Count == (program.Id == Qiulie ? 7 : 2);
        foreach (var t in program.Triggers)
        {
            valid &= !t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null &&
                t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && !t.IncludeResponseUses &&
                t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
            if (!valid) break;
            var op = t.Effects[0].Op;
            valid &= program.Id == Qiulie
                ? t.Id == "choose-hunt" && t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && op == SkillProgramEffectOp.ResolveMouZhangXiuQiulie && t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && t.CardKinds.Count == 0 && !t.SingleActionInstance ||
                  t.Id.StartsWith("hunt-", StringComparison.Ordinal) && Phase(t.Window) is not null && t.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && op == SkillProgramEffectOp.UseMouZhangXiuQiulieSlash && t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && t.CardKinds.Count == 0 && !t.SingleActionInstance ||
                  t.Id == "defend-hunt" && t.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects && op == SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash && t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.CardKinds.SequenceEqual([CardKind.Slash]) && !t.SingleActionInstance
                : t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                  (t.Id == "previous-undamaged" && t.Window == SkillProgramTriggerWindow.CardUseCommitted && op == SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao && !t.SingleActionInstance ||
                   t.Id == "complete-undamaged" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && op == SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao && t.SingleActionInstance);
        }
        if (!valid) throw new InvalidOperationException(path + ": OL Mou Zhang Xiu requires the complete six-phase paid native Slash and previous-true-Use Zhuijiao bindings.");
    }
}
