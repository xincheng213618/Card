namespace CardGame.Core;
internal static class WuHuangFuSongComposition
{
    internal const string Chaozhen = "wumiao:chaozhen", Lianjie = "wumiao:lianjie", Jiangxian = "wumiao:jiangxian";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen or SkillProgramEffectOp.ResolveWuHuangFuSongLianjie or SkillProgramEffectOp.ArmWuHuangFuSongJiangxian or SkillProgramEffectOp.SettleWuHuangFuSongJiangxian;
    private static bool Effect(IReadOnlyList<SkillProgramEffect> e, SkillProgramEffectOp op) => e is [var a] && a.Op == op && a.Target == SkillProgramEffectTarget.Owner && a.Amount == 0 && a.Condition.Kind == SkillProgramConditionKind.Always && a.ActorReference is null && a.TargetReference is null && a.SourceBind is null && a.ResultBind is null && a.NumberExpression is null;
    private static bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow w, bool optional, SkillProgramEffectOp op) => t.Id == id && t.Window == w && t.Optional == optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.MarkerCost is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 && Effect(t.Effects, op);
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!new[] { Chaozhen, Lianjie, Jiangxian }.Contains(p.Id) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        var empty = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.HpSkillGrants.Count == 0 && p.CardIdentities.Count == 0 && p.DamageModifiers.Count == 0 && !p.LordSkillProjection;
        var valid = p.Id switch
        {
            Chaozhen => p.Activations.Count == 0 && p.Triggers is [var prep, var dying] && Trigger(prep, "preparation-minimum", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, true, SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen) && prep.Subject == SkillProgramTriggerSubject.Owner && Trigger(dying, "dying-minimum", SkillProgramTriggerWindow.DyingEntering, true, SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen) && dying.Subject == SkillProgramTriggerSubject.Owner,
            Lianjie => p.Activations.Count == 0 && p.Triggers is [var use] && Trigger(use, "hand-use-minimum", SkillProgramTriggerWindow.CardUseTargetsFinalized, true, SkillProgramEffectOp.ResolveWuHuangFuSongLianjie) && use.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor,
            Jiangxian => p.Activations is [var a] && a.Id == "arm-turn" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.UsesPerGame == 1 && a.UsesPerTurn is null && a.UsesPerPhase is null && a.UsageGroup == a.Id && a.Condition.Kind == SkillProgramConditionKind.Always && Effect(a.Effects, SkillProgramEffectOp.ArmWuHuangFuSongJiangxian) && p.Triggers is [var end] && Trigger(end, "lose-after-turn", SkillProgramTriggerWindow.AfterTurnEnded, false, SkillProgramEffectOp.SettleWuHuangFuSongJiangxian) && end.Subject == SkillProgramTriggerSubject.Owner && end.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
            _ => false
        };
        if (!empty || !valid) throw new InvalidOperationException(path + ": Wu Huangfu Song requires his complete source-bound minimum acquire, genuine Hand Use and limited turn benefit contracts.");
    }
}
