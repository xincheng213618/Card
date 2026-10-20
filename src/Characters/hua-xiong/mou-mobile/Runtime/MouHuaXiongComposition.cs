namespace CardGame.Core;

internal static class MouHuaXiongComposition
{
    internal const string Yaowu = "mou-mobile:yaowu", Yangwei = "mou-mobile:yangwei";
    private static bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.TargetReference is null && e.ActorReference is null && e.NumberExpression is null && e.SourceBind is null && e.ResultBind is null && e.Condition.Kind == SkillProgramConditionKind.Always;
    internal static bool IsYangwei(SkillProgram p) => p.Id == Yangwei && p.Triggers.Count == 0 && p.ViewAs.Count == 0 &&
        PlainProgram(p) && p.Activations is [var a] && a.Id == "draw-and-wei" && a.MinCards == 0 && a.MaxCards == 0 &&
        a.MinTargets == 0 && a.MaxTargets == 0 && a.TargetKind == SkillProgramTargetKind.AnyLiving &&
        a.Condition.Kind == SkillProgramConditionKind.Always && a.MarkerCost is null && !a.ContinueAfterOwnerDeath && a.UsesPerPhase == 1 && a.UsesPerTurn is null && a.UsesPerGame is null &&
        a.Effects is [var first, var draw, var last] && Plain(first, SkillProgramEffectOp.ResolveMouHuaXiongYangwei) &&
        Plain(draw, SkillProgramEffectOp.Draw) && draw.Amount == 2 && Plain(last, SkillProgramEffectOp.ResolveMouHuaXiongYangwei);
    private static bool Benefit(SkillProgramEffect e, SkillProgramEffectOp op, string option, bool source) => e.Op == op && e.Amount == 1 &&
        e.Target == SkillProgramEffectTarget.Owner && e.ActorReference is null && e.NumberExpression is null && e.SourceBind is null && e.ResultBind is null &&
        (source ? e.TargetReference?.Kind == ProgramParticipantRef.EventSource : e.TargetReference is null) &&
        e.Condition.Kind == SkillProgramConditionKind.ChoiceIs && e.Condition.SourceBind == "yaowu-benefit" && e.Condition.OptionId == option;
    internal static bool IsYaowu(SkillProgram p) => p.Id == Yaowu && p.Activations.Count == 0 && p.ViewAs.Count == 0 && PlainProgram(p) &&
        p.Triggers is [var t] && t.Id == "slash-damage-benefit" && t.Window == SkillProgramTriggerWindow.BeforeDamageApplied &&
        t.Subject == SkillProgramTriggerSubject.DamageTarget && !t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.MarkerCost is null && t.UsageScope is null && t.UsageLimit is null && t.DamageOccurrence is null && t.OwnerRelation is null &&
        t.DamageCardKinds.Count == 0 && t.SourceZones.Count == 0 && t.MovementReasons.Count == 0 && t.Priority == 0 &&
        t.Effects is [var choose, var recover, var sourceDraw, var ownDraw] && Plain(choose, SkillProgramEffectOp.ResolveMouHuaXiongYaowu) &&
        Benefit(recover, SkillProgramEffectOp.Recover, "source-recover", true) && Benefit(sourceDraw, SkillProgramEffectOp.Draw, "source-draw", true) &&
        Benefit(ownDraw, SkillProgramEffectOp.Draw, "owner-draw", false);
    private static bool PlainProgram(SkillProgram p) => p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 &&
        p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 &&
        p.Contributions.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.ResolveMouHuaXiongYangwei or SkillProgramEffectOp.ResolveMouHuaXiongYaowu);
        if ((reserved || p.Id is Yaowu or Yangwei) && !IsYaowu(p) && !IsYangwei(p))
            throw new InvalidOperationException($"Invalid skill program at {path}: mobile Hua Xiong must retain the complete exact damage benefit or phase draw/Wei contract.");
    }
}
