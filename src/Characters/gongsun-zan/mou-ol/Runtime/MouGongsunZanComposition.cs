namespace CardGame.Core;
internal static class MouGongsunZanComposition
{
    internal const string Jiaodi = "mou-ol:jiaodi", Baojing = "mou-ol:baojing";
    [ProgramCatalogValidation(1490)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        bool Own(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouGongsunZanJiaodi or SkillProgramEffectOp.ResolveMouGongsunZanBaojing;
        if (p.Id is not (Jiaodi or Baojing))
        { if (p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(Own)) throw new InvalidOperationException(path + ": Gongsun Zan requires his own source."); return; }
        bool Effect(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Amount == 0 && e.ActorReference is null && e.TargetReference is null && e.Condition.Kind == SkillProgramConditionKind.Always && e.SourceBind is null && e.ResultBind is null;
        var empty = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 && p.DamageModifiers.Count == 0 && p.HpSkillGrants.Count == 0;
        var valid = p.Id == Jiaodi
            ? p.Activations.Count == 0 && p.Triggers is [var t] && t.Id == "unique-slash" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !t.Optional && !t.SingleActionInstance && !t.IncludeResponseUses && t.UsageLimit is null && t.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && Effect(t.Effects, SkillProgramEffectOp.ResolveMouGongsunZanJiaodi)
            : p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "adjust-range" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 && a.TargetKind == SkillProgramTargetKind.OtherLiving && a.UsesPerPhase == 1 && a.UsesPerTurn is null && a.UsesPerGame is null && a.Condition.Kind == SkillProgramConditionKind.Always && Effect(a.Effects, SkillProgramEffectOp.ResolveMouGongsunZanBaojing);
        if (!empty || !valid) throw new InvalidOperationException(path + ": Gongsun Zan must preserve both independent range comparisons, one native Slash and the next-real-Play range grant.");
    }
}
