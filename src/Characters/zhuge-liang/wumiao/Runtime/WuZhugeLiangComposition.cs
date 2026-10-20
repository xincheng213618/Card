namespace CardGame.Core;
internal static class WuZhugeLiangComposition
{
    internal const string Jincui = "wumiao:jincui", Qingshi = "wumiao:qingshi", Zhizhe = "wumiao:zhizhe";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.InitializeWuZhugeLiangJincui or SkillProgramEffectOp.ResolveWuZhugeLiangJincui or SkillProgramEffectOp.ResolveWuZhugeLiangQingshi or SkillProgramEffectOp.ResolveWuZhugeLiangZhizhe;
    private static bool Effect(IReadOnlyList<SkillProgramEffect> e, SkillProgramEffectOp op) => e is [var a] && a.Op == op && a.Target == SkillProgramEffectTarget.Owner && a.Amount == 0 && a.Condition.Kind == SkillProgramConditionKind.Always && a.ActorReference is null && a.TargetReference is null && a.SourceBind is null && a.ResultBind is null && a.NumberExpression is null;
    private static bool T(SkillProgramTrigger t, string id, SkillProgramTriggerWindow w, bool optional, SkillProgramEffectOp op) => t.Id == id && t.Window == w && t.Optional == optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.MarkerCost is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 && Effect(t.Effects, op);
    internal static bool ValidCommittedUseTrigger(SkillProgramTrigger t) =>
        T(t, "same-name-use", SkillProgramTriggerWindow.CardUseCommitted, true, SkillProgramEffectOp.ResolveWuZhugeLiangQingshi) &&
        t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.Subject is null && t.IncludeResponseUses && t.SingleActionInstance &&
        t.SourceSkillId is null && t.SourceViewAsId is null && t.UsageScope is null && t.ChoiceGroup is null && t.Priority == 0 &&
        t.DamageOccurrence is null && t.MovementOccurrence is null && !t.OnlyDesignatedCardTargets && !t.AllowNoEventTarget &&
        !t.EvaluateConditionAtResolution && !t.NoDyingAtActivation;
    [ProgramCatalogValidation(1020)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!new[] { Jincui, Qingshi, Zhizhe }.Contains(p.Id) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        var empty = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.HpSkillGrants.Count == 0 && p.CardIdentities.Count == 0 && p.DamageModifiers.Count == 0 && !p.LordSkillProjection;
        var valid = p.Id switch
        {
            Jincui => p.Activations.Count == 0 && p.Triggers is [var g, var prep] && T(g, "opening-seven", SkillProgramTriggerWindow.GameStarting, false, SkillProgramEffectOp.InitializeWuZhugeLiangJincui) && g.Subject == SkillProgramTriggerSubject.Owner && T(prep, "preparation-seven", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, false, SkillProgramEffectOp.ResolveWuZhugeLiangJincui) && prep.Subject == SkillProgramTriggerSubject.Owner,
            Qingshi => p.Activations.Count == 0 && p.Triggers is [var q] && ValidCommittedUseTrigger(q),
            Zhizhe => p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "copy-hand" && a.MinCards == 1 && a.MaxCards == 1 && a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.MinTargets == 0 && a.MaxTargets == 0 && a.UsesPerGame == 1 && a.UsesPerTurn is null && a.UsesPerPhase is null && a.UsageGroup == a.Id && a.Condition.Kind == SkillProgramConditionKind.Always && Effect(a.Effects, SkillProgramEffectOp.ResolveWuZhugeLiangZhizhe),
            _ => false
        };
        if (!empty || !valid) throw new InvalidOperationException(path + ": Wu Zhuge Liang requires the complete source-bound opening/HP setting, same-name Use and real Hand-copy contracts.");
    }
}

internal sealed class WuZhugeLiangCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => WuZhugeLiangComposition.Qingshi;
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (!WuZhugeLiangComposition.ValidCommittedUseTrigger(trigger))
            throw new InvalidOperationException("Qingshi requires its exact optional actor committed true-Use trigger, including native response Uses.");
    }
}
