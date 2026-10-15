namespace CardGame.Core;
internal static class NonBasicUnrespondableSlashSchema
{
    internal static void Validate(SkillProgramViewAs r, string path)
    {
        if (!r.NonBasicUnrespondableSlash) return;
        if (r.InputCount != 1 || r.VariableInputCount || r.InputKinds.Count != 0 || r.InputSuits.Count != 0 ||
            !r.InputCategories.SequenceEqual(new[] { SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }) ||
            !r.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) ||
            r.OutputKind != CardKind.Slash || !r.ForPlay || r.ForResponse || r.Condition.Kind != SkillProgramConditionKind.Always ||
            r.AllowChainedInput || r.SameSuit || r.InheritPreviousPlaySuit || r.UsesPerPhase is not null || r.UsageGroup is not null ||
            r.UseOnly || r.ExtendedUse || r.CostDestination is not null || r.DistanceUnlimited || r.AllHandCardsOfSelectedSuit ||
            r.SingleCardTrickUse || r.ExcludeOwnerEffects || r.DeclaredEntity || r.DeclarationValidation is not null ||
            r.ConversionStateId is not null || r.ActivationUsageGroup is not null || r.RoundDistinctBasicUse is not null ||
            r.DrawFundedDistinctBasic is not null || r.TieredRoundConversion is not null || r.ChainedStateCost is not null ||
            r.LastInSourceZone is not null || r.NoDying || r.AllowSameKind || r.UnusedOutputThisTurn || r.UnusedOutputNameThisGame ||
            r.DamageBonus != 0 || r.RecoveryBonus != 0 || r.DisableSkillUntilTurnEndIfNoDamage is not null)
            throw new InvalidOperationException($"{path}: nonBasicUnrespondableSlash requires one plain HE non-basic-to-Slash conversion for play only.");
    }
}
