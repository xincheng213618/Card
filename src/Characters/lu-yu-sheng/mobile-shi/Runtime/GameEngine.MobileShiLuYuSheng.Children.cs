namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 67400)]
    private bool ShiLysMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.MobileShiLuYuSheng is { Stage: 9 or 10 or 11 or 13 } r && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        r.Source?.OwnerSeat == f.OwnerSeat && r.GameplayHash == f.GameplayHash && effect?.Op is
            SkillProgramEffectOp.ResolveMobileShiRunweiGrant or SkillProgramEffectOp.ResolveMobileShiRunweiPhaseEnd or SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard;
    private bool ShiLysFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MobileShiLuYuSheng is not { } r || r.ActualTurn != _turnNumber || r.GameplayHash != f.GameplayHash || r.Source?.OwnerSeat != f.OwnerSeat || r.Source.SkillInstanceId != f.SkillInstanceId) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Operation == "shuanghuai" && r.Stage == 12 && r.SameAsLast;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && ShiLysMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Operation == "shuanghuai" && r.Stage == 11)
        {
            if (child is DyingFrame dying) return r.HpPaid && dying.ParentFrameId == f.Id && dying.VictimSeat == f.OwnerSeat && dying.Continuation == DyingContinuationKind.ProgramSkill &&
                ProgramEventHistory<MobileShiShuanghuaiHpPaidEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.After <= 0 && e.Before - e.After == 1) == 1;
            if (child is HpChangedTriggerWindowFrame hp) return r.HpPaid && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == f.OwnerSeat;
            if (child is IdentityAppearanceFrame appearance) return r.HpAppearance && appearance.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
                appearance.Receipt.ParentFrameId == f.Id && appearance.Receipt.OwnerSeat == f.OwnerSeat && appearance.Receipt.RequestedDeduction == 1;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
             ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool ShiLysChildEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i-1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i-1], _resolutionStack[i]) ||
        _resolutionStack[i-1] is ProgramSkillFrame f && ShiLysFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i-1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i-1]);
    private bool ShiLysChildSuffix(int n) => _resolutionStack[n+1] is DyingFrame d && n+2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n+1, d) ||
        HasObserverSuffix(n, static(g,i) => g.ShiLysChildEdge(i), static(g,i,d) => g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? ShiLysObserverRoot() => FindProgramObserverRoot(static(g,f,c) => g.ShiLysFirstChild(f,c), static(g,n) => g.ShiLysChildSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 67400)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 67400)]
    private bool ShiLysDyingChild() => ActiveDying is { } d && ShiLysObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 67400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 67400)]
    private bool ShiLysDamageChild(long id) => ShiLysObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 67400)]
    private bool ShiLysBeforeDamageChild(long id) => ShiLysObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 67400)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 67400)]
    private bool ShiLysNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || ShiLysObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 67400)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 67400)]
    private bool ShiLysRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiLysFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n+1 && ShiLysFirstChild(f, _resolutionStack[n+1]) && ShiLysChildSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 67400)]
    private bool AdvanceShiLysChildren() => _pendingDecision is null && ShiLysObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
