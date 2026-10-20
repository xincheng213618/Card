namespace CardGame.Core;

internal static class MouLuZhiComposition
{
    [ProgramCatalogValidation(1110)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id is not ("mou-mobile:mingren" or "mou-mobile:zhenliang") && !effects.Any(e => e.Op is SkillProgramEffectOp.InitializeMouLuZhiMingren or SkillProgramEffectOp.ExchangeMouLuZhiMingren or SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang or SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin)) return;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 &&
            (p.Id == "mou-mobile:mingren" ? p.Activations.Count == 0 && p.Triggers is [var initial, var exchange] &&
                Trigger(initial, "initial-ren", SkillProgramTriggerWindow.GameStarting, false, SkillProgramEffectOp.InitializeMouLuZhiMingren) &&
                Trigger(exchange, "exchange-ren", SkillProgramTriggerWindow.TurnEnding, true, SkillProgramEffectOp.ExchangeMouLuZhiMingren) :
             p.Id == "mou-mobile:zhenliang" && p.Activations is [var a] && a.Id == "yang-damage" && a.MinCards == 0 && a.MaxCards == 0 &&
                a.MinTargets == 0 && a.MaxTargets == 0 && a.TargetKind == SkillProgramTargetKind.AnyLiving && a.UsesPerTurn is null && a.UsesPerPhase == 1 &&
                Own(a.Effects, SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang) && p.Triggers is [var use, var played] &&
                Completion(use, "yin-use", SkillProgramTriggerWindow.CardUseCompleted, true) && Completion(played, "yin-played", SkillProgramTriggerWindow.CardResponseCompleted, false));
        if (!valid) throw new InvalidOperationException(path + ": Mou Lu Zhi requires its complete private Ren and native conversion bindings.");
    }
    private static bool Plain(SkillProgramTrigger t) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.NamedUsageGroup is null;
    private static bool Own(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
    private static bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, bool optional, SkillProgramEffectOp op) => Plain(t) &&
        t.Id == id && t.Window == window && t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && t.Optional == optional && Own(t.Effects, op);
    private static bool Completion(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, bool use) => Plain(t) && t.Id == id && t.Window == window && t.Subject is null &&
        t.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && t.Optional && t.IncludeResponseUses == use && t.SingleActionInstance == use && Own(t.Effects, SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin);
}
