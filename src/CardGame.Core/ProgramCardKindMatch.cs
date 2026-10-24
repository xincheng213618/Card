namespace CardGame.Core;

/// <summary>Runtime compatibility for an explicitly complete pre-Cold Slash family.</summary>
internal static class ProgramCardKindMatch
{
    // Definitions retain their original immutable members and canonical JSON
    // fingerprints. A single Slash (or a selected elemental pair) is not a wildcard.
    internal static bool Contains(IReadOnlyCollection<CardKind> kinds, CardKind actual) =>
        kinds.Contains(actual) || actual == CardKind.ColdSlash &&
        kinds.Contains(CardKind.Slash) && kinds.Contains(CardKind.FireSlash) && kinds.Contains(CardKind.ThunderSlash);

    internal static bool IsFullSlashFamily(IReadOnlyCollection<CardKind> kinds) =>
        kinds.Count is 3 or 4 && kinds.Contains(CardKind.Slash) && kinds.Contains(CardKind.FireSlash) &&
        kinds.Contains(CardKind.ThunderSlash) && (kinds.Count == 3 || kinds.Contains(CardKind.ColdSlash));

    internal static bool IsFullElementalSlashFamily(IReadOnlyCollection<CardKind> kinds) =>
        kinds.Count is 2 or 3 && kinds.Contains(CardKind.FireSlash) && kinds.Contains(CardKind.ThunderSlash) &&
        (kinds.Count == 2 || kinds.Contains(CardKind.ColdSlash));

    // This condition explicitly means all elemental Slashes; ordinary lists
    // naming Fire and Thunder alone keep their exact, narrower meaning.
    internal static bool MatchesDamageModifier(SkillProgramDamageModifier modifier, CardKind actual) =>
        Contains(modifier.CardKinds, actual) || actual == CardKind.ColdSlash &&
        modifier.Condition == SkillProgramDamageModifierCondition.OwnerActualTurnElementalSlashUseOrdinalWithinLimit &&
        IsFullElementalSlashFamily(modifier.CardKinds);
}
