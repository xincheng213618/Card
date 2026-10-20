namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouLz(ProgramSkillFrame f)
    {
        if (f.MouLuZhiOl is not { } r || f.InstructionIndex != 1 || f.ActivationId != f.TriggerId || r.Source != MouLzSource(f) || r.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Selected.Count != r.Selected.Distinct().Count() ||
            f.WindowContext is not { } w || w.ParentFrameId != r.WindowId || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            ProgramEventHistory<MouLuZhiOlStartedEvent>().Count(e => e == new MouLuZhiOlStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.OriginalId, r.OriginalActionId, r.ActorSeat, r.TargetSeat, r.Turn, r.BatchId)) != 1) return false;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); if (n < 1 || _resolutionStack[n - 1].Id != r.WindowId) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMouLuZhiLiance)
            return f.SkillId == MouLuZhiOlComposition.Liance && r.BatchId > 0 && w.MovementBatch?.Id == r.BatchId && _resolutionStack[n - 1] is CardsMovedTriggerWindowFrame moved && moved.CandidateIndex >= 0 && moved.CandidateIndex < moved.Candidates.Count && MountObserverCandidateMatches(f, moved.Candidates[moved.CandidateIndex]) && r.Stage is MouLuZhiOlStage.Drawing or MouLuZhiOlStage.DrawChildren;
        if (f.SkillId != MouLuZhiOlComposition.Sibing) return false;
        if (_resolutionStack[n - 1] is MouLuZhiOlDirectWindowFrame dw)
        {
            if (dw.Context != w || !ExactMouLzDirectWindow(dw) || !MountObserverCandidateMatches(f, dw.Candidate) || _resolutionStack.SingleOrDefault(x => x.Id == dw.ParentFrameId) is not ProgramSkillFrame { MouLuZhiOlDirectDuel: { } d } || d.FrameId != r.OriginalId || d.Instruction != r.OriginalInstruction || r.OriginalActionId != 0 || r.ActorSeat != d.Actor || r.TargetSeat != d.Target) return false;
        }
        else if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame window && MouLzContextUse(w) is { Action: { } a } use)
        { if (window.ParentFrameId != use.Id || r.OriginalId != use.Id || r.OriginalActionId != a.ActionId || window.Action.ActionId != a.ActionId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex]))) return false; }
        else return false;
        return r.Op == SkillProgramEffectOp.ResolveMouLuZhiSibingPress ? f.TriggerId == "red-press" && r.Stage is MouLuZhiOlStage.Red or MouLuZhiOlStage.RedChildren or MouLuZhiOlStage.TargetRed or MouLuZhiOlStage.TargetRedChildren :
            r.Op == SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation && f.TriggerId == "undamaged-retaliation" && r.Stage is MouLuZhiOlStage.Black or MouLuZhiOlStage.BlackChildren or MouLuZhiOlStage.SlashTarget or MouLuZhiOlStage.Issuing or MouLuZhiOlStage.NativeUse or MouLuZhiOlStage.NativeReturned;
    }
    private bool DrainMouLz(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 32900)]
    private bool MouLzMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => ValidMouLz(f) && p == f.PendingMovementContinuation && e?.Op == f.MouLuZhiOl!.Op && p.BeforeCount == 0 && p.CoverageResultBind is null && p.SubjectSeat == (f.MouLuZhiOl.Stage == MouLuZhiOlStage.TargetRedChildren ? f.MouLuZhiOl.TargetSeat : f.OwnerSeat) && f.MouLuZhiOl.Stage is MouLuZhiOlStage.RedChildren or MouLuZhiOlStage.TargetRedChildren or MouLuZhiOlStage.BlackChildren or MouLuZhiOlStage.DrawChildren;
    private bool MouLzLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouLz(f) || f.MouLuZhiOl is not { Stage: MouLuZhiOlStage.RedChildren or MouLuZhiOlStage.TargetRedChildren or MouLuZhiOlStage.BlackChildren } r) return false;
        var seat = r.Stage == MouLuZhiOlStage.TargetRedChildren ? r.TargetSeat : f.OwnerSeat;
        if (!_cardMovements.Any(m => r.Selected.Contains(m.CardId) && m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(seat) && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == MouLzDiscard)) return false;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == MouLzDiscard && recovery.Attempt.SourceSeat == seat && recovery.Attempt.TargetSeat == seat && recovery.Attempt.Amount == 1;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == seat && hp.Change.TargetSeat == seat && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == seat && e.Reason.Value == MouLzDiscard && e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouLzFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouLz(f)) return false; var r = f.MouLuZhiOl!;
        if (r.Stage == MouLuZhiOlStage.NativeUse && child is CardUseFrame u) return IsMouLzCounterUse(u);
        if (r.Stage == MouLuZhiOlStage.Drawing && NativeDrawAttemptRidesOn(child, f)) return f.NativeDraw is not null;
        if (MouLzLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame m) return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, m) && m.Batch.Movements.All(x => _cardMovements.Contains(x) && x.Sequence > r.Before && x.Sequence <= r.After);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouLzNativeEdge(int n)
    {
        if (_resolutionStack[n - 1] is CardUseFrame { MouLuZhiOlCounter: not null } u && IsMouLzCounterUse(u) && _resolutionStack[n] is ProgramCardTriggerWindowFrame w) return w.ParentFrameId == u.Id && w.Action.ActionId == u.Action!.ActionId;
        if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame window && LifecycleCardUse(window.ParentFrameId) is { MouLuZhiOlCounter: not null } use && IsMouLzCounterUse(use) && _resolutionStack[n] is ProgramSkillFrame f && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count) return MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) && f.WindowContext == CreateCardActionProgramContext(window, window.Candidates[window.CandidateIndex]);
        if (_resolutionStack[n - 1] is ProgramSkillFrame { MouLuZhiOlDirectDuel: { } d } p && _resolutionStack[n] is MouLuZhiOlDirectWindowFrame direct) return direct.ParentFrameId == p.Id && ValidMouLzDirect(p, d) && ExactMouLzDirectWindow(direct);
        return _resolutionStack[n - 1] is MouLuZhiOlDirectWindowFrame dw && _resolutionStack[n] is ProgramSkillFrame skill && ExactMouLzDirectWindow(dw) && MountObserverCandidateMatches(skill, dw.Candidate) && skill.WindowContext == dw.Context;
    }
    private bool MouLzObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || MouLzNativeEdge(n) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouLzFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouLzSuffix(int n) => HasObserverSuffix(n, static (g, i) => g.MouLzObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouLzRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouLzFirstChild(f, c), static (g, n) => g.MouLzSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 32900)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 32900)] private bool MouLzDamage(long id) => MouLzRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 32900)] private bool MouLzBeforeDamage(long id) => MouLzRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 32900)] [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 32900)] private bool MouLzDying() => ActiveDying is { } d && MouLzRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 32900)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 32900)] private bool MouLzRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child)
    {
        if (!ValidMouLz(f)) return false; if (MouLzLionChild(f, child) || ProgramRecoveryReplacementReceiptMatches(f, child, CompleteProgramEventHistory().ToArray())) return true;
        var a = _resolutionStack.FindIndex(x => x.Id == f.Id); var b = _resolutionStack.FindIndex(x => x.Id == child.Id); return a >= 0 && b > a + 1 && MouLzFirstChild(f, _resolutionStack[a + 1]) && MouLzSuffix(a) && RecoveryReplacementFrameRidesOn(child, _resolutionStack[b - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32900)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32900)] private bool KeepMouLzPaid(ProgramSkillFrame f) => f.MouLuZhiOl is { Paid: true } && ValidMouLz(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 32900)] private bool ResumeMouLz(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuZhiOl: not null } f || f.Id != id) return false; ContinueMouLz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 32900)] private bool ReturnMouLzMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouLz(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 32900)] private bool AdvanceMouLzChildren() => _pendingDecision is null && MouLzRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32900)] private void AssertMouLz(ProgramSkillFrame f) { if (f.MouLuZhiOl is not null && !ValidMouLz(f)) throw new InvalidOperationException("Lu Zhi lost its original source, current native parent or paid child receipt."); }
}
