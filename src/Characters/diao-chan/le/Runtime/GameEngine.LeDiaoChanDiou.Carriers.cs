namespace CardGame.Core;

// A carrier represents the continuing equipment/judgment placement of one
// genuine zero-material Use. It never becomes a new reusable physical card.
public sealed record DiouCarrierDestroyedEvent(long OriginalProgramFrameId, CardConversionSource Source,
    string GameplayHash, int CardId, CardKind Kind, long ParentFrameId, int MovementSequence,
    CardLocation From, CardMoveReason Reason) : IGameEvent;

public sealed partial class GameEngine
{
    private bool IsDiouCarrier(int id) => id > 0 && HasDiouCapability && _generatedPhysicalCardIds.Contains(id) &&
        ProgramEventHistory<DiouCarrierCreatedEvent>().Where(e => e.CardId == id).ToArray() is [var fact] &&
        fact.Source.SkillId == DiouSkill && fact.Source.BindingId == DiouBinding &&
        IsValidPlayerSeat(fact.Source.OwnerSeat) &&
        _contentRegistry.GetSkill(fact.Source.SkillId).Program?.GameplayHash == fact.GameplayHash &&
        (EquipmentCatalog.IsEquipment(fact.Kind) || IsDelayedCard(fact.Kind)) &&
        GetAdvancedCard(id) is { Suit: Suit.None, Rank: 0 } card && card.Kind == fact.Kind;

    private CardLocation NormalizeDiouCarrierDestination(Card card, CardLocation from, CardLocation to)
    {
        if (!IsDiouCarrier(card.Id) || from == to || to == CardLocation.OutsideGame) return to;
        if (from == CardLocation.OutsideGame)
        {
            // Exactly the original issued Use can bring a fresh carrier into
            // Processing. Destruction is final, even if a later ability searches
            // all registered entities rather than only the draw pile.
            if (to == CardLocation.Processing && !_cardMovements.Any(m => m.CardId == card.Id) &&
                _resolutionStack.LastOrDefault() is CardUseFrame { DiouOrigin: { } origin } use &&
                origin.CarrierId == card.Id && origin.Kind == card.Kind && IsIssuedDiouUse(use)) return to;
            return CardLocation.OutsideGame;
        }
        // A continuing field effect can change its field holder without
        // inventing a Hand/deck entity. This also preserves native equipment
        // exchange and Wooden Ox grain-transfer semantics.
        if (from.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment)
            return to.Zone == from.Zone ? to : CardLocation.OutsideGame;
        return to.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.Processing
            ? to : CardLocation.OutsideGame;
    }

    private void RecordDiouCarrierDestruction(CardMovementRecord movement)
    {
        if (movement.To != CardLocation.OutsideGame || !IsDiouCarrier(movement.CardId)) return;
        var original = ProgramEventHistory<DiouCarrierCreatedEvent>().Single(e => e.CardId == movement.CardId);
        if (movement.From == CardLocation.OutsideGame ||
            ProgramEventHistory<DiouCarrierDestroyedEvent>().Any(e => e.CardId == movement.CardId))
            throw new InvalidOperationException("A Diou carrier cannot be destroyed twice or re-enter after destruction.");
        AdvanceEventRulesAndQueueFact(new DiouCarrierDestroyedEvent(original.FrameId, original.Source,
            original.GameplayHash, movement.CardId, movement.CardKind, _resolutionStack.LastOrDefault()?.Id ?? 0,
            movement.Sequence, movement.From, movement.Reason));
    }

    // Every accepted destruction is tied to its original unique generated
    // identity and the exact real movement, not just an OutsideGame location.
    private bool IsDiouCarrierDestroyedMaterial(CardMovementRecord movement) =>
        movement.To == CardLocation.OutsideGame && IsDiouCarrier(movement.CardId) &&
        _cardMovements.Contains(movement) &&
        ProgramEventHistory<DiouCarrierDestroyedEvent>().Where(e => e.CardId == movement.CardId).ToArray() is [var fact] &&
        fact.MovementSequence == movement.Sequence && fact.From == movement.From &&
        fact.Kind == movement.CardKind && fact.Reason == movement.Reason &&
        ProgramEventHistory<DiouCarrierCreatedEvent>().Any(created => created.CardId == fact.CardId &&
            created.FrameId == fact.OriginalProgramFrameId && created.Source == fact.Source &&
            created.GameplayHash == fact.GameplayHash && created.Kind == fact.Kind);

    private bool IsDiouCarrierDestroyedMaterial(int cardId, CardLocation from, CardLocation to) =>
        to == CardLocation.OutsideGame && ProgramEventHistory<DiouCarrierDestroyedEvent>()
            .Where(e => e.CardId == cardId && e.From == from).ToArray() is [var fact] &&
        _cardMovements.SingleOrDefault(m => m.Sequence == fact.MovementSequence) is { } movement &&
        IsDiouCarrierDestroyedMaterial(movement);

    private bool IsDiouCarrierDestroyedMaterial(long useId, int cardId) =>
        LifecycleCardUse(useId) is { Action: { } action } &&
        action.PhysicalCards.SingleOrDefault(c => c.CardId == cardId) is { } material &&
        ProgramEventHistory<DiouCarrierDestroyedEvent>().Where(e => e.CardId == cardId).ToArray() is [var fact] &&
        fact.ParentFrameId == useId && fact.From == material.From && fact.Kind == material.CardKind &&
        _cardMovements.SingleOrDefault(m => m.Sequence == fact.MovementSequence) is { } movement &&
        IsDiouCarrierDestroyedMaterial(movement);

    private void RetireUnplacedDiouCarrier(long useId)
    {
        if (LifecycleCardUse(useId) is not { DiouOrigin: { CarrierId: > 0 } origin } use || !IsIssuedDiouUse(use)) return;
        if (_cardZones.GetLocation(origin.CarrierId) == CardLocation.Processing)
            MoveCard(GetAdvancedCard(origin.CarrierId), CardLocation.Processing, CardLocation.OutsideGame,
                new("skill-program.le-diao-chan.diou.carrier-retired"));
    }
}
