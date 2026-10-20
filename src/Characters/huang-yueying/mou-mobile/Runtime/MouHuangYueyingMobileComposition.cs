namespace CardGame.Core;

internal static class MouHuangYueyingMobileComposition
{
    internal const string Jizhi = "mou-mobile:jizhi", Qicai = "mou-mobile:qicai";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouHuangYueyingJizhi or SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment or SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains;
    [ProgramCatalogValidation(1370)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects));
        if (p.Id is not (Jizhi or Qicai))
        { if (effects.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": mobile Yueying operations require their complete original source."); return; }
        bool Owner(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.Id == Jizhi ? p.Activations.Count == 0 && p.Triggers is [var trick, var response] &&
            trick.Id == "ordinary-trick-used" && trick.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects && trick.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && trick.CardCategories.SequenceEqual([SkillProgramCardCategory.InstantTrick]) &&
            response.Id == "nullification-used" && response.Window == SkillProgramTriggerWindow.CardResponseAccepted && response.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && response.CardKinds.SequenceEqual([CardKind.Nullification]) &&
            p.Triggers.All(t => !t.Optional && t.Effects.Count == 1 && Owner(t.Effects[0], SkillProgramEffectOp.ResolveMouHuangYueyingJizhi)) && p.CardPolicies.Count == 0 :
            p.Activations is [var a] && a.Id == "place-equipment" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 && a.TargetKind == SkillProgramTargetKind.OtherLiving && a.UsesPerPhase == 1 && a.UsesPerTurn is null &&
            a.Effects.Count == 1 && Owner(a.Effects[0], SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment) && p.Triggers is [var gain] && gain.Id == "receive-three" &&
            gain.Window == SkillProgramTriggerWindow.CardsGained && gain.Subject == SkillProgramTriggerSubject.Owner && !gain.Optional && gain.DestinationZones.SequenceEqual([CardZoneKind.Hand]) &&
            gain.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && gain.Effects.Count == 1 && Owner(gain.Effects[0], SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains) &&
            p.CardPolicies is [var distance] && distance.Kind == SkillProgramCardPolicyKind.IgnoreUseDistance && distance.CardKinds.All(k => CardCatalog.Get(k).CategoryName == "锦囊牌") && distance.CardKinds.Contains(CardKind.Snatch) && distance.CardKinds.Contains(CardKind.SupplyShortage);
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": mobile Yueying requires complete Jizhi Draw/exemption and Qicai placement/three-card transfer contracts.");
    }
}
