namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ShiHjFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MobileShiHuanJie is not { } r || r.ActualTurn != _turnNumber || r.ActualTurnOwner != _currentSeat ||
            r.Source != ShiHjSource(f) || r.GameplayHash != f.GameplayHash) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && ShiHjMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MobileShiHuanJieStage.Drawing;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
             ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool ShiHjChildEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && ShiHjFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiHjChildSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.ShiHjChildEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ShiHjObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.ShiHjFirstChild(f, child), static (g, n) => g.ShiHjChildSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 48300)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 48300)]
    private bool ShiHjDyingChild() => ActiveDying is { } d && ShiHjObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 48300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 48300)]
    private bool ShiHjDamageChild(long id) => ShiHjObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 48300)]
    private bool ShiHjBeforeDamageChild(long id) => ShiHjObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 48300)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 48300)]
    private bool ShiHjNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || ShiHjObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 48300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 48300)]
    private bool ShiHjRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiHjFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && ShiHjFirstChild(f, _resolutionStack[n + 1]) && ShiHjChildSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 48300)]
    private bool AdvanceShiHjChildren() => _pendingDecision is null && ShiHjObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
