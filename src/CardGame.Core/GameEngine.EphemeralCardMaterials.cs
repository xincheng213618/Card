namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Native movement and payment proofs share one route for generated field
    // cards. Each producer still validates its own exact creation/destruction.
    private CardLocation NormalizeEphemeralCardDestination(Card card, CardLocation from, CardLocation to) =>
        NormalizeDagongCheDestination(card, from, NormalizeHappyZuoCiPhantomDestination(card, from,
            NormalizeShenDaCarrierDestination(card, from, NormalizeWushuangShopEquipmentDestination(card, from, NormalizeDiouCarrierDestination(card, from, to)))));

    private bool IsEphemeralCardDestroyedMaterial(CardMovementRecord movement) =>
        IsDagongCheDestroyedMaterial(movement) || IsHappyZuoCiDestroyedMaterial(movement) || IsShenDaCarrierDestroyedMaterial(movement) || IsDiouCarrierDestroyedMaterial(movement) || IsWushuangShopDestroyedMaterial(movement);

    private bool IsEphemeralCardDestroyedMaterial(int cardId, CardLocation from, CardLocation to) =>
        IsDagongCheDestroyedMaterial(cardId, from, to) || IsHappyZuoCiDestroyedMaterial(cardId, from, to) || IsShenDaCarrierDestroyedMaterial(cardId, from, to) || IsDiouCarrierDestroyedMaterial(cardId, from, to) || to == CardLocation.OutsideGame &&
        _cardMovements.Any(m => m.CardId == cardId && m.From == from && m.To == to && IsWushuangShopDestroyedMaterial(m));

    private bool IsEphemeralCardDestroyedMaterial(long useId, int cardId) =>
        IsDagongCheDestroyedMaterial(useId, cardId) || IsHappyZuoCiDestroyedMaterial(useId, cardId) || IsShenDaCarrierDestroyedMaterial(useId, cardId) || IsDiouCarrierDestroyedMaterial(useId, cardId) || LifecycleCardUse(useId)?.Action?.PhysicalCards
            .SingleOrDefault(c => c.CardId == cardId) is { } material &&
        ProgramEventHistory<WushuangShopEquipmentDestroyedEvent>().Any(fact => fact.CardId == cardId && fact.ParentFrameId == useId &&
            _cardMovements.SingleOrDefault(m => m.Sequence == fact.MovementSequence) is { } movement &&
            movement.From == material.From && movement.CardKind == material.CardKind && IsWushuangShopDestroyedMaterial(movement));
}
