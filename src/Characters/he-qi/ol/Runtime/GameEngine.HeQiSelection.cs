namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HeQiSelectionQueries(GameEngine engine) : IHeQiSelectionQueries
    {
        public bool HasRoundRange(int sourceSeat, int targetSeat) => engine.HasSayingRoundRange(sourceSeat, targetSeat);
        public int CombatDistance(int sourceSeat, int targetSeat) => engine.GetCombatDistance(sourceSeat, targetSeat);
        public int AttackRange(int seat) => engine.GetAttackRange(seat);
        public int EquipmentSlotCapacity(int seat, EquipmentSlot slot) => engine._players[seat].EquipmentSlotCapacity(slot);
        public int EquipmentCountInSlot(int seat, EquipmentSlot slot) =>
            engine.GetEquipment(engine._players[seat]).Count(card => EquipmentCatalog.Get(card.Kind).Slot == slot);
    }

}
