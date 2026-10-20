namespace CardGame.Core;
internal static class MouZhouYuMobileComposition
{
    internal const string Yingzi = "mou-mobile:yingzi", Fanjian = "mou-mobile:fanjian";
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id is not (Yingzi or Fanjian) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMouZhouYuYingzi or SkillProgramEffectOp.ResolveMouZhouYuFanjian)) return;
        static bool Effect(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.ActorReference is null && e.TargetReference is null && e.Condition.Kind == SkillProgramConditionKind.Always;
        var common = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 && p.DamageModifiers.Count == 0;
        var valid = p.Id switch
        {
            Yingzi => p.Activations.Count == 0 && p.Triggers is [var t] && t.Id == "three-condition-draw" && t.Window == SkillProgramTriggerWindow.DrawPhaseStarting && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional && t.UsageLimit is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Effects is [var e] && Effect(e, SkillProgramEffectOp.ResolveMouZhouYuYingzi),
            Fanjian => p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "cover-and-declare" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 && a.TargetKind == SkillProgramTargetKind.OtherLiving && a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.UsesPerTurn is null && a.UsesPerPhase is null && a.UsesPerGame is null && a.Condition.Kind == SkillProgramConditionKind.Always && a.Effects is [var e] && Effect(e, SkillProgramEffectOp.ResolveMouZhouYuFanjian),
            _ => false
        };
        if (!common || !valid) throw new InvalidOperationException(path + ": Mobile Mou Zhou Yu requires frozen three-condition Yingzi and the complete privately covered, declared, guessed/turned, revealed/gifted Fanjian sequence.");
    }
}
