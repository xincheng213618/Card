namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A physical card's printed category is unchanged when a rule places it as
    // a bone. Slot occupancy and printed equipment abilities are separate facts.
    private EquipmentSlot FieldEquipmentSlot(Card card) =>
        HappyBoneSlot(card) ?? EquipmentCatalog.Get(card.Kind).Slot;

    private bool HasHappyWeiYanPotentialCancellation(int seat) =>
        HasHappyWeiYanEzhiCapability && GetEquipment(seat).Any(card => IsHappyWeiYanBone(card.Id));

    private CardSnapshot ProjectEquipmentCard(Card card, int viewerSeat, bool revealAll)
    {
        var snapshot = ToSnapshot(card);
        return snapshot.IsBone && (revealAll || HappyBoneRecord(card.Id)?.Source.OwnerSeat == viewerSeat)
            ? snapshot with { Kind = card.Kind, DisplayName = card.DisplayName, SkillMarkLabel = "骨" }
            : snapshot;
    }
}
