namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidWuZgl(ProgramSkillFrame f) => f.WuZhugeLiang is { } r && r.Source == WuZglSource(f) && r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 &&
        r.WindowId == (f.WindowContext?.ParentFrameId ?? 0) && WuZhugeLiangComposition.Handles(r.Operation) && GetPausedPrivateOfferEffect(f)?.Op == r.Operation &&
        r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && r.Targets.Distinct().Count() == r.Targets.Count && r.Targets.All(IsValidPlayerSeat) && r.Cursor >= 0 && r.Cursor <= r.Targets.Count &&
        ProgramEventHistory<WuZhugeLiangStartedEvent>().Count(e => e == new WuZhugeLiangStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ActualTurn, r.TurnOwner, r.WindowId, r.ActionId, r.UseId)) == 1;
    private void SealWuZgl(long id, long before)
    {
        var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { WuZhugeLiang = f.WuZhugeLiang! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence))
        {
            var fact = new WuZhugeLiangMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<WuZhugeLiangMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool DrainWuZgl(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30300)]
    private bool IsWuZglMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) => effect is not null && WuZhugeLiangComposition.Handles(effect.Op) &&
        ValidWuZgl(f) && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool WuZglFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidWuZgl(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return f.WuZhugeLiang!.Stage == WuZhugeLiangStage.Drawing && f.NativeDraw is not null;
        if (child is RecoveryReplacementFrame replacement) return replacement.ParentFrameId == f.Id && ProgramRecoveryReplacementReceiptMatches(f, replacement, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp)
            return ProgramHpReturnMatches(f, hp) && hp.Change.ParentFrameId == f.Id &&
                (ProgramRecoveryReceiptMatches(f, hp, CompleteProgramEventHistory().ToArray()) || f.WuZhugeLiang is { Operation: SkillProgramEffectOp.ResolveWuZhugeLiangJincui } r &&
                 hp.Change.Kind == HpChangeKind.Setting && hp.Change.Id == r.HpChangeId && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.SourceSeat == f.OwnerSeat &&
                 hp.Change.HpBefore == r.HpBefore && hp.Change.HpAfter == r.HpAfter && ProgramEventHistory<WuZhugeLiangHpSetEvent>().Count(e => e.FrameId == f.Id && e.ChangeId == hp.Change.Id && e.Before == r.HpBefore && e.After == r.HpAfter) == 1);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.Count > 0 &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.All(m => ProgramEventHistory<WuZhugeLiangMovementSignedEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool WuZglObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) ||
        g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n-1], g._resolutionStack[n]) ||
        g._resolutionStack[n-1] is ProgramSkillFrame f && g.WuZglFirstChild(f, g._resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n-1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n-1]),
        static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? WuZglObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.WuZglFirstChild(f, c), static (g, i) => g.WuZglObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30300)]
    private bool HasWuZglDamageObserver(long id) => WuZglObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30300)]
    private bool HasWuZglBeforeDamageObserver(long id) => WuZglObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30300)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30300)]
    private bool IsWuZglDying() => ActiveDying is { } d && WuZglObserverRoot() is { } r && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == r.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30300)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 30300)]
    private bool AllowsWuZglNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WuZglObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return !sourceLess && e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30300)]
    private bool IsWuZglRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame r)
    {
        if (f.WuZhugeLiang is null || !ValidWuZgl(f)) return false;
        if (r.ParentFrameId == f.Id) return ProgramRecoveryReplacementReceiptMatches(f, r, CompleteProgramEventHistory().ToArray());
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == r.Id);
        return i >= 0 && child > i + 1 && WuZglFirstChild(f, _resolutionStack[i + 1]) && WuZglObserverSuffix(i) && RecoveryReplacementFrameRidesOn(r, _resolutionStack[child - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30300)]
    private void AssertWuZgl(ProgramSkillFrame f) { if (f.WuZhugeLiang is not null && !ValidWuZgl(f)) throw new InvalidOperationException("Wu Zhuge Liang changed an owning native receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30300)]
    private bool ContinuePaidWuZgl(ProgramSkillFrame f) => f.WuZhugeLiang is { Paid: true } && ValidWuZgl(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 30300)]
    private bool ResumeWuZgl(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuZhugeLiang: not null } f || f.Id != id) return false; ContinueWuZgl(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30300)]
    private bool ReturnWuZglMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeWuZgl(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30300)]
    private bool AdvanceWuZglChildren() => _pendingDecision is null && WuZglObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool HasWuZglUseContinuation(long nativeId)
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.WuZhugeLiang?.Operation == SkillProgramEffectOp.ResolveWuZhugeLiangQingshi))
        {
            if (!ValidWuZgl(f) || f.WuZhugeLiang is not { } r ||
                _resolutionStack.SingleOrDefault(p => p.Id == r.WindowId) is not ProgramCardTriggerWindowFrame window || window.ParentFrameId != nativeId ||
                GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCommitted || window.Action.ActionId != r.ActionId || window.Action.ActorSeat != f.OwnerSeat ||
                !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, f.WindowContext!.OccurrenceIndex), f.WindowContext!, window)) continue;
            var i = _resolutionStack.FindIndex(p => p.Id == f.Id); var native = _resolutionStack.FindIndex(p => p.Id == nativeId);
            if (native < 0 || i != native + 2 || _resolutionStack[native + 1].Id != window.Id) continue;
            if (i == _resolutionStack.Count - 1)
            {
                if (r.Stage is WuZhugeLiangStage.Choice or WuZhugeLiangStage.DrawTargets && _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
                    p.PlayerSeat == f.OwnerSeat && AssistedChoicesEqual(p.Choices, WuZglChoices(f))) return true;
                if (r.Stage == WuZhugeLiangStage.DrawChildren && _pendingDecision is null) return true;
            }
            else if (WuZglFirstChild(f, _resolutionStack[i + 1]) && WuZglObserverSuffix(i)) return true;
        }
        return false;
    }
    private bool HasWuZglRescueContinuation(long dyingId)
    {
        if (_resolutionStack.OfType<CardUseFrame>().Where(use => use.DyingResponse?.ResolutionId == dyingId).ToArray() is not [var rescue] ||
            rescue.DyingResponse is not { } response || rescue.Action is not { Type: CardActionType.Use } action || rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) ||
            action.ActorSeat != response.ResponderSeat || rescue.SourceSeat != response.ResponderSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == dyingId) is not DyingFrame dying || !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) || !HasWuZglUseContinuation(rescue.Id)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == rescue.Id);
        return index > 0 && _resolutionStack[index - 1].Id == dyingId &&
            (rescue.CardKind == CardKind.Peach ? response.UsedPeach : response.UsedAlcohol && dying.VictimSeat == rescue.SourceSeat);
    }
}
