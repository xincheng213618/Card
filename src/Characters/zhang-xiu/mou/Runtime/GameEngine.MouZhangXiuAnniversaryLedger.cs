namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void CaptureMouZhangXiuAnniversaryDiscards(CardMovementBatchContext batch)
    {
        if (!HasMouZhangXiuAnniversary) return;
        foreach (var m in batch.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To))
            AdvanceEventRulesAndQueueFact(new MouZhangXiuAnniversaryDiscardEvent(batch.TurnNumber, m.CardId, m.Sequence));
    }
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 36600)] private bool MouZhangXiuAnniversaryFinalizedDamageUse(ProgramTargetCardWindowQuery query) => HasMouZhangXiuAnniversary && MouZhangXiuAnniversaryRuntime.DamageCard(query.Kind) && IsOrdinaryTrick(query.Kind);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 36600)] private void ObserveMouZhangXiuAnniversaryDamage(IGameEvent payload)
    {
        if (!HasMouZhangXiuAnniversary || payload is not DamageAppliedEvent { Amount: > 0 } applied || CurrentDamageAttempt is not CardAttackHandle card || !card.DamageWasApplied || card.IsProgramSkillDamage || card.IsProgramJudgmentDamage || _resolutionStack.LastOrDefault() is not DamageFrame damage || damage.ParentFrameId != card.ResolutionId || damage.TargetSeat != applied.TargetSeat || damage.SourceSeat != applied.SourceSeat || damage.Amount != applied.Amount || !ProgramEventHistory<DamageRequestedEvent>().Any(e => e.ResolutionId == damage.Id && e.TargetSeat == applied.TargetSeat && e.SourceSeat == applied.SourceSeat && e.Amount == applied.Amount && e.SourceCard == card.EffectiveCardKind && e.SourceLess == card.IsSourceLess)) return;
        if (card.EffectiveCardKind is not { } effectiveKind) return;
        IReadOnlyList<int> physical;
        if (card.IsDelayedJudgmentDamage)
        {
            if (card.EffectiveCardKind != CardKind.Lightning || _resolutionStack.SingleOrDefault(f => f.Id == card.ResolutionId) is not JudgmentFrame { Continuation: JudgmentContinuationKind.Lightning, DelayedCardId: { } id } judgment || card.Card?.Id != id || card.PhysicalCards is not [var entity] || entity.Id != id || !ProgramEventHistory<LightningResolvedEvent>().Any(e => e.ResolutionId == judgment.Id && e.CardId == id && e.Hit && e.JudgmentTargetSeat == judgment.TargetSeat)) return;
            physical = [id];
        }
        else
        {
            if (LifecycleCardUse(card.ResolutionId) is not { Action: { Type: CardActionType.Use } a } use || use.CardKind != card.EffectiveCardKind || !CompletedUndamagedNativeUseActorMatches(use, card) || !HasExactAcceptedCompletedUndamagedUse(use, a) || use.PhysicalCardIds is not { } ids || !card.PhysicalCards.Select(c => c.Id).SequenceEqual(ids) && !HasExactCompletedUndamagedTunanVirtualAttack(use, a, card)) return;
            physical = ids;
        }
        foreach (var id in physical.Where(id => id > 0).Distinct())
            if (!ProgramEventHistory<MouZhangXiuAnniversaryDamageEvent>().Any(e => e.DamageFrameId == damage.Id && e.CardId == id)) AdvanceEventRulesAndQueueFact(new MouZhangXiuAnniversaryDamageEvent(_turnNumber, card.ResolutionId, damage.Id, id, effectiveKind, applied.TargetSeat, applied.Amount, card.IsDelayedJudgmentDamage));
    }
}
