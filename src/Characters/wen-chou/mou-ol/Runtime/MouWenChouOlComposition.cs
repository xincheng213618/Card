namespace CardGame.Core;
internal static class MouWenChouOlComposition
{
    internal const string Lunzhan = "mou-ol:lunzhan", Juejue = "mou-ol:juejue";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.UseMouWenChouLunzhan or SkillProgramEffectOp.RewardMouWenChouLunzhan or SkillProgramEffectOp.ResolveMouWenChouJuejue;
    [ProgramCatalogValidation(1530)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Lunzhan or Juejue) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 && p.Triggers.Count == 1 && p.Activations.Count == (p.Id == Lunzhan ? 6 : 0);
        if (p.Id == Lunzhan)
            foreach (var a in p.Activations)
            {
                var n = a.MinCards;
                valid &= n is >= 0 and <= 5 && a.Id == $"duel-{n}" && a.MaxCards == n && a.MinTargets == 1 && a.MaxTargets == 1 && a.TargetKind == SkillProgramTargetKind.OtherLiving && a.UsesPerTurn is null && a.UsesPerPhase is null && a.UsesPerGame is null && a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && a.Effects is [{ Op: SkillProgramEffectOp.UseMouWenChouLunzhan, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] && a.Effects[0].Amount == n;
            }
        if (p.Triggers.FirstOrDefault() is { } t)
            valid &= !t.Optional && t.SingleActionInstance == (p.Id == Lunzhan) && t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null && t.MarkerCost is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.Suits.Count == 0 && t.CardCategories.Count == 0 && !t.IncludeResponseUses &&
                (p.Id == Lunzhan ? t.Id == "reward" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && t.CardKinds.SequenceEqual([CardKind.Duel]) && t.Effects is [{ Op: SkillProgramEffectOp.RewardMouWenChouLunzhan, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] : t.Id == "all-hand" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.CardKinds.Count == 0 && t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouWenChouJuejue, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]);
        if (!valid) throw new InvalidOperationException(path + ": OL Mou Wen Chou requires complete zero-to-five HE Duel, whole-Use reward and first qualifying all-Hand bindings.");
    }
}
