namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 35500)]
    private bool MouCaoShuangAnniversaryMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.MouCaoShuangAnniversary is { } r &&
        effect?.Op == r.Operation && MouCaoShuangAnniversary.Valid(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouCaoShuangAnniversaryFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouCaoShuangAnniversary.Valid(f) || f.MouCaoShuangAnniversary is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouCaoShuangAnniversaryStage.Drawing && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouCaoShuangAnniversaryMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage == MouCaoShuangAnniversaryStage.MoveChildren && r.Option != 3 && r.Cards.Any(c => c.From.Zone == CardZoneKind.Equipment && c.Kind == CardKind.SilverLion))
        {
            var lion = _cardMovements.Any(m => r.Cards.Any(c => c.Id == m.CardId && c.From == m.From) && m.CardKind == CardKind.SilverLion && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == MouCaoShuangAnniversaryRuntime.DiscardReason);
            if (lion && child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.SourceSeat == r.Payer && rr.Attempt.TargetSeat == r.Payer;
            if (lion && child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == r.Payer;
        }
        if (r.Stage == MouCaoShuangAnniversaryStage.HpChildren && ProgramEventHistory<MouCaoShuangAnniversaryHpIssuedEvent>().Count(e => e.FrameId == f.Id && e.Cursor == r.Cursor - 1 && e.Target == r.Payer) == 1)
            return DyingSuitsStructuralEdge(f, child);
        if (r.Stage == MouCaoShuangAnniversaryStage.RecoveryChildren)
        {
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && rr.Attempt.SourceSeat == f.OwnerSeat && rr.Attempt.TargetSeat == f.OwnerSeat;
            if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Change.TargetSeat == f.OwnerSeat;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouCaoShuangAnniversaryEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && MouCaoShuangAnniversaryFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]);
    private bool MouCaoShuangAnniversarySuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.MouCaoShuangAnniversaryEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouCaoShuangAnniversaryRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouCaoShuangAnniversaryFirstChild(f, c), static (g, n) => g.MouCaoShuangAnniversarySuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 35500)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 35500)]
    private bool MouCaoShuangAnniversaryDying() => ActiveDying is { } d && MouCaoShuangAnniversaryRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 35500)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 35500)]
    private bool MouCaoShuangAnniversaryRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouCaoShuangAnniversaryFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouCaoShuangAnniversaryFirstChild(f, _resolutionStack[n + 1]) && MouCaoShuangAnniversarySuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 35500)]
    private bool MouCaoShuangAnniversaryAdvanceChildren() => _pendingDecision is null && MouCaoShuangAnniversaryRoot() is not null && TryAdvanceObserverSubtree(true);
}
