namespace CardGame.Core;
internal static class MouXiahouDunMobileComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var operations = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id is not ("mou-mobile:ganglie" or "mou-mobile:qingjian") && !operations.Any(e => e.Op is SkillProgramEffectOp.ResolveMouXiahouDunGanglie or SkillProgramEffectOp.CollectMouXiahouDunQingjian or SkillProgramEffectOp.DistributeMouXiahouDunQingjian)) return;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 &&
            (p.Id == "mou-mobile:ganglie" ? p.Triggers.Count == 0 && p.Activations is [var a] && a.Id == "retaliate-all" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 &&
                a.UsesPerTurn is null && a.UsesPerPhase == 1 && a.TargetKind == SkillProgramTargetKind.AnyLiving && Own(a.Effects, SkillProgramEffectOp.ResolveMouXiahouDunGanglie) :
             p.Id == "mou-mobile:qingjian" && p.Activations.Count == 0 && p.Triggers is [var collect, var distribute] &&
                Trigger(collect, "collect-non-use", SkillProgramTriggerWindow.DiscardPileReceived, SkillProgramEffectOp.CollectMouXiahouDunQingjian) && collect.MovementOccurrence is null &&
                Trigger(distribute, "distribute-qingjian", SkillProgramTriggerWindow.PlayEnding, SkillProgramEffectOp.DistributeMouXiahouDunQingjian));
        if (!valid) throw new InvalidOperationException(path + ": Mou Xiahou Dun requires its full multi-target retaliation and compulsory non-Use Qingjian bindings.");
    }
    private static bool Own(IReadOnlyList<SkillProgramEffect> e, SkillProgramEffectOp op) => e is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } own] && own.Op == op;
    private static bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow w, SkillProgramEffectOp op) => t.Id == id && t.Window == w && t.Subject == SkillProgramTriggerSubject.Owner &&
        !t.Optional && t.OwnerRelation is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.Suits.Count == 0 && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && !t.MovementDiscardOnly && Own(t.Effects, op);
}
