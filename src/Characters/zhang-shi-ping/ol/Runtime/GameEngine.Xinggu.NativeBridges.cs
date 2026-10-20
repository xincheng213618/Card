namespace CardGame.Core;

public sealed partial class GameEngine
{

    private bool CanPlaceZhangShiPingMount(int owner, int target, Card card, CardLocation from) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeXingGuMountPile) &&
        IsValidPlayerSeat(owner) && IsValidPlayerSeat(target) && owner != target && _players[owner].IsAlive && _players[target].IsAlive &&
        from.Zone == CardZoneKind.PublicPersistentPile && from.OwnerSeat == owner && _cardZones.GetLocation(card.Id) == from &&
        PublicPileSources(owner).Any(source => source.SkillId == "ol:xinggu" && source.Location == from) && IsXingGuMount(card) &&
        !_players[target].EquipmentAreaAbolished && _players[target].EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot) > 0;

    private void PlaceZhangShiPingMount(ProgramSkillFrame frame, int target, Card card, CardLocation from)
    {
        if (GetActiveProgramFrame(frame.Id) != frame || frame.ZhangShiPingXingGu is not { Stage: XingGuStage.PlacementChildren } receipt ||
            receipt.CardId != card.Id || receipt.RecipientSeat != target || receipt.PileLocation != from ||
            !CanPlaceZhangShiPingMount(frame.OwnerSeat, target, card, from))
            throw new InvalidOperationException("Xinggu lost its exact public mount, native equipment slot or accepted placement.");
        var slot = EquipmentCatalog.Get(card.Kind).Slot;
        var equipped = GetEquipment(_players[target]).Where(item => EquipmentCatalog.Get(item.Kind).Slot == slot).ToArray();
        var replaced = equipped.Length >= _players[target].EquipmentSlotCapacity(slot) ? equipped[0] : null;
        if (replaced is not null)
        {
            CopyFirstReplacedWeapon(card, replaced);
            MoveCard(replaced, CardLocation.Equipment(target), CardLocation.DiscardPile, CardMoveReasons.EquipmentReplace);
        }
        MoveCard(card, from, CardLocation.Equipment(target), CardMoveReasons.EquipmentEnter);
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(frame.Id, target, slot, card.Id, card.Kind, replaced?.Id));
        AddLog("EquipmentChanged", $"{_players[frame.OwnerSeat].Name} 将【{card.DisplayName}】置入 {_players[target].Name} 的装备区。", target);
    }
}
