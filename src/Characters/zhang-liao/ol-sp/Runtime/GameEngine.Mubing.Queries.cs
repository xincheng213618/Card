namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MubingMilitaryCount(int seat) => ProgramEventHistory<MubingCardsAcquiredEvent>().Where(e => e.Source.OwnerSeat == seat).Sum(e => e.MilitaryCardCount);
    private static bool IsMubingMilitaryCard(Card card) => IsSlashCard(card.Kind) ||
        card.Kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault ||
        EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon;

    private IReadOnlyList<int> MubingAvailableReveal(ProgramSkillFrame f) =>
        GetProgramCardSet(f, MubingReveal).CardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
    private IReadOnlyList<int> MubingAvailableGifts(ProgramSkillFrame f) =>
        f.SpZhangLiao!.Obtained.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat)).ToArray();
}
