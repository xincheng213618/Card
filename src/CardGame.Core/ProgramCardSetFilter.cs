namespace CardGame.Core;

/// <summary>One typed predicate shared by runtime filtering, symbolic resource proof and AI estimates.</summary>
internal static class ProgramCardSetFilter
{
    internal static bool Matches(CardKind kind, Suit suit, IReadOnlyList<Suit> suits,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<EquipmentSlot> equipmentSlots,
        IReadOnlyList<CardKind> cardKinds) =>
        (suits.Count == 0 || suits.Contains(suit)) &&
        (categories.Count == 0 && equipmentSlots.Count == 0 && cardKinds.Count == 0 ||
         cardKinds.Contains(kind) ||
         categories.Count > 0 && categories.Contains(Category(kind)) ||
         EquipmentCatalog.IsEquipment(kind) && equipmentSlots.Contains(EquipmentCatalog.Get(kind).Slot));

    // Bounded, public prior for cards that have not yet been revealed. It never reads the draw pile.
    internal static double PriorForSuit(Suit suit, IReadOnlyList<Suit> suits,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<EquipmentSlot> equipmentSlots,
        IReadOnlyList<CardKind> cardKinds)
    {
        var kinds = Enum.GetValues<CardKind>()
            .Where(kind => kind is not (CardKind.RedBloodBlade or CardKind.GeneralWeapon)).ToArray();
        return kinds.Count(kind => Matches(kind, suit, suits, categories, equipmentSlots, cardKinds)) /
               (double)kinds.Length;
    }

    private static SkillProgramCardCategory Category(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName switch
        {
            "基本牌" => SkillProgramCardCategory.Basic,
            "锦囊牌" => SkillProgramCardCategory.Trick,
            "装备牌" => SkillProgramCardCategory.Equipment,
            _ when EquipmentCatalog.IsEquipment(kind) => SkillProgramCardCategory.Equipment,
            _ => throw new InvalidOperationException($"Card kind '{kind}' has no program filter category.")
        };
}
