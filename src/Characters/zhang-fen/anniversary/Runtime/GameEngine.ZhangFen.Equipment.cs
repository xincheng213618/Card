namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasDagongCheCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack);
    private static string DagongGrant(int id) => "equipment:anniversary:dagong-che:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private bool IsRealDagongChe(int id) => id > 0 && _generatedPhysicalCardIds.Contains(id) &&
        ProgramEventHistory<DagongCheCreatedEvent>().Where(e => e.CardId == id).ToArray() is [var made] &&
        made.Source.SkillId == ZhangFenComposition.Wanglu && made.Source.BindingId == "preparation-cart-or-play" && made.OwnerSeat == made.Source.OwnerSeat &&
        _contentRegistry.GetSkill(made.Source.SkillId).Program?.GameplayHash == made.Hash && GetAdvancedCard(id) is { Kind: CardKind.DagongChe, Suit: Suit.Spade, Rank: 9 };
    private DagongCheUpgradedEvent[] DagongUpgrades(int id) => IsRealDagongChe(id) ? ProgramEventHistory<DagongCheUpgradedEvent>().Where(e => e.CardId == id).OrderBy(e => e.Ordinal).ToArray() : [];
    private Card? HeldDagongChe(int owner) => GetEquipment(_players[owner]).FirstOrDefault(c => c.Kind == CardKind.DagongChe && IsRealDagongChe(c.Id) && !IsHappyWeiYanBone(c.Id));
    private bool IsUnupgradedDagongCheDiscardForbidden(int actor, Card card, CardLocation from, OwnedCardMoveIntent intent) =>
        intent == OwnedCardMoveIntent.Discard && from.Zone == CardZoneKind.Equipment && card.Kind == CardKind.DagongChe && IsRealDagongChe(card.Id) &&
        _cardZones.Contains(from, card.Id) && DagongUpgrades(card.Id).Length == 0;

    private CardLocation NormalizeDagongCheDestination(Card card, CardLocation from, CardLocation to)
    {
        if (card.Kind != CardKind.DagongChe || !IsRealDagongChe(card.Id) || from == to) return to;
        if (from.Zone == CardZoneKind.Equipment) return CardLocation.OutsideGame;
        if (from != CardLocation.OutsideGame) return CardLocation.OutsideGame;
        // Only the once-issued preparation placement may bring a fresh real
        // generated card from OutsideGame into Equipment. Destruction is final.
        return to.Zone == CardZoneKind.Equipment && !_cardMovements.Any(m => m.CardId == card.Id) &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhangFen: { Stage: ZhangFenStage.PlaceChildren } r } f && r.CartId == card.Id &&
            to.OwnerSeat == f.OwnerSeat && ValidZhangFen(f) ? to : CardLocation.OutsideGame;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 43500)]
    private void ObserveDagongCheMovement(CardMovedEvent moved)
    {
        if (moved.CardKind != CardKind.DagongChe || !IsRealDagongChe(moved.CardId)) return;
        if (moved.From is { Zone: CardZoneKind.Equipment, OwnerSeat: { } old })
        {
            _observedSkillGrantRevisions.TryAdd(old, _players[old].SkillGrants.Revision);
            _players[old].SkillGrants.RemoveGrant(DagongGrant(moved.CardId));
            if (moved.To != CardLocation.OutsideGame || ProgramEventHistory<DagongCheDestroyedEvent>().Any(e => e.CardId == moved.CardId))
                throw new InvalidOperationException("A real Dagong Che must be destroyed exactly once on leaving Equipment.");
            var movement = _cardMovements.Last(m => m.CardId == moved.CardId);
            AdvanceEventRulesAndQueueFact(new DagongCheDestroyedEvent(moved.CardId, old, movement.Sequence,
                _resolutionStack.LastOrDefault()?.Id ?? 0, movement.Reason, DagongUpgrades(moved.CardId).Length));
        }
        if (moved.To is { Zone: CardZoneKind.Equipment, OwnerSeat: { } holder })
        {
            _observedSkillGrantRevisions.TryAdd(holder, _players[holder].SkillGrants.Revision);
            _players[holder].SkillGrants.Grant(new(DagongGrant(moved.CardId), ZhangFenComposition.Cart, DagongGrant(moved.CardId), DagongGrant(moved.CardId)));
        }
    }
    private bool IsDagongCheDestroyedMaterial(CardMovementRecord m) => m.To == CardLocation.OutsideGame && m.From.Zone == CardZoneKind.Equipment &&
        IsRealDagongChe(m.CardId) && _cardMovements.Contains(m) && ProgramEventHistory<DagongCheDestroyedEvent>().Where(e => e.CardId == m.CardId).ToArray() is [var e] &&
        e.FormerOwnerSeat == m.From.OwnerSeat && e.MovementSequence == m.Sequence && e.Reason == m.Reason && e.Upgrades == DagongUpgrades(m.CardId).Length;
    private bool IsDagongCheDestroyedMaterial(int cardId, CardLocation from, CardLocation to) => to == CardLocation.OutsideGame && cardId > 0 &&
        _cardMovements.Any(m => m.CardId == cardId && m.From == from && m.To == to && IsDagongCheDestroyedMaterial(m));
    private bool IsDagongCheDestroyedMaterial(long useId, int cardId) => LifecycleCardUse(useId)?.Action is { } action &&
        action.PhysicalCards.SingleOrDefault(c => c.CardId == cardId) is { } cost &&
        ProgramEventHistory<DagongCheDestroyedEvent>().Any(e => e.CardId == cardId && e.ParentId == useId &&
            _cardMovements.Any(m => m.Sequence == e.MovementSequence && m.From == cost.From && m.CardKind == cost.CardKind && IsDagongCheDestroyedMaterial(m)));
}
