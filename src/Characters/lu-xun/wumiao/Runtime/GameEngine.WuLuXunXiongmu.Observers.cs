namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WuXiongmuPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.Xiongmu is not { } r || !_cardMovements.Contains(m)) return false;
        if (r.Stage == XiongmuStage.InsertChildren)
            return r.PutMovements.Contains(m) || r.WoodenOxSpill.Contains(m);
        if (r.Stage == XiongmuStage.ObtainChildren) return r.ObtainMovements.Contains(m);
        return r.Stage == XiongmuStage.DrawChildren && r.DrawOutcome is { } draw &&
            m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter &&
            (draw.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == XiongmuDrawReason ||
             m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    private bool WuXiongmuSilverLion(ProgramSkillFrame f) => f.Xiongmu is { Stage: XiongmuStage.InsertChildren } r &&
        r.PutMovements.Any(m => m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(f.OwnerSeat) &&
            m.Reason.Value == XiongmuPutReason && _cardMovements.Contains(m));
    private bool WuXiongmuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Xiongmu is not { } r || !ValidWuXiongmuStarted(f)) return false;
        if (r.Stage == XiongmuStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        AssertWuXiongmu(f);
        if (child is RecoveryReplacementFrame recovery) return IsWuXiongmuRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
            return r.Stage == XiongmuStage.InsertChildren && WuXiongmuSilverLion(f) && hp.Change.ParentFrameId == f.Id &&
                hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id &&
                    e.Change.Window == state.Window && e.Change.TargetSeat == state.OwnerSeat);
        return child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is not null &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => WuXiongmuPaidMovement(f, m));
    }
    private bool WuXiongmuLifecycleEdge(int index) =>
        _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame window && _resolutionStack[index] is ProgramSkillFrame f &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) &&
        f.WindowContext is { } context && context.ParentFrameId == window.Id && context.Window == window.Window;
    private bool WuXiongmuObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g.WuXiongmuLifecycleEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame { Zenrun: { Paid: true } } replacement && g.ZenrunFirstChild(replacement, g._resolutionStack[n]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? WuXiongmuObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.WuXiongmuFirstChild(f, child), static (g, i) => g.WuXiongmuObserverSuffix(i));
    private bool HasWuXiongmuDamageObserver(long id) => WuXiongmuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasWuXiongmuBeforeDamageObserver(long id) => WuXiongmuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsWuXiongmuDying() => ActiveDying is { } dying && WuXiongmuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsWuXiongmuNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            WuXiongmuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount &&
            effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    private bool IsWuXiongmuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.Xiongmu is null) return false;
        if (WuXiongmuSilverLion(f) && RecoveryReplacementFrameRidesOn(recovery, f) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat &&
            recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason?.Value == XiongmuPutReason) return true;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && WuXiongmuFirstChild(f, _resolutionStack[parent + 1]) &&
            WuXiongmuObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29000)]
    private bool AdvanceWuXiongmuChildren() => _pendingDecision is null && WuXiongmuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
