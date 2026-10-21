namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 37300)] private bool CaoXianMovement(ProgramSkillFrame f, SkillProgramEffect? op, ProgramMovementContinuation p) => f.CaoXian is { } r && CaoXianRules.Valid(f) && op?.Op == r.Operation && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool CaoXianFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!CaoXianRules.Valid(f) || f.CaoXian is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == CaoXianStage.Drawing && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && CaoXianMovement(f, GetPausedPrivateOfferEffect(f), p)) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        var lion = r.Cards.FirstOrDefault(c => c.Kind == CardKind.SilverLion && c.From.Zone == CardZoneKind.Equipment && _cardMovements.Any(m => m.CardId == c.Id && m.From == c.From && m.Sequence > r.Before && m.Sequence <= r.After));
        if (lion?.From.OwnerSeat is { } holder)
        {
            if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == holder && recovery.Attempt.TargetSeat == holder;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == holder;
        }
        if (r.Stage == CaoXianStage.EffectChildren && r.Option == 3 && ProgramEventHistory<CaoXianHpIssuedEvent>().Count(e => e.FrameId == f.Id && e.Target == r.Target) == 1) return DyingSuitsStructuralEdge(f, child);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool CaoXianEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && CaoXianFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]);
    private bool CaoXianSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) || HasObserverSuffix(n, static (g, i) => g.CaoXianEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? CaoXianRoot() => FindProgramObserverRoot(static (g, f, c) => g.CaoXianFirstChild(f, c), static (g, n) => g.CaoXianSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 37300)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 37300)] private bool CaoXianDying() => ActiveDying is { } d && CaoXianRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 37300)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 37300)] private bool CaoXianRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (CaoXianFirstChild(f, recovery)) return true; var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id); return n >= 0 && c > n + 1 && CaoXianFirstChild(f, _resolutionStack[n + 1]) && CaoXianSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 37300)] private bool CaoXianAdvanceChildren() => _pendingDecision is null && CaoXianRoot() is not null && TryAdvanceObserverSubtree(true);
}
