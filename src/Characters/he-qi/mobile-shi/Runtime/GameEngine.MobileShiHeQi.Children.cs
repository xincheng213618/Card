namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ShiHqFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ShiHeQi is not { } r || r.Turn != _turnNumber || r.TurnOwner != _currentSeat ||
            r.Source != ShiHqSource(f) || r.Hash != f.GameplayHash) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && ShiHqMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == ShiHqStage.Drawing;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
             ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool ShiHqChildEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && ShiHqFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiHqChildSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.ShiHqChildEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ShiHqObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.ShiHqFirstChild(f, child), static (g, n) => g.ShiHqChildSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49000)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49000)]
    private bool ShiHqDyingChild() => ActiveDying is { } d && ShiHqObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49000)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49000)]
    private bool ShiHqDamageChild(long id) => ShiHqObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49000)]
    private bool ShiHqBeforeDamageChild(long id) => ShiHqObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 49000)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 49000)]
    private bool ShiHqNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || ShiHqObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49000)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49000)]
    private bool ShiHqRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiHqFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && ShiHqFirstChild(f, _resolutionStack[n + 1]) && ShiHqChildSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49000)]
    private bool AdvanceShiHqChildren() => _pendingDecision is null && ShiHqObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
