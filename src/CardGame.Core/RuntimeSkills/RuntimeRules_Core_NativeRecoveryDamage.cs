namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NativeRecoveryDamage
{
    internal static bool NativeRecoveryMaterialGroupMatches(CardUseFrame use)
    {
        if (use.Action is not { Type: CardActionType.Use } action || action.ActorSeat != use.SourceSeat ||
            action.EffectiveKind != use.CardKind || !action.TargetSeats.SequenceEqual(use.TargetSeats)) return false;
        var materials = action.PhysicalCards.Select(c => c.CardId).ToArray();
        // Empty virtual uses retain their own issuance protocol. A physical
        // conversion pays the complete ordered group, not a representative only.
        return materials.Length > 0 && materials.Distinct().Count() == materials.Length && materials[0] == use.CardId &&
            materials.SequenceEqual(use.PhysicalCardIds ?? [use.CardId]) &&
            action.PhysicalCards.All(c => c.From.OwnerSeat is null || c.From.OwnerSeat == action.ProviderSeat);
    }
}
