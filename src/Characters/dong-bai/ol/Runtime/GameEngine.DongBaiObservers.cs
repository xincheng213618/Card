namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsXiaHuiPhysicalRestricted(int actorSeat, int cardId) => IsValidPlayerSeat(actorSeat) &&
        _players[actorSeat].XiaHuiRestrictions?.Any(r => r.RecipientSeat == actorSeat && r.CardId == cardId && _cardZones.GetLocation(cardId) == CardLocation.Hand(actorSeat)) == true;
    private bool IsXiaHuiDiscardForbidden(int actorSeat, Card card, CardLocation from, OwnedCardMoveIntent intent) =>
        intent == OwnedCardMoveIntent.Discard && from == CardLocation.Hand(actorSeat) && IsXiaHuiPhysicalRestricted(actorSeat, card.Id);
    private bool IsXiaHuiHandLimitExempt(int owner, Card card) => _players[owner].IsAlive && _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner) &&
        CardPolicies(_players[owner], SkillProgramCardPolicyKind.XiaHuiBlackHandAndGainedEntityRestrictions).Any() && EffectiveSuit(_players[owner], card) is Suit.Spade or Suit.Club;
    private void ObserveDongBaiMovement(CardMovementRecord movement, IReadOnlyList<XiaHuiMovementSource>? originalSources = null)
    {
        if (movement.From is { Zone: CardZoneKind.Hand, OwnerSeat: { } departed } && _players[departed].XiaHuiRestrictions is { Count: > 0 } old)
        {
            foreach (var r in old.Where(r => r.CardId == movement.CardId)) AdvanceEventRulesAndQueueFact(new XiaHuiRestrictionReleasedEvent(departed, r.CardId, movement.Sequence, "entity-left-hand"));
            _players[departed].XiaHuiRestrictions = Array.AsReadOnly(old.Where(r => r.CardId != movement.CardId).ToArray());
        }
        if (movement.From.OwnerSeat is not { } owner || movement.To is not { Zone: CardZoneKind.Hand, OwnerSeat: { } recipient } || owner == recipient || !_players[recipient].IsAlive || originalSources is null) return;
        foreach (var item in originalSources.Where(s => s.Source.OwnerSeat == owner && s.OriginalSuit is Suit.Spade or Suit.Club))
        {
            var restriction = new XiaHuiCardRestriction(item.Source, item.GameplayHash, recipient, movement.CardId, movement.Sequence, item.OriginalSuit);
            var recipientExisting = _players[recipient].XiaHuiRestrictions ?? [];
            if (recipientExisting.Any(r => r.Source == restriction.Source && r.CardId == restriction.CardId && r.MovementSequence == movement.Sequence)) continue;
            _players[recipient].XiaHuiRestrictions = Array.AsReadOnly(recipientExisting.Append(restriction).ToArray()); AdvanceEventRulesAndQueueFact(new XiaHuiRestrictionGrantedEvent(restriction));
        }
    }
    private void ObserveDongBaiHpDecreased(int seat, int before, int after)
    {
        if (after >= before || _players[seat].XiaHuiRestrictions is not { Count: > 0 } restrictions) return;
        _players[seat].XiaHuiRestrictions = null;
        foreach (var r in restrictions) AdvanceEventRulesAndQueueFact(new XiaHuiRestrictionReleasedEvent(seat, r.CardId, _movementSequence, "recipient-hp-decreased"));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 580)]
    private void ObserveDongBaiRecipientDeath(PlayerDiedEvent died) => CleanupDongBaiRecipientDeath(died.VictimSeat);

    private void CleanupDongBaiRecipientDeath(int seat)
    {
        if (_players[seat].IsAlive || _players[seat].XiaHuiRestrictions is not { Count: > 0 } restrictions) return;
        _players[seat].XiaHuiRestrictions = null;
        foreach (var r in restrictions) AdvanceEventRulesAndQueueFact(new XiaHuiRestrictionReleasedEvent(seat, r.CardId, _movementSequence, "recipient-died"));
    }
    private bool LianZhuFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!CanContinueLianZhu(root) || root.LianZhu is not { } r || child is not CardsMovedTriggerWindowFrame moved) return false;
        return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == LianZhuReason(r.Stage));
    }
    private ProgramSkillFrame? LianZhuObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.LianZhuFirstChild(root, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    private bool HasLianZhuDamageObserver(long id) => LianZhuObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsLianZhuDying() => ActiveDying is { } dying && LianZhuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsLianZhuNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id || observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) || LianZhuObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = GetPausedPrivateOfferEffect(observer); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool TryAdvanceLianZhuSubtree() => _pendingDecision is null && LianZhuObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
