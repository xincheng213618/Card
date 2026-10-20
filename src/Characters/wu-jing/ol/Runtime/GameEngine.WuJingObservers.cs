namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27130)]
    private void AssertWuJing(ProgramSkillFrame f)
    {
        if (f.WuJing is not { } r) return;
        if (f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context || GetPausedPrivateOfferEffect(f)?.Op != r.Operation || !WuJingComposition.Handles(r.Operation) ||
            r.Source != WuJingSource(f) || r.GameplayHash != f.GameplayHash || r.WindowId != context.ParentFrameId || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Entities.Select(c => c.CardId).Distinct().Count() != r.Entities.Count ||
            ProgramEventHistory<WuJingStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.WindowId == r.WindowId && e.ActualTurn == r.ActualTurn && e.UseId == r.UseId && e.ActionId == r.ActionId && e.TargetSeat == r.TargetSeat) != 1)
            throw new InvalidOperationException("Wu Jing lost his exact owning instruction, immutable entities or native start invoice.");
        var at = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (at < 1) throw new InvalidOperationException("Wu Jing lost his retained native trigger parent.");
        if (r.Operation == SkillProgramEffectOp.ClaimLiubingUndamagingSlash)
        {
            if (r.Stage != WuJingStage.ClaimChildren || context.Window != SkillProgramTriggerWindow.DiscardPileReceived || context.MovementBatch is not { } batch || _resolutionStack[at - 1] is not CardsMovedTriggerWindowFrame parent ||
                parent.Id != r.WindowId || parent.Id != batch.Id || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
                r.Entities.Count == 0 || r.Entities.Any(entity => entity.From != CardLocation.DiscardPile || !ProgramEventHistory<WuJingLiubingSettledEvent>().Any(e => e.UseId == r.UseId && e.ActionId == r.ActionId && e.BatchId == batch.Id && e.FinalActor == r.TargetSeat && e.FinalActor != f.OwnerSeat && e.CardId == entity.CardId && e.MovementSequence == entity.Sequence)) ||
                ProgramEventHistory<WuJingClaimIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId && e.BatchId == batch.Id && e.Count == r.Entities.Count && e.Before == r.Before && e.After == r.After) != 1 ||
                _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).Any(m => m.From != CardLocation.DiscardPile || m.To != CardLocation.Hand(f.OwnerSeat) || m.Reason.Value != WuJingClaimReason || !r.Entities.Any(e => e.CardId == m.CardId)) ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == WuJingClaimReason) != r.Entities.Count)
                throw new InvalidOperationException("Liubing lost its exact complete zero-damage Use, other actor's actual Play or once-claimed discard entities.");
        }
        else
        {
            if (context.Window != SkillProgramTriggerWindow.CardUseCompleted || _resolutionStack[at - 1] is not ProgramCardTriggerWindowFrame parent || parent.Id != r.WindowId || parent.ParentFrameId != r.UseId || parent.Action.ActionId != r.ActionId ||
                parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex])))
                throw new InvalidOperationException("Heji lost the exact completed native Use and current trigger cursor.");
            if (r.Operation == SkillProgramEffectOp.RewardHejiUnconvertedUse)
            {
                if (r.Stage != WuJingStage.RewardChildren || r.NativeOrigin is not { RewardEligible: true } origin || origin.UseId != r.UseId || origin.ActionId != r.ActionId || origin.Source.OwnerSeat != f.OwnerSeat || origin.Source.SkillId != f.SkillId || origin.Source.SkillInstanceId != f.SkillInstanceId || origin.GameplayHash != f.GameplayHash ||
                    LifecycleCardUse(r.UseId) is not { WuJingHeji: { Rewarded: true } issued } || issued.Origin != origin ||
                    ProgramEventHistory<WuJingRewardIssuedEvent>().Count(e => e.FrameId == f.Id && e.ProgramFrameId == origin.ProgramFrameId && e.UseId == r.UseId && e.CardId == r.RewardCardId && e.Before == r.Before && e.After == r.After) != 1 ||
                    (r.RewardCardId is { } card ? _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray() is not [var moved] || moved.CardId != card || moved.From != CardLocation.DrawPile || moved.To != CardLocation.Hand(f.OwnerSeat) || moved.Reason.Value != WuJingRewardReason || GetAdvancedCard(card).Suit is not (Suit.Heart or Suit.Diamond) : r.Before != r.After))
                    throw new InvalidOperationException("Heji's reward lost its one genuinely unconverted native Use and seeded real red deck entity.");
            }
            else if (r.Stage is WuJingStage.NativeUse or WuJingStage.NativeChildren)
            {
                if (r.NativeOrigin is not { } origin || origin.ProgramFrameId != f.Id || origin.Source != r.Source || origin.GameplayHash != r.GameplayHash || origin.IncomingUseId != r.UseId || origin.IncomingActionId != r.ActionId || origin.ActualTurn != r.ActualTurn || origin.ActorSeat != f.OwnerSeat || origin.TargetSeat != r.TargetSeat ||
                    r.Selection is not { } selected || selected.Kind != origin.Kind || !selected.CardIds.SequenceEqual(origin.Materials.Select(m => m.CardId)) ||
                    origin.Materials.Count == 0 && !WuJingZeroPaymentProof(f, r) ||
                    ProgramEventHistory<WuJingHejiPaidEvent>().Count(e => e.ProgramFrameId == f.Id && e.UseId == origin.UseId && e.Before == r.Before && e.After == r.After) != 1 ||
                    (r.Stage == WuJingStage.NativeUse ? r.NativeCompleted && LifecycleCardUse(origin.UseId) is not null : !r.NativeCompleted || LifecycleCardUse(origin.UseId) is not null || ProgramEventHistory<WuJingHejiReturnedEvent>().Count(e => e.ProgramFrameId == f.Id && e.UseId == origin.UseId) != 1))
                    throw new InvalidOperationException("Heji lost its exact once-paid native physical attack and complete typed return.");
            }
            else if (r.Stage is not (WuJingStage.UseChoice or WuJingStage.IssuingUse or WuJingStage.FactionRequest)) throw new InvalidOperationException("Heji entered another instruction's paid stage.");
        }
        var nativePrompt = _pendingDecision is { } currentPrompt ? KenshangProgramNativeDecision(ChainedStateBasicNativeDecision(DrawFundedDistinctBasicNativeDecision(WeijieNativeDecision(currentPrompt)))) : null;
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage == WuJingStage.UseChoice && (nativePrompt is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(prompt.Choices, WuJingChoices(f))))
            throw new InvalidOperationException("Heji lost its real private chosen-card boundary.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 600)]
    private bool IsWuJingMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.WuJing is { Stage: WuJingStage.NativeChildren or WuJingStage.RewardChildren or WuJingStage.ClaimChildren } r && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && (e is null || e.Op == r.Operation);
    private bool IsWuJingNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext batch) => f.WuJing is { Stage: WuJingStage.NativeChildren, NativeCompleted: true, NativeOrigin: { } origin } r &&
        batch.ParentFrameId == origin.UseId && batch.AwaitingProgramFrameId is null && batch.Movements.Count > 0 && ProgramEventHistory<WuJingHejiReturnedEvent>().Count(e => e.ProgramFrameId == f.Id && e.UseId == origin.UseId) == 1 &&
        batch.Movements.All(m => m.Sequence > r.After && _cardMovements.Contains(m) && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished && origin.Materials.Any(e => e.CardId == m.CardId && e.PrintedKind == m.CardKind));
    private bool WuJingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.WuJing is not { } r) return false;
        if (child is DrawFundedDistinctBasicFrame draw) return IsWuJingHejiRequest(f) && draw.Payment.ParentFrameId == f.Id && draw.Payment.RequestFrameId == f.Id && draw.Payment.Cursor == f.InstructionIndex && draw.Payment.Source.OwnerSeat == f.OwnerSeat && draw.Payment.TargetSeat == r.TargetSeat && draw.OriginalDecision.SkillPrompt?.SkillId == f.SkillId && draw.OriginalDecision.PlayerSeat == f.OwnerSeat && ProgramEventHistory<DrawFundedDistinctBasicStartedEvent>().Any(e => e.PaymentFrameId == draw.Id && e.ParentFrameId == f.Id && e.RequestFrameId == f.Id && e.OriginalPromptId == draw.OriginalDecision.PromptId);
        if (child is ChainedStateBasicFrame chain) return IsWuJingHejiRequest(f) && chain.Payment.ParentFrameId == f.Id && chain.Payment.RequestFrameId == f.Id && chain.Payment.Cursor == f.InstructionIndex && chain.Payment.Source.OwnerSeat == f.OwnerSeat && chain.Payment.TargetSeat == r.TargetSeat && chain.OriginalDecision.SkillPrompt?.SkillId == f.SkillId && chain.OriginalDecision.PlayerSeat == f.OwnerSeat && ValidChainedStateBasicPayment(chain.Payment);
        if (child is CardUseFrame use) return r.Stage == WuJingStage.NativeUse && use.WuJingHeji?.Origin == r.NativeOrigin && IsWuJingHejiUse(use);
        if (f.PendingMovementContinuation is null) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return r.Stage == WuJingStage.NativeChildren ? moved.ResumeProgramFrameId == f.Id && IsWuJingNativeSettlementBatch(f, moved.Batch) :
                r.Stage is WuJingStage.RewardChildren or WuJingStage.ClaimChildren && ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && m.To == CardLocation.Hand(f.OwnerSeat) &&
                    (r.Stage == WuJingStage.RewardChildren ? m.From == CardLocation.DrawPile && m.CardId == r.RewardCardId && m.Reason.Value == WuJingRewardReason : m.From == CardLocation.DiscardPile && r.Entities.Any(e => e.CardId == m.CardId) && m.Reason.Value == WuJingClaimReason));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.ResumeProgramFrameId == f.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
    }
    private ProgramSkillFrame? WuJingObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.WuJingFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i, static (engine, c) => engine.IsMovementObserverEdge(c) || engine.IsYanPuDyingExitEdge(c) || engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[c], engine._resolutionStack[c - 1]), static (engine, c, d) => engine.IsSuspendedOrPaidRescueObserverSuffix(c, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1460)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1570)]
    private bool HasWuJingDamageObserver(long id) => WuJingObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1590)]
    private bool HasWuJingBeforeDamageObserver(long id) => HasWuJingDamageObserver(id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1420)]
    private bool IsWuJingDying() => ActiveDying is { } d && WuJingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 2070)]
    private bool AllowsWuJingNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WuJingObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27130)] private bool AdvanceWuJingSubtree() => _pendingDecision is null && WuJingObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
