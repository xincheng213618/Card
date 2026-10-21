namespace CardGame.Core;
internal static class MouDianWeiComposition
{
    internal const string Kuangzhan = "mou:kuangzhan", Kangyong = "mou:kangyong";
    [ProgramCatalogValidation(1750)]
    internal static void Validate(string path, SkillProgram p)
    {
        var ours = p.Id is Kuangzhan or Kangyong;
        if (!ours && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMouDianWeiKuangzhan or SkillProgramEffectOp.ResolveMouDianWeiKangyongStart or SkillProgramEffectOp.ResolveMouDianWeiKangyongEnd)) return;
        bool Effect(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = p.Id == Kuangzhan ? p.Triggers.Count == 0 && p.Activations is [{ Id: "draw-and-contest", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 1 } a] && Effect(a.Effects, SkillProgramEffectOp.ResolveMouDianWeiKuangzhan) :
            p.Activations.Count == 0 && p.Triggers is [var start, var end] && start.Id == "recover-actual-turn-start" && start.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && start.Subject == SkillProgramTriggerSubject.Owner && !start.Optional &&
            end.Id == "repay-actual-turn-end" && end.Window == SkillProgramTriggerWindow.AfterTurnEnded && end.Subject == SkillProgramTriggerSubject.Owner && !end.Optional && !end.DeferredTurnEndOnly &&
            Effect(start.Effects, SkillProgramEffectOp.ResolveMouDianWeiKangyongStart) && Effect(end.Effects, SkillProgramEffectOp.ResolveMouDianWeiKangyongEnd);
        if (!ours || !valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Mou Dian Wei requires his actual draw/contest and full-turn recovery/debt contracts.");
    }
}
