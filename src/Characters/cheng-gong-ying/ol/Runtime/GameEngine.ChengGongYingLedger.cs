namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool KuangxiangDrawGrantConsumed(long id) => ProgramEventHistory<KuangxiangRedDrawConsumedEvent>().Any(e => e.GrantFrameId == id);
    private int ChengGongYingDrawPenalty(int actorSeat)
    {
        if (!HasChengGongYingCapability || !IsValidPlayerSeat(actorSeat) || !_players[actorSeat].IsAlive) return 0;
        if (CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase && phase.ActorSeat == actorSeat && ProgramEventHistory<KuangxiangDrawConfirmedEvent>().Any(e => e.Phase == phase))
            return ProgramEventHistory<KuangxiangRedDrawConsumedEvent>().Where(e => e.Phase == phase).Sum(e => e.Count);
        return ProgramEventHistory<KuangxiangRedDrawIssuedEvent>().Where(e => e.Recipient == actorSeat && !KuangxiangDrawGrantConsumed(e.FrameId)).Sum(e => e.Count);
    }
    private void ConsumeChengGongYingDraw(int actorSeat)
    {
        if (!HasChengGongYingCapability) return;
        if (CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase || phase.ActorSeat != actorSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("Kuangxiang can expire only on the current confirmed actual Draw phase.");
        if (ProgramEventHistory<KuangxiangDrawConfirmedEvent>().Any(e => e.Phase == phase)) return;
        AdvanceEventRulesAndQueueFact(new KuangxiangDrawConfirmedEvent(phase));
        foreach (var grant in ProgramEventHistory<KuangxiangRedDrawIssuedEvent>().Where(e => e.Recipient == actorSeat && e.LastActualPhaseToken < phase.Token && !KuangxiangDrawGrantConsumed(e.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new KuangxiangRedDrawConsumedEvent(grant.FrameId, actorSeat, phase, grant.Count));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27100)]
    private void BindKuangxiangBlackUse(IGameEvent payload)
    {
        if (!HasChengGongYingCapability || payload is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { KuangxiangBlack: null, Action: { Type: CardActionType.Use, PhysicalCards: [var material] } action } use ||
            use.CardId != declared.CardId || use.SourceSeat != declared.SourceSeat || action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind || action.ProviderSeat != action.ActorSeat || material.From != CardLocation.Hand(action.ProviderSeat)) return;
        var grant = ProgramEventHistory<KuangxiangCardObtainedEvent>().Where(e => e.Recipient == action.ActorSeat && e.CardId == material.CardId && e.EffectiveSuit is Suit.Spade or Suit.Club &&
            _cardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(e.Recipient) && m.Reason.Value == KuangxiangDrawReason)).OrderBy(e => e.MovementSequence).LastOrDefault();
        if (grant is null) return;
        var r = new KuangxiangBlackUseReceipt(use.Id, action.ActionId, grant.FrameId, action.ActorSeat, action.ProviderSeat, material.CardId, grant.MovementSequence, action.EffectiveKind);
        UpdateLifecycleCardUse(use.Id, u => u with { KuangxiangBlack = r });
        AdvanceEventRulesAndQueueFact(new KuangxiangBlackUseBoundEvent(r.UseId, r.ActionId, r.GrantFrameId, r.ActorSeat, r.ProviderSeat, r.CardId, r.ObtainedSequence, r.EffectiveKind));
    }
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 110)]
    private int ChengGongYingKuangxiangDamageBonus(IDamageAttempt attack)
    {
        // The first recipient's finalized amount is the native propagation base.
        // Subsequent chained recipients inherit it rather than paying a second +1.
        if (!HasChengGongYingCapability || attack.IsChainPropagation) return 0;
        if (_resolutionStack.SingleOrDefault(f => f.Id == attack.ResolutionId) is JudgmentFrame { Continuation: JudgmentContinuationKind.Lightning, DelayedCardId: { } held, Succeeded: true } judgment && attack.IsDelayedJudgmentDamage && attack.EffectiveCardKind == CardKind.Lightning && attack.PhysicalCards.Select(c => c.Id).SequenceEqual([held]))
        {
            var placed = ProgramEventHistory<DelayedCardPlacedEvent>().LastOrDefault(e => e.CardId == held && e.CardKind == CardKind.Lightning);
            var bound = placed is null ? null : ProgramEventHistory<KuangxiangBlackUseBoundEvent>().SingleOrDefault(e => e.UseId == placed.ResolutionId && e.CardId == held && e.EffectiveKind == CardKind.Lightning && e.ActorSeat == placed.SourceSeat);
            if (bound is null) return 0;
            var delayed = new KuangxiangBlackUseReceipt(bound.UseId, bound.ActionId, bound.GrantFrameId, bound.ActorSeat, bound.ProviderSeat, bound.CardId, bound.ObtainedSequence, bound.EffectiveKind);
            if (judgment.KuangxiangBlack is null)
            {
                ReplaceRuntimeFrame(judgment.Id, judgment with { KuangxiangBlack = delayed });
                AdvanceEventRulesAndQueueFact(new KuangxiangLightningBoundEvent(judgment.Id, bound.UseId, bound.GrantFrameId, held));
            }
            else if (judgment.KuangxiangBlack != delayed) throw new InvalidOperationException("Kuangxiang Lightning lost its latest real delayed-card placement.");
            return 1;
        }
        if (LifecycleCardUse(attack.ResolutionId) is not { KuangxiangBlack: { } r } use) return 0;
        if (r.UseId != use.Id || attack.EffectiveCardKind != r.EffectiveKind || use.CardKind != r.EffectiveKind || use.Action is not { PhysicalCards: [var material] } action ||
            action.ActionId != r.ActionId || material.CardId != r.CardId || !ShownEntityUseActorMatches(use, r.ActorSeat, r.ProviderSeat) ||
            ProgramEventHistory<KuangxiangBlackUseBoundEvent>().Count(e => e.UseId == r.UseId && e.ActionId == r.ActionId && e.GrantFrameId == r.GrantFrameId && e.ActorSeat == r.ActorSeat && e.ProviderSeat == r.ProviderSeat && e.CardId == r.CardId && e.ObtainedSequence == r.ObtainedSequence && e.EffectiveKind == r.EffectiveKind) != 1 ||
            ProgramEventHistory<KuangxiangCardObtainedEvent>().Count(e => e.FrameId == r.GrantFrameId && e.CardId == r.CardId && e.Recipient == r.ActorSeat && e.MovementSequence == r.ObtainedSequence && e.EffectiveSuit is Suit.Spade or Suit.Club) != 1)
            throw new InvalidOperationException("Kuangxiang damage lost its exact obtained physical entity and originally issued accepted use.");
        return 1;
    }
    private bool TryReturnChengGongYingPaidPreparation(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (!HasChengGongYingCapability || frame.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || frame.Continuation != ProgramLifecycleContinuation.NormalTurnStart || frame.OwnerSeat != _currentSeat || frame.OwnerSeat != _turnProgression.OwnerSeat || _turnProgression.TurnNumber != _turnNumber || !IsValidPlayerSeat(frame.OwnerSeat) || _winner == Winner.None && _players[frame.OwnerSeat].IsAlive || frame.CandidateIndex != frame.Candidates.Count || _resolutionStack.Count != 0) return false;
        var paid = ProgramEventHistory<ChengGongYingStartedEvent>().Where(start => start.WindowId == frame.Id && start.Turn == _turnNumber && start.TurnOwner == frame.OwnerSeat && start.Source.OwnerSeat == frame.OwnerSeat &&
            frame.Candidates.Any(c => c.OwnerSeat == start.Source.OwnerSeat && c.SkillId == start.Source.SkillId && c.BindingId == start.Source.BindingId && c.SkillInstanceId == start.Source.SkillInstanceId && c.GameplayHash == start.GameplayHash && GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.AdjustKuangxiangHandToFour }]) &&
            ProgramEventHistory<ChengGongYingCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.Applied) == 1 && ProgramEventHistory<KuangxiangAdjustmentIssuedEvent>().Count(e => e.FrameId == start.FrameId) == 1).ToArray();
        if (paid.Length == 0) return false;
        if (ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == frame.OwnerSeat)) throw new InvalidOperationException("Kuangxiang cannot finalize its already ended actual turn twice.");
        ClearActualDiscardRecoveryPhase(); if (_winner == Winner.None) FinalizeEndTurn(_players[frame.OwnerSeat]); return true;
    }
}
