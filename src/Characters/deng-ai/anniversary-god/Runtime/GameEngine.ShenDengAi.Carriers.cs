namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsShenDaCarrier(int id) => id > 0 && _generatedPhysicalCardIds.Contains(id) &&
        ProgramEventHistory<ShenDengAiCarrierCreatedEvent>().Where(e => e.CardId == id).ToArray() is [var f] &&
        f.Source.SkillId == ShenDaCuixin && _contentRegistry.GetSkill(f.Source.SkillId).Program?.GameplayHash == f.GameplayHash &&
        GetAdvancedCard(id) is { Suit: Suit.None, Rank: 0 } c && c.Kind == f.Kind && IsDelayedCard(c.Kind);
    private CardLocation NormalizeShenDaCarrierDestination(Card card, CardLocation from, CardLocation to)
    {
        if (!IsShenDaCarrier(card.Id) || from == to || to == CardLocation.OutsideGame) return to;
        if (from == CardLocation.OutsideGame)
            return to == CardLocation.Processing && !_cardMovements.Any(m => m.CardId == card.Id) &&
                _resolutionStack.LastOrDefault() is CardUseFrame { ShenDengAiCuixin: { } o } u && o.CarrierId == card.Id && IsShenDaCuixinUse(u.Id) ? to : CardLocation.OutsideGame;
        if (from.Zone == CardZoneKind.Judgment) return to.Zone == CardZoneKind.Judgment ? to : CardLocation.OutsideGame;
        return to.Zone is CardZoneKind.Judgment or CardZoneKind.Processing ? to : CardLocation.OutsideGame;
    }
    private void RecordShenDaCarrierDestruction(CardMovementRecord m)
    {
        if (m.To != CardLocation.OutsideGame || !IsShenDaCarrier(m.CardId)) return;
        if (m.From == CardLocation.OutsideGame || ProgramEventHistory<ShenDengAiCarrierDestroyedEvent>().Any(e => e.CardId == m.CardId))
            throw new InvalidOperationException("A Cuixin delayed-card carrier cannot be destroyed twice or re-enter a reusable zone.");
        AdvanceEventRulesAndQueueFact(new ShenDengAiCarrierDestroyedEvent(m.CardId, m.Sequence, m.From, m.Reason));
    }
    private bool IsShenDaCarrierDestroyedMaterial(CardMovementRecord m) => m.To == CardLocation.OutsideGame && IsShenDaCarrier(m.CardId) &&
        _cardMovements.Contains(m) && ProgramEventHistory<ShenDengAiCarrierDestroyedEvent>().Where(e => e.CardId == m.CardId).ToArray() is [var f] &&
        f.MovementSequence == m.Sequence && f.From == m.From && f.Reason == m.Reason;
    private bool IsShenDaCarrierDestroyedMaterial(int id, CardLocation from, CardLocation to) => to == CardLocation.OutsideGame &&
        _cardMovements.Any(m => m.CardId == id && m.From == from && m.To == to && IsShenDaCarrierDestroyedMaterial(m));
    private bool IsShenDaCarrierDestroyedMaterial(long use, int id) => LifecycleCardUse(use)?.Action?.PhysicalCards.SingleOrDefault(c => c.CardId == id) is { } c &&
        IsShenDaCarrierDestroyedMaterial(id, c.From, CardLocation.OutsideGame);
    private void RetireUnplacedShenDaCarrier(long id)
    {
        if (LifecycleCardUse(id) is { ShenDengAiCuixin: { CarrierId: > 0 } o } && IsShenDaCuixinUse(id) && _cardZones.GetLocation(o.CarrierId) == CardLocation.Processing)
            MoveCard(GetAdvancedCard(o.CarrierId), CardLocation.Processing, CardLocation.OutsideGame, new("skill-program.shen-deng-ai.cuixin.carrier-retired"));
    }
}
