namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouZuifengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouZuifeng is not { Paid: true } r || !IsPaidMouZuifeng(f)) return false;
        if (child is CardUseFrame use) return r.Stage == MouZuifengStage.NativeUse && use.Id == r.UseId &&
            use.MouZuifengAlcohol?.ProgramFrameId == f.Id && IsIssuedMouZuifengAlcoholUse(use);
        if (child is DamageFrame damage) return r.Stage == MouZuifengStage.DamageChildren && r.FireIssued && !r.FireReturned &&
            damage.ParentFrameId == f.Id && f.AttackAttempt is { } attack && damage.SourceSeat == attack.SourceSeat &&
            damage.TargetSeat == attack.TargetSeat && damage.Amount == attack.DamageAmount && damage.Nature == attack.Nature;
        if (child is BeforeDamageProgramWindowFrame before) return r.Stage == MouZuifengStage.DamageChildren && r.FireIssued && !r.FireReturned &&
            (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == f.Id && f.AttackAttempt is not null;
        if (child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null &&
                (r.Stage == MouZuifengStage.CostChildren && ProgramMovementReturnMatches(f,moved) && MouZuifengCostBatch(f,moved.Batch) ||
                 r.Stage == MouZuifengStage.NativeChildren && moved.ResumeProgramFrameId == f.Id && IsMouZuifengNativeSettlementBatch(f,moved.Batch));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        return false;
    }
    private bool MouZuifengCardWindowEdge(int index)
    {
        if (_resolutionStack[index-1] is CardUseFrame use && IsIssuedMouZuifengAlcoholUse(use) &&
            _resolutionStack[index] is ProgramCardTriggerWindowFrame window)
            return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId &&
                window.Action.ActorSeat == use.SourceSeat && window.Action.EffectiveKind == CardKind.Alcohol;
        if (_resolutionStack[index-1] is not ProgramCardTriggerWindowFrame parent ||
            LifecycleCardUse(parent.ParentFrameId) is not { } native || !IsIssuedMouZuifengAlcoholUse(native) ||
            parent.Action.ActionId != native.Action!.ActionId || _resolutionStack[index] is not ProgramSkillFrame child ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count) return false;
        var candidate = parent.Candidates[parent.CandidateIndex];
        return MountObserverCandidateMatches(child,ToSharedCandidate(candidate)) && child.WindowContext == CreateCardActionProgramContext(parent,candidate);
    }
    private bool MouZuifengLifecycleEdge(int index) => _resolutionStack[index-1] is ProgramLifecycleTriggerWindowFrame window &&
        _resolutionStack[index] is ProgramSkillFrame f && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex]) && f.WindowContext is { } context &&
        context.ParentFrameId == window.Id && context.Window == window.Window;
    private bool MouZuifengObserverSuffix(int index) => HasObserverSuffix(index,static (g,n) =>
        g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n-1],g._resolutionStack[n]) ||
        g.MouZuifengCardWindowEdge(n) || g.MouZuifengLifecycleEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g._resolutionStack[n-1] is ProgramSkillFrame { Zenrun: { Paid: true } } replacement && g.ZenrunFirstChild(replacement,g._resolutionStack[n]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),
        static (g,n,dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n,dying));
    private ProgramSkillFrame? MouZuifengObserverRoot() => FindProgramObserverRoot(
        static (g,f,child) => g.MouZuifengFirstChild(f,child),static (g,index) => g.MouZuifengObserverSuffix(index));
    private bool HasMouZuifengDamageObserver(long id) => MouZuifengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMouZuifengBeforeDamageObserver(long id) => MouZuifengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouZuifengDying() => ActiveDying is { } dying && MouZuifengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouZuifengNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess) return false;
        if (f.MouZuifeng is { Stage: MouZuifengStage.DamageChildren, FireIssued: true, FireReturned: false } r)
            return IsPaidMouZuifeng(f) && target == r.TargetSeat && amount == 1 && source is null && nature == DamageNature.Fire &&
                ProgramEventHistory<MouZuifengFireIssuedEvent>().Count(e => e == new MouZuifengFireIssuedEvent(f.Id,f.OwnerSeat,r.TargetSeat,r.ShownId!.Value)) == 1;
        if (MouZuifengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,effect.Target));
    }
    private bool IsMouZuifengRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if (f.MouZuifeng is null || recovery.ParentFrameId == f.Id) return false;
        var rootIndex = _resolutionStack.FindIndex(x => x.Id == f.Id); var childIndex = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return rootIndex >= 0 && childIndex > rootIndex+1 && MouZuifengFirstChild(f,_resolutionStack[rootIndex+1]) &&
            MouZuifengObserverSuffix(rootIndex) && RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[childIndex-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,29020)]
    private bool AdvanceMouZuifengChildren()
    {
        if (_pendingDecision is not null) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { MouZuifeng: { Stage: MouZuifengStage.NativeChildren, UseReturned: true } } f &&
            IsPaidMouZuifeng(f) && LifecycleCardUse(f.MouZuifeng.UseId) is null &&
            ProgramEventHistory<MouZuifengAlcoholReturnedEvent>().Count(e => e == new MouZuifengAlcoholReturnedEvent(f.Id,f.MouZuifeng.UseId,f.MouZuifeng.ActionId)) == 1)
        { ContinueMouZuifeng(f.Id); AdvanceRulesAndPublishState(); return true; }
        return MouZuifengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    }
}
