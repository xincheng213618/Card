namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidSpSunShangxiangLiangzhu(ProgramSkillFrame f)
    {
        if (f.SpSunShangxiangLiangzhu is not { } r || r.Source != SpSunShangxiangSource(f) || r.GameplayHash != f.GameplayHash || f.SkillId != SpSunShangxiangOlComposition.Liangzhu ||
            f.TriggerId != "actual-own-play-recovery" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { } w || w.ParentFrameId != r.ChangeId ||
            w.TargetSeat != r.RecoveredSeat || !Enum.IsDefined(r.Stage) || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            ProgramEventHistory<SpSunShangxiangLiangzhuStartedEvent>().Count(e => e == new SpSunShangxiangLiangzhuStartedEvent(f.Id, r.Source, r.GameplayHash, r.ChangeId, r.RecoveredSeat)) != 1 ||
            !CanRunSpSunShangxiangLiangzhu(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), w)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1] is not HpChangedTriggerWindowFrame parent || parent.Id != r.ChangeId || parent.Change != w.HpChange ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        if (r.Stage == SpSunShangxiangLiangzhuStage.Choosing) return r.Branch is null && r.Requested == 0 && r.Draw is null;
        if (r.Branch is not ("self" or "help") || r.Requested != (r.Branch == "help" ? 2 : 1) || r.RecipientSeat != (r.Branch == "help" ? r.RecoveredSeat : f.OwnerSeat) ||
            ProgramEventHistory<SpSunShangxiangLiangzhuChoiceEvent>().Count(e => e == new SpSunShangxiangLiangzhuChoiceEvent(f.Id, r.Branch, r.RecipientSeat, r.Requested)) != 1) return false;
        if (r.Stage == SpSunShangxiangLiangzhuStage.Drawing) return r.Draw is null;
        return r.Draw is { } draw && draw.RecipientSeat == r.RecipientSeat && draw.RequestedCount == r.Requested &&
            ProgramEventHistory<SpSunShangxiangLiangzhuDrawEvent>().Count(e => e == new SpSunShangxiangLiangzhuDrawEvent(f.Id, draw.RecipientSeat, draw.RequestedCount,
                draw.ActualDrawIds.Count, draw.ReplacementGainedIds.Count, draw.AttemptFrameId)) == 1;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 32300)]
    private bool IsSpSunShangxiangLiangzhuMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op == SkillProgramEffectOp.ResolveSpSunShangxiangLiangzhu &&
        f.SpSunShangxiangLiangzhu is { Stage: SpSunShangxiangLiangzhuStage.Children, Draw: not null } && ValidSpSunShangxiangLiangzhu(f) && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool SpSunShangxiangLiangzhuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidSpSunShangxiangLiangzhu(f) || f.SpSunShangxiangLiangzhu is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == SpSunShangxiangLiangzhuStage.Drawing && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved && r.Draw is { } draw && f.PendingMovementContinuation is { } p && IsSpSunShangxiangLiangzhuMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool SpSunShangxiangLiangzhuObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && SpSunShangxiangLiangzhuFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool SpSunShangxiangLiangzhuSuffix(int n) => _resolutionStack[n + 1] is DyingFrame dying && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, dying) ||
        HasObserverSuffix(n, static (g, i) => g.SpSunShangxiangLiangzhuObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? SpSunShangxiangLiangzhuObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.SpSunShangxiangLiangzhuFirstChild(f, c), static (g, n) => g.SpSunShangxiangLiangzhuSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 32300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 32300)]
    private bool HasSpSunShangxiangLiangzhuDamage(long id) => SpSunShangxiangLiangzhuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 32300)]
    private bool HasSpSunShangxiangLiangzhuBeforeDamage(long id) => SpSunShangxiangLiangzhuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 32300)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 32300)]
    private bool IsSpSunShangxiangLiangzhuDying() => ActiveDying is { } dying && SpSunShangxiangLiangzhuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 32300)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 32300)]
    private bool AllowsSpSunShangxiangLiangzhuDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || SpSunShangxiangLiangzhuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 32300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 32300)]
    private bool IsSpSunShangxiangLiangzhuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && SpSunShangxiangLiangzhuFirstChild(f, _resolutionStack[n + 1]) && SpSunShangxiangLiangzhuSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 32300)]
    private bool ResumeSpSunShangxiangLiangzhu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SpSunShangxiangLiangzhu: not null } f || f.Id != id) return false; ContinueSpSunShangxiangLiangzhu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 32300)]
    private bool ReturnSpSunShangxiangLiangzhuMovement(ProgramSkillFrame f) => f.SpSunShangxiangLiangzhu is not null && f.PendingMovementContinuation is not null && ResumeSpSunShangxiangLiangzhu(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 32300)]
    private bool AdvanceSpSunShangxiangLiangzhuChildren() => _pendingDecision is null && SpSunShangxiangLiangzhuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32300)]
    private bool ContinuePaidSpSunShangxiangLiangzhu(ProgramSkillFrame f) => f.SpSunShangxiangLiangzhu is { Branch: not null } && ValidSpSunShangxiangLiangzhu(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32300)]
    private void AssertSpSunShangxiangLiangzhu(ProgramSkillFrame f)
    { if (f.SpSunShangxiangLiangzhu is not null && !ValidSpSunShangxiangLiangzhu(f)) throw new InvalidOperationException("Liangzhu lost its actual recovered actor, source or native draw return."); }
}
