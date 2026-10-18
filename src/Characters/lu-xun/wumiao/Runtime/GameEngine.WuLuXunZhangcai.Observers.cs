namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WuLuXunZhangcaiRootMatches(ProgramSkillFrame f)
    {
        if (f.WuLuXunZhangcai is not { } r || f.SkillId != "wumiao:zhangcai" || f.ActivationId != f.TriggerId ||
            f.TriggerId != r.Qualification.Source.BindingId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Qualification.Source != WuLuXunActionSource(f) || r.Qualification.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveWuLuXunZhangcai ||
            f.WindowContext is not { } context || context.ParentFrameId != r.WindowId || context.Window != r.Qualification.Window ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame window ||
            window.ParentFrameId != r.NativeParentId || !WuLuXunZhangcaiWindowMatches(window, r.Qualification) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId,
                f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        var q = r.Qualification;
        return index > 0 && _resolutionStack[index - 1].Id == r.WindowId && q.RemainingEights >= 0 &&
            q.DrawCount == Math.Max(1, q.RemainingEights) && q.MovementSequence >= 0 && q.MovementSequence <= _movementSequence &&
            (q.EffectiveRank == 8 || q.RuxianFrameId is { } active &&
                ProgramEventHistory<WuLuXunRuxianActivatedEvent>().Any(e => e.FrameId == active && e.Source.OwnerSeat == f.OwnerSeat && e.ActualTurn <= q.ActualTurn) &&
                !ProgramEventHistory<TurnStartedEvent>().Any(e => e.ActorSeat == f.OwnerSeat && e.TurnNumber <= q.ActualTurn &&
                    e.TurnNumber > ProgramEventHistory<WuLuXunRuxianActivatedEvent>().Single(a => a.FrameId == active).ActualTurn)) &&
            ProgramEventHistory<WuLuXunZhangcaiQualifiedEvent>().Count(e => e.Qualification == q) == 1 &&
            ProgramEventHistory<WuLuXunZhangcaiStartedEvent>().Count(e => e == WuLuXunZhangcaiStart(f.Id, r)) == 1 &&
            ProgramEventHistory<WuLuXunZhangcaiStartedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat &&
                e.Source.SkillInstanceId == f.SkillInstanceId && e.ActionId == q.ActionId) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29002)]
    private void AssertWuLuXunZhangcai(ProgramSkillFrame f)
    {
        if (f.WuLuXunZhangcai is not { } r) return;
        if (!WuLuXunZhangcaiRootMatches(f) || !Enum.IsDefined(r.Stage))
            throw new InvalidOperationException("Zhangcai lost its exact native accepted action, post-payment rank-eight sample or issued all-ranks entitlement.");
        var returned = ProgramEventHistory<WuLuXunZhangcaiDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == WuLuXunZhangcaiStage.Drawing)
        {
            if (r.DrawOutcome is not null || returned.Length != 0 || f.PendingMovementContinuation is not null)
                throw new InvalidOperationException("Zhangcai cannot reuse a returned native draw as a fresh request.");
            return;
        }
        if (r.DrawOutcome is not { } o || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Qualification.DrawCount ||
            o.SequenceBefore < r.Qualification.MovementSequence || o.SequenceAfter < o.SequenceBefore || o.SequenceAfter > _movementSequence ||
            f.NativeDraw is not null || f.PendingMovementContinuation is not { } p ||
            !IsWuLuXunZhangcaiMovement(f, GetPausedPrivateOfferEffect(f), p) || returned is not [var fact] ||
            fact != new WuLuXunZhangcaiDrawReturnedEvent(f.Id, o.RequestedCount, o.ActualDrawIds.Count,
                o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId))
            throw new InvalidOperationException("Zhangcai lost its once-returned actual/replacement native draw invoice or unfinished children.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29002)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29002)]
    private bool PermitPaidWuLuXunZhangcai(ProgramSkillFrame f)
    { if (f.WuLuXunZhangcai is null) return false; AssertWuLuXunZhangcai(f); return true; }
    private bool IsWuLuXunZhangcaiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.WuLuXunZhangcai is { Stage: WuLuXunZhangcaiStage.DrawChildren, DrawOutcome: not null } &&
        effect?.Op == SkillProgramEffectOp.ResolveWuLuXunZhangcai && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool WuLuXunZhangcaiDrawMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.WuLuXunZhangcai is not { Stage: WuLuXunZhangcaiStage.DrawChildren, DrawOutcome: { } o } ||
            !_cardMovements.Contains(m) || m.Sequence <= o.SequenceBefore || m.Sequence > o.SequenceAfter) return false;
        return o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == WuLuXunZhangcaiDrawReason ||
            o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) ||
            m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
    }
    private bool WuLuXunZhangcaiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.WuLuXunZhangcai is not { } r || !WuLuXunZhangcaiRootMatches(f)) return false;
        if (r.Stage == WuLuXunZhangcaiStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => WuLuXunZhangcaiDrawMovement(f, m));
    }
    private bool WuLuXunZhangcaiObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p &&
            (g.WuLuXunZhangcaiFirstChild(p, g._resolutionStack[n]) || p.Zenrun is not null && g.ZenrunFirstChild(p, g._resolutionStack[n])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? WuLuXunZhangcaiObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.WuLuXunZhangcaiFirstChild(f, child), static (g, index) => g.WuLuXunZhangcaiObserverSuffix(index));
    private bool HasWuLuXunZhangcaiDamageObserver(long id) => WuLuXunZhangcaiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasWuLuXunZhangcaiBeforeDamageObserver(long id) => WuLuXunZhangcaiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsWuLuXunZhangcaiDying() => ActiveDying is { } d && WuLuXunZhangcaiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsWuLuXunZhangcaiNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            WuLuXunZhangcaiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsWuLuXunZhangcaiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.WuLuXunZhangcai is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && WuLuXunZhangcaiFirstChild(f, _resolutionStack[parent + 1]) &&
            WuLuXunZhangcaiObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29002)]
    private bool AdvanceWuLuXunZhangcaiChildren() => _pendingDecision is null && WuLuXunZhangcaiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
