namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouZyMobile(ProgramSkillFrame f)
    {
        if (f.MouZhouYuMobile is not { } r || r.Source != MouZyMobileSource(f) || r.GameplayHash != f.GameplayHash || f.SkillId != MouZhouYuMobileComposition.Fanjian ||
            f.ActivationId != "cover-and-declare" || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 ||
            !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) || !IsValidPlayerSeat(r.TargetSeat) || r.TargetSeat == f.OwnerSeat || !Enum.IsDefined(r.Stage) ||
            r.Phase.ActorSeat != f.OwnerSeat || r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            r.Turn.TurnNumber != r.Phase.TurnNumber || r.Turn.OwnerSeat != r.Phase.ActualTurnOwnerSeat || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<MouZhouYuMobileStartedEvent>().Count(e => e == new MouZhouYuMobileStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.Phase, r.TargetSeat)) != 1) return false;
        var paid = ProgramEventHistory<MouZhouYuMobileCoveredEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.PaymentSequence == 0) return r.Stage == MouZhouYuMobileStage.OwnerChoice && r.CardId == 0 && paid.Length == 0 && r.Answer is null && !r.Revealed && r.GiftSequence == 0 && !r.LossPaid;
        if (paid is not [var payment] || payment != new MouZhouYuMobileCoveredEvent(f.Id, r.CardId, r.PrintedKind, r.ActualSuit, r.DeclaredSuit, CardLocation.Hand(f.OwnerSeat), CardLocation.Processing, r.PaymentSequence) ||
            r.DeclaredSuit is not (Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond) || !_cardMovements.Any(m => m.Sequence == r.PaymentSequence && m.CardId == r.CardId && m.CardKind == r.PrintedKind &&
                m.From == payment.From && m.To == payment.To && m.Reason.Value == "skill-program.mou-mobile:fanjian.cover")) return false;
        var answers = ProgramEventHistory<MouZhouYuMobileAnsweredEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Answer is null) return answers.Length == 0 && r.GiftSequence == 0 &&
            (r.Stage is MouZhouYuMobileStage.CoverChildren or MouZhouYuMobileStage.TargetChoice && !r.Revealed || r.Stage == MouZhouYuMobileStage.CleanupChildren && r.Revealed);
        if (r.Answer is not ("same" or "different" or "turn-over") || answers is not [var answer] || answer != new MouZhouYuMobileAnsweredEvent(f.Id, r.TargetSeat, r.Answer)) return false;
        if (r.GiftSequence > 0 && (!r.Revealed || ProgramEventHistory<MouZhouYuMobileGiftedEvent>().Count(e => e.FrameId == f.Id && e.CardId == r.CardId && e.TargetSeat == r.TargetSeat && e.Sequence == r.GiftSequence && e.From == CardLocation.Processing &&
                _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == r.CardId && m.From == e.From && m.To == e.To && m.Reason.Value == "skill-program.mou-mobile:fanjian.gift")) != 1)) return false;
        if (r.LossPaid && ((long)r.LossHpBefore - r.LossHpAfter != 1 || r.Stage != MouZhouYuMobileStage.LossChildren ||
            ProgramEventHistory<MouZhouYuMobileLossPaidEvent>().Count(e => e == new MouZhouYuMobileLossPaidEvent(f.Id, r.TargetSeat, r.LossHpBefore, r.LossHpAfter)) != 1 ||
            ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == r.TargetSeat && e.Amount == 1 && e.RemainingHp == r.LossHpAfter) != 1)) return false;
        return r.Stage is MouZhouYuMobileStage.ChoiceChildren or MouZhouYuMobileStage.GiftChildren or MouZhouYuMobileStage.LossChildren or MouZhouYuMobileStage.CleanupChildren;
    }
    private void SealMouZyMobile(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouZhouYuMobile = f.MouZhouYuMobile! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence))
        {
            var fact = new MouZhouYuMobileMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MouZhouYuMobileMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool DrainMouZyMobile(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31100)]
    private bool IsMouZyMobileMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op == SkillProgramEffectOp.ResolveMouZhouYuFanjian && ValidMouZyMobile(f) &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && f.MouZhouYuMobile!.Stage is not (MouZhouYuMobileStage.OwnerChoice or MouZhouYuMobileStage.TargetChoice);
    private bool MouZyMobileFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouZyMobile(f)) return false; var r = f.MouZhouYuMobile!;
        if (child is DyingFrame d) return d.ParentFrameId == f.Id && d.ResumesProgramSkill && d.VictimSeat == r.TargetSeat && r.Stage == MouZhouYuMobileStage.LossChildren && r.LossPaid;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp) && hp.Change.TargetSeat == r.TargetSeat && hp.Change.Kind == HpChangeKind.Loss && r.LossPaid &&
            hp.Change.Amount == 1 && hp.Change.HpBefore == r.LossHpBefore && hp.Change.HpAfter == r.LossHpAfter;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m =>
            ProgramEventHistory<MouZhouYuMobileMovementSignedEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouZyMobileObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame d && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, d) ||
        HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
            g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouZyMobileFirstChild(f, g._resolutionStack[n]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
            static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MouZyMobileObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouZyMobileFirstChild(f, child), static (g, i) => g.MouZyMobileObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31100)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31100)]
    private bool HasMouZyMobileDamageObserver(long id) => MouZyMobileObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31100)]
    private bool HasMouZyMobileBeforeDamageObserver(long id) => MouZyMobileObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31100)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31100)]
    private bool IsMouZyMobileDying() => ActiveDying is { } d && (_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == d.ParentFrameId && MouZyMobileFirstChild(f, d)) ||
        MouZyMobileObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 31100)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 31100)]
    private bool AllowsMouZyMobileNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouZyMobileObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31100)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31100)]
    private bool IsMouZyMobileRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMouZyMobile(f)) return false;
        if (recovery.ParentFrameId == f.Id) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && c > i + 1 && MouZyMobileFirstChild(f, _resolutionStack[i + 1]) && MouZyMobileObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31100)]
    private void AssertMouZyMobile(ProgramSkillFrame f)
    { if (f.MouZhouYuMobile is not null && !ValidMouZyMobile(f)) throw new InvalidOperationException("Mobile Fanjian changed its actual-turn source, real covered card or native paid return."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31100)]
    private bool KeepMouZyMobilePaid(ProgramSkillFrame f) => f.MouZhouYuMobile is { PaymentSequence: > 0 } && ValidMouZyMobile(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 31100)]
    private bool ResumeMouZyMobile(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhouYuMobile: not null } f || f.Id != id) return false; ContinueMouZyMobile(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31100)]
    private bool ReturnMouZyMobileMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouZyMobile(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31100)]
    private bool AdvanceMouZyMobileChildren() => _pendingDecision is null && MouZyMobileObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
