namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ZixiEffectsFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ZixiEffects is not { } r || !ValidZixiEffectsStarted(f)) return false;
        AssertZixiEffects(f);
        if (r.Stage == ZixiEffectsStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is RecoveryReplacementFrame recovery) return IsZixiEffectsRecoveryParent(f, recovery);
        if (child is DamageFrame damage) return r.Stage == ZixiEffectsStage.DamageChildren && f.AttackAttempt is { } attack &&
            damage.ParentFrameId == f.Id && damage.SourceSeat == f.OwnerSeat && damage.TargetSeat == attack.TargetSeat && damage.Amount == attack.DamageAmount;
        if (child is BeforeDamageProgramWindowFrame before) return r.Stage == ZixiEffectsStage.DamageChildren && f.AttackAttempt is { } attempt &&
            (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == f.Id && before.SourceSeat == f.OwnerSeat && before.TargetSeat == attempt.TargetSeat;
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage == ZixiEffectsStage.ObtainChildren &&
            hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == r.TargetSeat && hp.Change.TargetSeat == r.TargetSeat && hp.Change.Amount == 1 &&
            r.Entities.Any(e => e.PrintedKind == CardKind.SilverLion && e.From == CardLocation.Equipment(r.TargetSeat));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e =>
                e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            (moved.Batch.Movements.All(m => ZixiEffectsPrimaryMovement(f, m)) || ZixiEffectsOxSpill(f, moved.Batch));
    }
    private bool ZixiEffectsLifecycleEdge(int index) => _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame window &&
        _resolutionStack[index] is ProgramSkillFrame f && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) && f.WindowContext is { } context &&
        context.ParentFrameId == window.Id && context.Window == window.Window;
    private ProgramSkillFrame? ZixiEffectsObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.ZixiEffectsFirstChild(f, child), static (g, index) => g.HasObserverSuffix(index,
            static (engine, next) => engine.IsMovementObserverEdge(next) || engine.IsSameNameHandObserverEdge(next) ||
                engine.TypedDamagePostEventEdge(engine._resolutionStack[next - 1], engine._resolutionStack[next]) ||
                engine.ZixiEffectsLifecycleEdge(next) || engine._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun &&
                    engine.ZenrunFirstChild(zenrun, engine._resolutionStack[next]) ||
                NativeDrawAttemptRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) ||
                engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) || engine.IsYanPuDyingExitEdge(next),
            static (engine, next, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(next, dying)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1170)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1280)]
    private bool HasZixiEffectsDamageObserver(long id) => ZixiEffectsObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1310)]
    private bool HasZixiEffectsBeforeDamageObserver(long id) => ZixiEffectsObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1140)]
    private bool IsZixiEffectsDying() => ActiveDying is { } dying && ZixiEffectsObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1710)]
    private bool AllowsZixiEffectsNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id) return false;
        if (f.ZixiEffects is { Stage: ZixiEffectsStage.DamageChildren } r && ValidZixiEffectsStarted(f))
            return target == r.TargetSeat && amount == 3 && source is null && nature is null && !sourceLess &&
                ProgramEventHistory<ZixiEffectsDamageIssuedEvent>().Count(e => e.FrameId == f.Id && e.SourceSeat == f.OwnerSeat &&
                    e.TargetSeat == target && e.Amount == 3) == 1;
        if (ZixiEffectsObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source &&
            effect.DamageNature == nature && !sourceLess && target == (effect.TargetReference is { } reference
                ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28720)]
    private bool AdvanceZixiEffectsChildren() => _pendingDecision is null && ZixiEffectsObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
