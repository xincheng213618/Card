namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 37500)] private bool MouHuLieMovement(ProgramSkillFrame f, SkillProgramEffect? op, ProgramMovementContinuation p) => f.MouHuLie is { } r && MouHuLieRules.Valid(f) && op?.Op == r.Operation && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouHuLieFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouHuLieRules.Valid(f) || f.MouHuLie is not { } r) return false;
        if (child is CardUseFrame use) return IsIssuedMouHuLieUse(use);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouHuLieStage.Drawing && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouHuLieMovement(f, GetPausedPrivateOfferEffect(f), p)) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        var lion = r.Cards.FirstOrDefault(c => c.Kind == CardKind.SilverLion && c.From.Zone == CardZoneKind.Equipment && _cardMovements.Any(m => m.CardId == c.Id && m.From == c.From && m.Sequence > r.Before && m.Sequence <= r.After));
        if (lion?.From.OwnerSeat is { } holder)
        {
            if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == holder && recovery.Attempt.TargetSeat == holder;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == holder;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouHuLieEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && MouHuLieFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]);
    private bool MouHuLieSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) || HasObserverSuffix(n, static (g, i) => g.MouHuLieEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouHuLieRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouHuLieFirstChild(f, c), static (g, n) => g.MouHuLieSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 37500)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 37500)] private bool MouHuLieDying() => ActiveDying is { } d && MouHuLieRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 37500)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 37500)] private bool MouHuLieRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouHuLieFirstChild(f, recovery)) return true; var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id); return n >= 0 && c > n + 1 && MouHuLieFirstChild(f, _resolutionStack[n + 1]) && MouHuLieSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 37500)] private bool MouHuLieAdvanceChildren() => _pendingDecision is null && MouHuLieRoot() is not null && TryAdvanceObserverSubtree(true);
}
