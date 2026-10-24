namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ShiXxyFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ShiXinXianYing is not { } r || r.ActualTurn != _turnNumber || r.GameplayHash != f.GameplayHash || r.Source != ShiXxySource(f)) return false;
        if (child is SkillInvocationFrame invocation) return r.Stage == ShiXinXianYingStage.QingshiInvocation && r.Jiejie && r.QingIssued &&
            invocation.ParentFrameId == f.Id && invocation.ActorSeat == f.OwnerSeat && invocation.SkillKey == ShiXxyQingshi && invocation.SkillInstanceId == r.QingshiInstance &&
            invocation.OccurrenceKey == "shi-jiejie-qingshi:" + f.Id && SkillInvocationStructuralEdge(f, invocation);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == ShiXinXianYingStage.Drawing;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && ShiXxyMovementProof(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage == ShiXinXianYingStage.QingDiscardChildren && r.DiscardPaid && r.Selected.Any(m => GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion && m.From.Zone == CardZoneKind.Equipment))
        {
            if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) &&
                recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                r.Selected.Any(m => m.From.OwnerSeat == recovery.Attempt.TargetSeat && m.From.Zone == CardZoneKind.Equipment && GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion);
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery &&
                r.Selected.Any(m => m.From.OwnerSeat == hp.Change.TargetSeat && m.From.Zone == CardZoneKind.Equipment && GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion);
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
             ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool ShiXxyChildEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i-1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i-1], _resolutionStack[i]) ||
        _resolutionStack[i-1] is ProgramSkillFrame f && ShiXxyFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i-1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i-1]);
    private bool ShiXxyChildSuffix(int n) => _resolutionStack[n+1] is DyingFrame d && n+2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n+1, d) ||
        HasObserverSuffix(n, static(g,i) => g.ShiXxyChildEdge(i), static(g,i,d) => g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? ShiXxyObserverRoot() => FindProgramObserverRoot(static(g,f,c) => g.ShiXxyFirstChild(f,c), static(g,n) => g.ShiXxyChildSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 48200)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 48200)]
    private bool ShiXxyDyingChild() => ActiveDying is { } d && ShiXxyObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 48200)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 48200)]
    private bool ShiXxyDamageChild(long id) => ShiXxyObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 48200)]
    private bool ShiXxyBeforeDamageChild(long id) => ShiXxyObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 48200)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 48200)]
    private bool ShiXxyNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || ShiXxyObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 48200)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 48200)]
    private bool ShiXxyRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiXxyFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n+1 && ShiXxyFirstChild(f, _resolutionStack[n+1]) && ShiXxyChildSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 48200)]
    private bool AdvanceShiXxyChildren() => _pendingDecision is null && ShiXxyObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
