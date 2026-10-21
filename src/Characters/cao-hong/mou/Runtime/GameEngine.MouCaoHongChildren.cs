namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 35900)]
    private bool MouCaoHongMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.MouCaoHong is { } r && effect?.Op == r.Operation && MouCaoHong.Valid(f) &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouCaoHongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouCaoHong.Valid(f) || f.MouCaoHong is not { } r) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouCaoHongMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        var lion = r.Cards.FirstOrDefault(c => c.From.Zone == CardZoneKind.Equipment && c.Kind == CardKind.SilverLion && _cardMovements.Any(m => m.CardId == c.Id && m.From == c.From && m.Sequence > r.Before && m.Sequence <= r.After));
        if (lion?.From.OwnerSeat is { } holder)
        {
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.SourceSeat == holder && rr.Attempt.TargetSeat == holder;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == holder;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouCaoHongEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MouCaoHongFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool MouCaoHongSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.MouCaoHongEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouCaoHongRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouCaoHongFirstChild(f, c), static (g, n) => g.MouCaoHongSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 35900)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 35900)]
    private bool MouCaoHongDamageWindow(long id) => MouCaoHongRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 35900)]
    private bool MouCaoHongBeforeDamage(long id) => MouCaoHongRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 35900)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 35900)]
    private bool MouCaoHongDying() => ActiveDying is { } d && MouCaoHongRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 35900)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 35900)]
    private bool MouCaoHongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouCaoHongRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 35900)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 35900)]
    private bool MouCaoHongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouCaoHongFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouCaoHongFirstChild(f, _resolutionStack[n + 1]) && MouCaoHongSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 35900)] private bool MouCaoHongAdvanceChildren() => _pendingDecision is null && MouCaoHongRoot() is not null && TryAdvanceObserverSubtree(true);
}
