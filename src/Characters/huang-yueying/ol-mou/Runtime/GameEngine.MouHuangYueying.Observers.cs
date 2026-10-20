namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouHuangYueyingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouBingcai is { } r && IsPaidMouBingcai(f))
        {
            if (r.Stage == MouBingcaiStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
            if (child is RecoveryReplacementFrame recovery) return IsMouHuangYueyingRecoveryParent(f, recovery);
            if (child is HpChangedTriggerWindowFrame hp) return r.Stage == MouBingcaiStage.CostChildren &&
                hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1 &&
                r.PrintedKind == CardKind.SilverLion && r.From == CardLocation.Equipment(f.OwnerSeat);
            if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) &&
                moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                (moved.Batch.Movements.All(m => MouBingcaiPrimaryMovement(f, m)) || MouBingcaiOxSpill(f, moved.Batch));
        }
        if (f.MouLixian is { Paid: true } lx && ValidMouLixianStarted(f))
        {
            if (child is CardUseFrame use) return lx.Stage == MouLixianStage.NativeUse && use.Id == lx.UseId &&
                use.MouLixianOrigin?.ProgramFrameId == f.Id && IsIssuedMouLixianUse(use);
            if (child is CardsMovedTriggerWindowFrame settlement) return settlement.ResumeProgramFrameId == f.Id && f.PendingMovementContinuation is not null &&
                IsMouLixianNativeSettlementBatch(f, settlement.Batch);
        }
        if (f.MouBingcai is null && f.MouLixian is null) return false;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e =>
                e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return false;
    }
    private bool MouHuangYueyingLifecycleEdge(int index) => _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame window &&
        _resolutionStack[index] is ProgramSkillFrame f && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) && f.WindowContext is { } context &&
        context.ParentFrameId == window.Id && context.Window == window.Window;
    private ProgramSkillFrame? MouHuangYueyingObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.MouHuangYueyingFirstChild(f, child), static (g, index) => g.HasObserverSuffix(index,
            static (engine, next) => engine.IsMovementObserverEdge(next) || engine.IsSameNameHandObserverEdge(next) ||
                engine.TypedDamagePostEventEdge(engine._resolutionStack[next - 1], engine._resolutionStack[next]) ||
                engine.MouHuangYueyingLifecycleEdge(next) || engine.MouLixianCardWindowEdge(next) || engine._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun &&
                    engine.ZenrunFirstChild(zenrun, engine._resolutionStack[next]) ||
                NativeDrawAttemptRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) ||
                engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) || engine.IsYanPuDyingExitEdge(next),
            static (engine, next, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(next, dying)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1510)]
    private bool HasMouHuangYueyingDamageObserver(long id)
    {
        if (MouHuangYueyingObserverRoot() is not { } root || _resolutionStack.SingleOrDefault(f => f.Id == id) is not DamageTriggerWindowFrame window) return false;
        if (_resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id)) return true;
        return root.MouLixian is { Paid: true } r && ValidMouLixianStarted(root) && r.WindowId == window.Id &&
            root.WindowContext?.ParentFrameId == window.Id && window.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied && DamageProgramMatchesWindow(root, window);
    }
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1530)]
    private bool HasMouHuangYueyingBeforeDamageObserver(long id) => MouHuangYueyingObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1360)]
    private bool IsMouHuangYueyingDying()
    {
        if (ActiveDying is not { } dying) return false;
        if (MouHuangYueyingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id)) return true;
        return _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MouBingcai is not null && ValidMouBingcaiStarted(f) && IsMouBingcaiLegacyDyingParent(f)) ||
            IsMouBingcaiLegacyDyingWindow();
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1950)]
    private bool AllowsMouHuangYueyingNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            MouHuangYueyingObserverRoot() is not { } root || root.Id == f.Id || sourceLess) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28960)]
    private bool AdvanceMouHuangYueyingChildren()
    {
        if (_pendingDecision is not null) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame f && IsReturnedMouLixianUse(f))
        {
            ContinueMouLixian(f.Id);
            AdvanceRulesAndPublishState();
            return true;
        }
        return MouHuangYueyingObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28960)]
    private bool ResumeMouHuangYueying(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouBingcai is not null) { if (f.NativeDraw is null) ContinueMouBingcai(id); return true; }
        if (f.MouLixian is not null) { ContinueMouLixian(id); return true; }
        return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28960)]
    private bool ReturnMouHuangYueyingMovement(ProgramSkillFrame f)
    {
        if (f.MouBingcai is not null) { ContinueMouBingcai(f.Id); return true; }
        if (f.MouLixian is not null) { ContinueMouLixian(f.Id); return true; }
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28960)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28960)]
    private bool ContinuePaidMouHuangYueying(ProgramSkillFrame f) => f.MouBingcai is { Paid: true } && IsPaidMouBingcai(f) ||
        f.MouLixian is { Paid: true } r && ValidMouLixianStarted(f) &&
        (r.Stage == MouLixianStage.NativeUse && LifecycleCardUse(r.UseId) is { } use && IsIssuedMouLixianUse(use) ||
         r.Stage == MouLixianStage.NativeChildren && r.NativeReturned && ProgramEventHistory<MouLixianUseReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1);
}
