namespace CardGame.Core;
internal static class MouSunJianOlComposition
{
    internal const string Yipo = "mou-ol:yipo", Hulie = "mou-ol:hulie";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouSunJianYipo or SkillProgramEffectOp.OfferMouSunJianHulieDamage or SkillProgramEffectOp.ResolveMouSunJianHulieCounter;
    [ProgramCatalogValidation(1470)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Yipo or Hulie) && !p.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op))) return;
        var valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 &&
            p.Triggers.Count == (p.Id == Yipo ? 1 : 2);
        foreach (var t in p.Triggers)
        {
            valid &= !t.Optional && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && !t.IncludeResponseUses &&
                t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
            valid &= p.Id == Yipo ? t.Id == "first-positive-hp" && t.Window == SkillProgramTriggerWindow.AfterHealthChanged && t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null &&
                t.HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerEvent && !t.SingleActionInstance && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouSunJianYipo :
                p.Id == Hulie && t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                    (t.Id == "single-target-damage" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && !t.SingleActionInstance && t.Effects[0].Op == SkillProgramEffectOp.OfferMouSunJianHulieDamage ||
                     t.Id == "undamaged-counter" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && t.SingleActionInstance && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouSunJianHulieCounter);
        }
        if (!valid) throw new InvalidOperationException(path + ": OL Mou Sun Jian requires both complete native HP and independent original-Use Hulie bindings.");
    }
}
