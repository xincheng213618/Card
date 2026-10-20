namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RequireWuLuKangDixian(ProgramSkillFrame f)
    {
        if (f.WuLuKangDixian is not { } r || !HasWuLuKangDixianOriginalPayment(f) || r.Source != WuLuKangDixianSource(f) ||
            r.GameplayHash != f.GameplayHash || r.Phase.ActorSeat != f.OwnerSeat || r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play ||
            !IsRecordedActualDiscardRecoveryPhase(r.Phase) || r.SequenceBefore < 0 || r.SequenceBefore > _movementSequence || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<WuLuKangDixianStartedEvent>().Count(e => e == new WuLuKangDixianStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.SequenceBefore)) != 1)
            throw new InvalidOperationException("Dixian lost its exact limited payment, source or original actual Play.");
        var chosen = ProgramEventHistory<WuLuKangDixianRankChosenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == WuLuKangDixianStage.Choosing)
        {
            if (r.Rank != 0 || chosen.Length != 0 || r.Materials.Count != 0 || r.DrawOutcome is not null || r.MaterialIndex != 0 || r.ActualGained != 0)
                throw new InvalidOperationException("An unaccepted Dixian rank cannot inspect or change its future stock predicate.");
            return;
        }
        if (r.Rank is < 1 or > 13 || chosen is not [var c] || c.Rank != r.Rank || c.AllAtLeast != r.AllAtLeast || c.StockCount != r.StockCount ||
            c.MaterialCount != r.Materials.Count || r.StockCount < 0 || r.MaterialIndex < 0 || r.MaterialIndex > r.Materials.Count ||
            r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count || r.Materials.Any(m => m.PrintedRank != 13 || GetAdvancedCard(m.CardId).Rank != 13 ||
                GetAdvancedCard(m.CardId).Kind != m.PrintedKind || m.From.Zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile or CardZoneKind.DiscardPile)) ||
            !ProgramEventHistory<WuLuKangDixianMaterialFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.Material).SequenceEqual(r.Materials))
            throw new InvalidOperationException("Dixian changed its frozen printed-rank decision or genuine field/stock K roster.");
        var moved = ProgramEventHistory<WuLuKangDixianEntityMovedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var skipped = ProgramEventHistory<WuLuKangDixianEntitySkippedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (moved.Length + skipped.Length != r.MaterialIndex || moved.Select(e => e.Index).Concat(skipped.Select(e => e.Index)).Order().Where((value, i) => value != i).Any() ||
            moved.Any(e => e.Index >= r.Materials.Count || r.Materials[e.Index].CardId != e.CardId || r.Materials[e.Index].From != e.From ||
                !_cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == e.From && m.To == e.To && m.Reason.Value == WuLuKangDixianObtainReason)) ||
            r.ActualGained != moved.Count(e => e.To == CardLocation.Hand(f.OwnerSeat)))
            throw new InvalidOperationException("Dixian's acquired entities must match their real, once-only native destinations.");
        if (r.AllAtLeast && (r.Materials.Count != 0 || r.Stage is not (WuLuKangDixianStage.Drawing or WuLuKangDixianStage.DrawChildren)) ||
            !r.AllAtLeast && (r.DrawOutcome is not null || r.Stage is not (WuLuKangDixianStage.Collecting or WuLuKangDixianStage.MovementChildren)))
            throw new InvalidOperationException("Dixian cannot switch the accepted success/failure branch.");
        if (r.DrawOutcome is { } o && (o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Rank || f.NativeDraw is not null ||
            ProgramEventHistory<WuLuKangDixianDrawReturnedEvent>().Count(e => e == new WuLuKangDixianDrawReturnedEvent(f.Id, r.Rank,
                o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId)) != 1) ||
            r.Stage == WuLuKangDixianStage.DrawChildren && r.DrawOutcome is null)
            throw new InvalidOperationException("Dixian must retain its exact requested native draw and actual replacement result.");
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29701)]
    private void AssertWuLuKangDixianFrame(ProgramSkillFrame f)
    { if (f.WuLuKangDixian is not null) RequireWuLuKangDixian(f); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29701)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29701)]
    private bool ContinueAcceptedWuLuKangDixian(ProgramSkillFrame f)
    { if (f.WuLuKangDixian is not { Rank: > 0 }) return false; RequireWuLuKangDixian(f); return true; }
    private bool IsWuLuKangDixianMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.ResolveWuLuKangDixian && f.WuLuKangDixian is { Stage: WuLuKangDixianStage.DrawChildren or WuLuKangDixianStage.MovementChildren or WuLuKangDixianStage.Collecting } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && HasWuLuKangDixianOriginalPayment(f);
    private bool WuLuKangDixianMovementMatches(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.WuLuKangDixian is not { } r || !_cardMovements.Contains(m)) return false;
        if (r.Stage == WuLuKangDixianStage.DrawChildren && r.DrawOutcome is { } o)
            return m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter &&
                (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == WuLuKangDixianDrawReason ||
                 o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) ||
                 m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
        if (r.Stage != WuLuKangDixianStage.MovementChildren) return false;
        if (ProgramEventHistory<WuLuKangDixianEntityMovedEvent>().Any(e => e.FrameId == f.Id && e.Sequence == m.Sequence && e.CardId == m.CardId && e.From == m.From && e.To == m.To)) return true;
        // An actual Wooden Ox departure may spill its real grain in this same
        // batch. It is a child movement, never another frozen K acquisition.
        return m.From.Zone == CardZoneKind.WoodenOxGrain && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
            ProgramEventHistory<WuLuKangDixianEntityMovedEvent>().Any(e => e.FrameId == f.Id && e.From == CardLocation.Equipment(m.From.OwnerSeat!.Value) &&
                GetAdvancedCard(e.CardId).Kind == CardKind.WoodenOx && e.Sequence < m.Sequence);
    }
    private bool WuLuKangDixianFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.WuLuKangDixian is not { Rank: > 0 } r || !HasWuLuKangDixianOriginalPayment(f)) return false;
        RequireWuLuKangDixian(f);
        if (r.Stage == WuLuKangDixianStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => WuLuKangDixianMovementMatches(f, m));
    }
    private bool WuLuKangDixianObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p && (g.WuLuKangDixianFirstChild(p, g._resolutionStack[n]) || p.Zenrun is not null && g.ZenrunFirstChild(p, g._resolutionStack[n])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? WuLuKangDixianObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.WuLuKangDixianFirstChild(f, child), static (g, i) => g.WuLuKangDixianObserverSuffix(i));
    private bool HasWuLuKangDixianDamageObserver(long id) => WuLuKangDixianObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasWuLuKangDixianBeforeDamageObserver(long id) => WuLuKangDixianObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsWuLuKangDixianDying() => ActiveDying is { } dying && WuLuKangDixianObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsWuLuKangDixianNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WuLuKangDixianObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsWuLuKangDixianRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.WuLuKangDixian is null || recovery.ParentFrameId == f.Id) return false;
        var p = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return p >= 0 && c > p + 1 && WuLuKangDixianFirstChild(f, _resolutionStack[p + 1]) && WuLuKangDixianObserverSuffix(p) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29701)]
    private bool AdvanceWuLuKangDixianChildren() => _pendingDecision is null && WuLuKangDixianObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
