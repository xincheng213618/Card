namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPaidLeZhouFeiXianshuSelection(ProgramSkillFrame frame)
    {
        if (frame.LeZhouFeiXianshu is not { GiftSequence: > 0 } receipt ||
            frame.SkillId != "le:xianshu" || frame.ActivationId != "marked-hand-gift" ||
            frame.TriggerId is not null || frame.WindowContext is not null ||
            receipt.Source != LeZhouFeiXianshuSource(frame) || receipt.GameplayHash != frame.GameplayHash ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != frame.GameplayHash ||
            frame.InstructionIndex != receipt.InstructionIndex || frame.InstructionIndex != 1 ||
            !frame.SelectedCardIds.SequenceEqual([receipt.CardId]) ||
            !frame.SelectedTargetSeats.SequenceEqual([receipt.TargetSeat]) ||
            receipt.GiftSequence <= receipt.SequenceBefore || receipt.GiftSequence > _movementSequence ||
            ProgramEventHistory<XianshuGiftStartedEvent>().Count(e => e.FrameId == frame.Id) != 1 ||
            ProgramEventHistory<XianshuGiftPaidEvent>().Count(e => e.FrameId == frame.Id) != 1 ||
            ProgramEventHistory<XianshuGiftStartedEvent>().Count(e => e.FrameId == frame.Id &&
                e.Source == receipt.Source && e.GameplayHash == receipt.GameplayHash && e.Phase == receipt.Phase &&
                e.ActualTurn == receipt.ActualTurn && e.TurnOwner == receipt.TurnOwner &&
                e.CardId == receipt.CardId && e.PrintedKind == receipt.PrintedKind && e.GiftSuit == receipt.GiftSuit &&
                e.TargetSeat == receipt.TargetSeat && e.SequenceBefore == receipt.SequenceBefore) != 1 ||
            ProgramEventHistory<XianshuGiftPaidEvent>().Count(e => e.FrameId == frame.Id &&
                e.BatchId == receipt.GiftBatchId && e.CardId == receipt.CardId &&
                e.From == CardLocation.Hand(frame.OwnerSeat) && e.To == CardLocation.Hand(receipt.TargetSeat) &&
                e.MovementSequence == receipt.GiftSequence) != 1)
            return false;
        return _cardMovements.Count(m => m.Sequence == receipt.GiftSequence && m.CardId == receipt.CardId &&
            m.CardKind == receipt.PrintedKind && m.From == CardLocation.Hand(frame.OwnerSeat) &&
            m.To == CardLocation.Hand(receipt.TargetSeat) && m.Reason.Value == LeZhouFeiXianshuGiftReason) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28320)]
    private void AssertLeZhouFeiXianshu(ProgramSkillFrame frame)
    {
        if (frame.LeZhouFeiXianshu is not { } receipt) return;
        if (receipt.Source != LeZhouFeiXianshuSource(frame) || receipt.GameplayHash != frame.GameplayHash ||
            frame.SkillId != "le:xianshu" || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.ActivationId != "marked-hand-gift" || frame.InstructionIndex != receipt.InstructionIndex ||
            frame.InstructionIndex != 1 || GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth ||
            !IsRecordedActualDiscardRecoveryPhase(receipt.Phase) || receipt.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play ||
            receipt.Phase.ActorSeat != frame.OwnerSeat || receipt.Phase.TurnNumber != receipt.ActualTurn ||
            receipt.Phase.ActualTurnOwnerSeat != receipt.TurnOwner || !IsValidPlayerSeat(receipt.TargetSeat) ||
            receipt.TargetSeat == frame.OwnerSeat || !Enum.IsDefined(receipt.Stage) || !Enum.IsDefined(receipt.GiftSuit) ||
            receipt.Before < receipt.SequenceBefore || receipt.After < receipt.Before || receipt.After > _movementSequence ||
            !frame.SelectedCardIds.SequenceEqual([receipt.CardId]) || !frame.SelectedTargetSeats.SequenceEqual([receipt.TargetSeat]) ||
            receipt.GiftSequence > 0 && !HasPaidLeZhouFeiXianshuSelection(frame) ||
            receipt.GiftSequence == 0 && (receipt.Stage != LeZhouFeiXianshuStage.GiftChildren ||
                receipt.GiftBatchId != 0 || ProgramEventHistory<XianshuGiftPaidEvent>().Any(e => e.FrameId == frame.Id)))
            throw new InvalidOperationException("Xianshu lost its exact source, selected original Hand entity, native own Play or once-paid gift invoice.");
        if (receipt.RequestedDraw >= 0)
        {
            var facts = ProgramEventHistory<XianshuDrawRequestedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
            if (!HasPaidLeZhouFeiXianshuSelection(frame) || receipt.RequestedDraw > 5 || facts is not [var draw] ||
                draw.RequestedCount != receipt.RequestedDraw || draw.RequestedCount !=
                    (draw.OwnerHasHp && draw.TargetHasHp ? (int)Math.Min(5L, Math.Abs((long)draw.OwnerHp - draw.TargetHp)) : 0))
                throw new InvalidOperationException("Xianshu must sample one real capped HP difference after its gift children.");
        }
        else if (receipt.Stage != LeZhouFeiXianshuStage.GiftChildren || receipt.DrawOutcome is not null ||
            ProgramEventHistory<XianshuDrawRequestedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Xianshu cannot advance before its one native draw request is issued.");
        if (receipt.DrawOutcome is { } outcome)
        {
            if (receipt.Stage is LeZhouFeiXianshuStage.GiftChildren or LeZhouFeiXianshuStage.Drawing ||
                outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != receipt.RequestedDraw ||
                outcome.ActualDrawIds.Count > outcome.RequestedCount ||
                outcome.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } ||
                outcome.ReplacementGainedIds is not System.Collections.IList { IsReadOnly: true } ||
                outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
                outcome.ActualDrawIds.Intersect(outcome.ReplacementGainedIds).Any() ||
                outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
                outcome.ActualDrawIds.Any(id => _cardMovements.Count(m => m.CardId == id &&
                    m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat) &&
                    m.Reason.Value == LeZhouFeiXianshuDrawReason) != 1) ||
                _cardMovements.Count(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat) &&
                    m.Reason.Value == LeZhouFeiXianshuDrawReason) != outcome.ActualDrawIds.Count ||
                outcome.SequenceBefore < receipt.GiftSequence || outcome.SequenceAfter < outcome.SequenceBefore ||
                outcome.SequenceAfter > _movementSequence ||
                ProgramEventHistory<XianshuDrawReturnedEvent>().Count(e => e.FrameId == frame.Id) != 1 ||
                ProgramEventHistory<XianshuDrawReturnedEvent>().Count(e => e.FrameId == frame.Id &&
                    e.RecipientSeat == outcome.RecipientSeat && e.RequestedCount == outcome.RequestedCount &&
                    e.ActualCount == outcome.ActualDrawIds.Count && e.ReplacementCount == outcome.ReplacementGainedIds.Count &&
                    e.Before == outcome.SequenceBefore && e.After == outcome.SequenceAfter && e.AttemptFrameId == outcome.AttemptFrameId) != 1)
                throw new InvalidOperationException("Xianshu's typed draw return changed its original request or actual DrawPile/replacement materials.");
        }
        else if (receipt.Stage is not (LeZhouFeiXianshuStage.GiftChildren or LeZhouFeiXianshuStage.Drawing))
            throw new InvalidOperationException("Xianshu cannot compare HP before its own draw returns.");
        if (receipt.HealthAction is { } action)
        {
            var facts = ProgramEventHistory<XianshuHealthEvaluatedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
            if (receipt.DrawOutcome is null || receipt.Stage is not (LeZhouFeiXianshuStage.HealthIssuing or LeZhouFeiXianshuStage.HealthChildren) ||
                facts is not [var health] || health.Action != action || health.GiftSuit != receipt.GiftSuit ||
                health.Action != (!health.TargetAlive || !health.OwnerHasHp || !health.TargetHasHp ? XianshuHealthAction.None :
                    IsRedSuit(health.GiftSuit) && health.TargetHp <= health.OwnerHp ? XianshuHealthAction.Recover :
                    (health.GiftSuit is Suit.Spade or Suit.Club) && health.TargetHp >= health.OwnerHp ? XianshuHealthAction.LoseHp : XianshuHealthAction.None))
                throw new InvalidOperationException("Xianshu lost its once-only colour and freshly evaluated post-draw HP comparison.");
            if (action == XianshuHealthAction.LoseHp &&
                    (receipt.Stage == LeZhouFeiXianshuStage.HealthChildren) != receipt.HpLossCaptured ||
                action != XianshuHealthAction.LoseHp && (receipt.HpLossCaptured || receipt.AppearancePrevented))
                throw new InvalidOperationException("Xianshu cannot resume after an unperformed HP-loss instruction.");
            if (receipt.HpLossCaptured && (action != XianshuHealthAction.LoseHp ||
                receipt.Stage != LeZhouFeiXianshuStage.HealthChildren ||
                ProgramEventHistory<XianshuHpLossAppliedEvent>().Count(e => e.FrameId == frame.Id) != 1 ||
                ProgramEventHistory<XianshuHpLossAppliedEvent>().Count(e => e.FrameId == frame.Id &&
                    e.TargetSeat == receipt.TargetSeat && e.PreventedByAppearance == receipt.AppearancePrevented &&
                    e.Amount == (receipt.AppearancePrevented ? 0 : 1)) != 1))
                throw new InvalidOperationException("Xianshu repeated or lost its native HP deduction/identity-prevention return.");
            if (receipt.HpLossCaptured && !receipt.AppearancePrevented &&
                ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == frame.Id &&
                    e.SkillId == frame.SkillId && e.TargetSeat == receipt.TargetSeat && e.Amount == 1) != 1)
                throw new InvalidOperationException("Xianshu lost its original native HP-loss result.");
        }
        else if (receipt.Stage is LeZhouFeiXianshuStage.HealthIssuing or LeZhouFeiXianshuStage.HealthChildren)
            throw new InvalidOperationException("Xianshu has no issued colour-health action for this stage.");
        if (frame.PendingMovementContinuation is { } pending &&
            !IsLeZhouFeiXianshuMovement(frame, GetPausedPrivateOfferEffect(frame), pending))
            throw new InvalidOperationException("Xianshu's native child lost its exact typed parent return.");
    }

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 210)]
    private bool IsLeZhouFeiXianshuMovement(ProgramSkillFrame frame, SkillProgramEffect? effect,
        ProgramMovementContinuation pending) => frame.LeZhouFeiXianshu is { } receipt &&
        effect?.Op == SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth &&
        pending == frame.PendingMovementContinuation && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        pending.SubjectSeat == (receipt.Stage is LeZhouFeiXianshuStage.GiftChildren or LeZhouFeiXianshuStage.DrawChildren
            ? frame.OwnerSeat : receipt.TargetSeat) && receipt.Stage != LeZhouFeiXianshuStage.Drawing;

    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 370)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 360)]
    private bool IsLeZhouFeiXianshuRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame recovery) =>
        frame.LeZhouFeiXianshu is { Stage: LeZhouFeiXianshuStage.HealthChildren, HealthAction: XianshuHealthAction.Recover } receipt &&
        HasPaidLeZhouFeiXianshuSelection(frame) && RecoveryReplacementFrameRidesOn(recovery, frame) &&
        recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
        recovery.Attempt.SourceSeat == frame.OwnerSeat && recovery.Attempt.TargetSeat == receipt.TargetSeat &&
        recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program &&
        recovery.Attempt.Completion.InstructionIndex == frame.InstructionIndex;

    private bool LeZhouFeiXianshuPaidMovement(ProgramSkillFrame frame, CardMovementRecord movement)
    {
        var receipt = frame.LeZhouFeiXianshu!;
        if (!_cardMovements.Contains(movement)) return false;
        if (receipt.Stage == LeZhouFeiXianshuStage.GiftChildren)
            return movement.Sequence == receipt.GiftSequence && movement.CardId == receipt.CardId &&
                movement.From == CardLocation.Hand(frame.OwnerSeat) && movement.To == CardLocation.Hand(receipt.TargetSeat) &&
                movement.Reason.Value == LeZhouFeiXianshuGiftReason;
        return receipt.Stage == LeZhouFeiXianshuStage.DrawChildren && receipt.DrawOutcome is { } draw &&
            movement.Sequence > draw.SequenceBefore && movement.Sequence <= draw.SequenceAfter &&
            (draw.ActualDrawIds.Contains(movement.CardId) && movement.From == CardLocation.DrawPile &&
                movement.To == CardLocation.Hand(frame.OwnerSeat) && movement.Reason.Value == LeZhouFeiXianshuDrawReason ||
             draw.ReplacementGainedIds.Contains(movement.CardId) && movement.To == CardLocation.Hand(frame.OwnerSeat) ||
             movement.From == CardLocation.DiscardPile && movement.To == CardLocation.DrawPile && movement.Reason == CardMoveReasons.Reshuffle);
    }

    private bool LeZhouFeiXianshuFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.LeZhouFeiXianshu is not { } receipt || !HasPaidLeZhouFeiXianshuSelection(frame)) return false;
        AssertLeZhouFeiXianshu(frame);
        if (receipt.Stage == LeZhouFeiXianshuStage.Drawing && frame.NativeDraw is not null)
            return NativeDrawAttemptRidesOn(child, frame);
        if (child is IdentityAppearanceFrame appearance)
            return receipt.HealthAction == XianshuHealthAction.LoseHp &&
                appearance.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && appearance.Receipt.ParentFrameId == frame.Id &&
                appearance.Receipt.OwnerSeat == receipt.TargetSeat && appearance.Receipt.RequestedDeduction == 1 &&
                appearance.Receipt.OriginalInstructionIndex == frame.InstructionIndex;
        if (child is DyingFrame dying)
            return receipt.Stage == LeZhouFeiXianshuStage.HealthChildren && receipt.HealthAction == XianshuHealthAction.LoseHp &&
                receipt.HpLossCaptured && dying.ParentFrameId == frame.Id && dying.VictimSeat == receipt.TargetSeat &&
                dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.ProgramSkill;
        if (child is RecoveryReplacementFrame recovery) return IsLeZhouFeiXianshuRecoveryParent(frame, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == frame.Id && hp.ResumeFrameId == frame.Id &&
                receipt.Stage == LeZhouFeiXianshuStage.HealthChildren && hp.Change.TargetSeat == receipt.TargetSeat &&
                (receipt.HealthAction == XianshuHealthAction.LoseHp && hp.Continuation == PostEventContinuation.Program &&
                    hp.Change.Kind == HpChangeKind.Loss && hp.Change.SourceSeat is null && hp.Change.Amount == 1 ||
                 receipt.HealthAction == XianshuHealthAction.Recover && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                    hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == frame.OwnerSeat && hp.Change.Amount == 1);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == frame.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == frame.Id && e.Change.Window == state.Window);
        return child is CardsMovedTriggerWindowFrame moved && frame.PendingMovementContinuation is not null &&
            ProgramMovementReturnMatches(frame, moved) && moved.Batch.ParentFrameId == frame.Id &&
            moved.Batch.OriginOwnerSeat == frame.OwnerSeat && moved.Batch.OriginSkillId == frame.SkillId &&
            moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(movement => LeZhouFeiXianshuPaidMovement(frame, movement));
    }

    private bool LeZhouFeiXianshuLifecycleEdge(int index) =>
        _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame window &&
        _resolutionStack[index] is ProgramSkillFrame frame && window.CandidateIndex >= 0 &&
        window.CandidateIndex < window.Candidates.Count && MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]) &&
        frame.WindowContext is { } context && context.ParentFrameId == window.Id && context.Window == window.Window;

    private ProgramSkillFrame? LeZhouFeiXianshuObserverRoot() => FindProgramObserverRoot(
        static (engine, frame, child) => engine.LeZhouFeiXianshuFirstChild(frame, child),
        static (engine, index) => engine.HasObserverSuffix(index,
            static (game, next) => game.IsMovementObserverEdge(next) ||
                game.TypedDamagePostEventEdge(game._resolutionStack[next - 1], game._resolutionStack[next]) ||
                game.LeZhouFeiXianshuLifecycleEdge(next) ||
                game._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun &&
                    game.ZenrunFirstChild(zenrun, game._resolutionStack[next]) ||
                NativeDrawAttemptRidesOn(game._resolutionStack[next], game._resolutionStack[next - 1]) ||
                game.RecoveryReplacementFrameRidesOn(game._resolutionStack[next], game._resolutionStack[next - 1]) ||
                game.IsYanPuDyingExitEdge(next),
            static (game, next, dying) => game.IsSuspendedOrPaidRescueObserverSuffix(next, dying)));

    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1140)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1250)]
    private bool HasLeZhouFeiXianshuDamageObserver(long id) => LeZhouFeiXianshuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1280)]
    private bool HasLeZhouFeiXianshuBeforeDamageObserver(long id) => LeZhouFeiXianshuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1110)]
    private bool IsLeZhouFeiXianshuDying() => ActiveDying is { } dying && LeZhouFeiXianshuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1680)]
    private bool AllowsLeZhouFeiXianshuNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            LeZhouFeiXianshuObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source &&
            effect.DamageNature == nature && !sourceLess && target == (effect.TargetReference is { } reference
                ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28320)]
    private bool AdvanceLeZhouFeiXianshuChildren() => _pendingDecision is null &&
        LeZhouFeiXianshuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
