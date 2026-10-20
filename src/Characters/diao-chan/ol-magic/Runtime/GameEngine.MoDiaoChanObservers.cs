namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMoDc(ProgramSkillFrame f) => f.MoDiaoChan is { } r && r.Source == MoDcSource(f) && r.GameplayHash == f.GameplayHash &&
        MoDiaoChanComposition.Handles(r.Op) && f.InstructionIndex == 1 && f.TriggerId is not null && f.TriggerId == f.ActivationId &&
        f.WindowContext?.ParentFrameId == r.WindowId && Enum.IsDefined(r.Stage) && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        r.Cards.Distinct().Count() == r.Cards.Count && r.Targets.Distinct().Count() == r.Targets.Count &&
        r.Cards is System.Collections.IList { IsReadOnly: true } && r.Targets is System.Collections.IList { IsReadOnly: true } &&
        ProgramEventHistory<MoDiaoChanStartedEvent>().Count(e => e == new MoDiaoChanStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.WindowId, r.ActualTurn, r.Round)) == 1 &&
        (r.Op != SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse || ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Any(e => e.Claim.PromiseId == r.PromiseId && e.Claim.ActionId == r.ActionId && e.Claim.Source == r.Source && e.Claim.ParentFrameId == r.UseId)) &&
        ProgramEventHistory<MoDiaoChanCostEvent>().Where(e => e.FrameId == f.Id).All(e => _cardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.CardKind == e.Kind && m.From == e.From && m.To == e.To) == 1);
    private void SealMoDcMovement(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MoDiaoChan = f.MoDiaoChan! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence).ToArray())
        {
            var fact = new MoDiaoChanMovementEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<MoDiaoChanMovementEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool DrainMoDc(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31400)]
    private bool IsMoDcMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e is not null && MoDiaoChanComposition.Handles(e.Op) && ValidMoDc(f) &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MoDcLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MoDiaoChan is not { Stage: MoDiaoChanStage.CostChildren } || !ValidMoDc(f)) return false;
        var paid = ProgramEventHistory<MoDiaoChanCostEvent>().LastOrDefault(e => e.FrameId == f.Id && e.Kind == CardKind.SilverLion && e.From.Zone == CardZoneKind.Equipment);
        if (paid?.From.OwnerSeat is not { } owner || _cardMovements.SingleOrDefault(m => m.Sequence == paid.Sequence) is not { } movement) return false;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == movement.Reason &&
            recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == owner && recovery.Attempt.TargetSeat == owner;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == owner && hp.Change.TargetSeat == owner &&
            ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == owner && e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.Reason == movement.Reason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MoDcFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMoDc(f)) return false; var r = f.MoDiaoChan!;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MoDiaoChanStage.Drawing && f.NativeDraw is not null;
        if (MoDcLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m =>
            m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MoDiaoChanMovementEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MoDcObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame d && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, d) ||
        HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
            g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MoDcFirstChild(f, g._resolutionStack[n]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
            static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MoDcObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MoDcFirstChild(f, child), static (g, n) => g.MoDcObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31400)]
    private bool MoDcDamageObserver(long id) => MoDcObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31400)]
    private bool MoDcBeforeDamageObserver(long id) => MoDcObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31400)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31400)]
    private bool MoDcDying() => ActiveDying is { } d && MoDcObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 31400)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 31400)]
    private bool MoDcNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MoDcObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31400)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31400)]
    private bool MoDcRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMoDc(f)) return false;
        if (MoDcLionChild(f, recovery) || ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray())) return true;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && c > i + 1 && MoDcFirstChild(f, _resolutionStack[i + 1]) && MoDcObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31400)]
    private void AssertMoDc(ProgramSkillFrame f)
    { if (f.MoDiaoChan is not null && !ValidMoDc(f)) throw new InvalidOperationException("Magic Diao Chan changed her source or native payment/child cursor."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31400)]
    private bool KeepMoDcPaid(ProgramSkillFrame f) => f.MoDiaoChan is { Paid: true } && ValidMoDc(f) || f.WindowContext is { } c && IsIssuedMoDiaoChanCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0, c.OccurrenceIndex), c);
    [ProgramContinuation(ProgramContinuationKind.Resume, 31400)]
    private bool ResumeMoDc(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoDiaoChan: not null } f || f.Id != id) return false; ContinueMoDc(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31400)]
    private bool ReturnMoDcMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMoDc(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31400)]
    private bool AdvanceMoDcChildren() => _pendingDecision is null && MoDcObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
