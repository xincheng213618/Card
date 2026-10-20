namespace CardGame.Core;
internal static class MouJiangWeiComposition
{
    internal const string Tiaoxin = "mou-mobile:tiaoxin", Zhiji = "mou-mobile:zhiji";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.InitializeMouJiangWei or SkillProgramEffectOp.ResolveMouJiangWeiTiaoxin or SkillProgramEffectOp.ResolveMouJiangWeiZhiji;
    [ProgramCatalogValidation(1540)]
    internal static void Validate(string path, SkillProgram p)
    {
        var es = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects));
        if (p.Id is not (Tiaoxin or Zhiji) && !es.Any(e => Handles(e.Op))) return;
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.TargetReference is null && e.ActorReference is null && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0;
        if (p.Id == Tiaoxin)
        {
            valid &= p.Activations is [{ Id: "taunt", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null } a] && a.Effects.Count == 1 && Plain(a.Effects[0], SkillProgramEffectOp.ResolveMouJiangWeiTiaoxin);
            valid &= p.Triggers is [{ Id: "initial-charge", Window: SkillProgramTriggerWindow.GameStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: false } t] && t.Effects.Count == 2 && Plain(t.Effects[0], SkillProgramEffectOp.InitializeMouJiangWei) && Plain(t.Effects[1], SkillProgramEffectOp.GrantArmor) && t.Effects[1].Amount == 1;
        }
        else valid &= p.Id == Zhiji && p.Activations.Count == 0 && p.Triggers is [{ Id: "awakening", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner, Optional: false } t] && t.Effects.Count == 1 && Plain(t.Effects[0], SkillProgramEffectOp.ResolveMouJiangWeiZhiji);
        if (!valid) throw new InvalidOperationException(path + ": mobile Jiang Wei requires complete 4/4 charge, sequential native taunt and independent preparation awakening.");
    }
}
