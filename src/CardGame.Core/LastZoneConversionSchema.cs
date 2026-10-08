namespace CardGame.Core;

// This additive policy never widens the legacy source-zone contract unless it
// explicitly requires the final real entity in one owner-scoped region.
internal static class LastZoneConversionSchema
{
    internal static void Validate(SkillProgramViewAs rule, string path)
    {
        if (rule.LastInSourceZone != true) return;

        var direction = rule.SourceZones.Count == 1 && (rule.SourceZones[0] switch
        {
            CardZoneKind.Hand => rule.OutputKind == CardKind.Dodge &&
                                 !rule.ForPlay && rule.ForResponse && !rule.UseOnly,
            CardZoneKind.Equipment => rule.OutputKind == CardKind.Nullification &&
                                      !rule.ForPlay && rule.ForResponse && rule.UseOnly,
            CardZoneKind.Judgment => rule.OutputKind == CardKind.Slash &&
                                     rule.ForPlay && rule.ForResponse && !rule.UseOnly,
            _ => false
        });
        if (!direction || rule.InputCount != 1 || rule.VariableInputCount ||
            rule.AllowChainedInput || rule.SameSuit || rule.ExtendedUse ||
            rule.SingleCardTrickUse || rule.CostDestination is not null ||
            rule.TieredRoundConversion is not null || rule.DrawFundedDistinctBasic is not null ||
            rule.RoundDistinctBasicUse is not null || rule.ConversionStateId is not null ||
            rule.DeclarationValidation is not null || rule.UnusedOutputNameThisGame ||
            rule.NameLedgerId is not null || rule.DeclaredEntity || rule.ExcludeOwnerEffects ||
            rule.NoDying || rule.UnusedOutputThisTurn || rule.InheritPreviousPlaySuit ||
            rule.UsesPerPhase is not null || rule.UsageGroup is not null ||
            rule.ActivationUsageGroup is not null || rule.DistanceUnlimited ||
            rule.DisableSkillUntilTurnEndIfNoDamage is not null ||
            rule.DamageBonus != 0 || rule.RecoveryBonus != 0)
            throw new InvalidOperationException(path +
                ": lastInSourceZone requires exactly one real owner Hand/Dodge, Equipment/use-only Nullification, or Judgment/Slash material and its native use/response directions without another cost, quota or conversion policy.");
    }
}
