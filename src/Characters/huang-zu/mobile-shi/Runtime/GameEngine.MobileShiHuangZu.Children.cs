namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ShiHzFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ShiHuangZu is not { } r || r.ActualTurn != _turnNumber || r.GameplayHash != f.GameplayHash ||
            f.WindowContext?.ParentFrameId != r.WindowId || r.Source.OwnerSeat != f.OwnerSeat || f.SkillId != ShiHzDuanyang) return false;
        if (child is CardUseFrame use) return ShiHzUseRidesOn(use, f);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && ShiHzMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (r.Stage == ShiHzStage.ReturnedUse || m.Sequence > r.Before && m.Sequence <= r.After));
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage is ShiHzStage.RecastDrawing or ShiHzStage.RewardDrawing;
        if (r.Stage == ShiHzStage.RecastCostChildren && r.Selected.Any(m => m.PrintedKind == CardKind.SilverLion && m.From.Zone == CardZoneKind.Equipment &&
                ProgramEventHistory<ShiHzRecastPaidEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.From == m.From)))
        {
            if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) &&
                recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                recovery.Attempt.SourceSeat == r.Target && recovery.Attempt.TargetSeat == r.Target;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == r.Target;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
             ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool ShiHzChildEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i-1], _resolutionStack[i]) ||
        _resolutionStack[i-1] is ProgramSkillFrame f && ShiHzFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i-1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i-1]);
    private bool ShiHzChildSuffix(int n) => _resolutionStack[n+1] is DyingFrame d && n+2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n+1, d) ||
        HasObserverSuffix(n, static(g,i) => g.ShiHzChildEdge(i), static(g,i,d) => g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? ShiHzObserverRoot() => FindProgramObserverRoot(static(g,f,c) => g.ShiHzFirstChild(f,c), static(g,n) => g.ShiHzChildSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 47900)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 47900)]
    private bool ShiHzDyingChild() => ActiveDying is { } d && ShiHzObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 47900)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 47900)]
    private bool ShiHzDamageChild(long id) => ShiHzObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 47900)]
    private bool ShiHzBeforeDamageChild(long id) => ShiHzObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 47900)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 47900)]
    private bool ShiHzNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || ShiHzObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 47900)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 47900)]
    private bool ShiHzRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiHzFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n+1 && ShiHzFirstChild(f, _resolutionStack[n+1]) && ShiHzChildSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 47900)]
    private bool AdvanceShiHzChildren() => _pendingDecision is null && ShiHzObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
