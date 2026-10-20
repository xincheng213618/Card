namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidWuHf(ProgramSkillFrame f) => f.WuHuangFuSong is { } r && r.Source == WuHfSource(f) && r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 &&
        r.WindowId == f.WindowContext?.ParentFrameId && WuHuangFuSongComposition.Handles(r.Operation) && GetPausedPrivateOfferEffect(f)?.Op == r.Operation && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<WuHuangFuSongStartedEvent>().Count(e => e == new WuHuangFuSongStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.Turn, r.WindowId, r.UseId)) == 1 &&
        (!r.Paid || r.Operation == SkillProgramEffectOp.SettleWuHuangFuSongJiangxian || ProgramEventHistory<WuHuangFuSongMovedEvent>().Count(e => e.FrameId == f.Id && e.CardId == r.CardId && e.From == r.From && e.To == r.To &&
            _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == e.From && m.To == e.To)) == 1);
    private void SealWuHf(long id, long before)
    {
        var f = GetActiveProgramFrame(id); var r = f.WuHuangFuSong!;
        ReplaceRuntimeTop(f with { WuHuangFuSong = r with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence))
        {
            var fact = new WuHuangFuSongMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<WuHuangFuSongMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool DrainWuHf(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 381)]
    private bool IsWuHfMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) => effect is not null && WuHuangFuSongComposition.Handles(effect.Op) &&
        ValidWuHf(f) && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool WuHfFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidWuHf(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return f.WuHuangFuSong!.Stage == WuHuangFuSongStage.Drawing && f.NativeDraw is not null;
        if (child is RecoveryReplacementFrame replacement) return replacement.ParentFrameId == f.Id && ProgramRecoveryReplacementReceiptMatches(f, replacement, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp) && hp.Change.ParentFrameId == f.Id &&
            ProgramRecoveryReceiptMatches(f, hp, CompleteProgramEventHistory().ToArray());
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.Count > 0 &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.All(m => ProgramEventHistory<WuHuangFuSongMovementSignedEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool WuHfObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) ||
        g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n-1], g._resolutionStack[n]) ||
        g._resolutionStack[n-1] is ProgramSkillFrame f && g.WuHfFirstChild(f, g._resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n-1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n-1]),
        static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? WuHfObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.WuHfFirstChild(f, c), static (g, i) => g.WuHfObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1361)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1471)]
    private bool HasWuHfDamageObserver(long id) => WuHfObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1491)]
    private bool HasWuHfBeforeDamageObserver(long id) => WuHfObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1321)]
    private bool IsWuHfDying() => ActiveDying is { } d && WuHfObserverRoot() is { } r && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == r.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 101)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1891)]
    private bool AllowsWuHfNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WuHfObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return !sourceLess && e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 930)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 870)]
    private bool IsWuHfRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame r)
    {
        if (f.WuHuangFuSong is null || !ValidWuHf(f)) return false;
        if (r.ParentFrameId == f.Id) return ProgramRecoveryReplacementReceiptMatches(f, r, CompleteProgramEventHistory().ToArray());
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == r.Id);
        return i >= 0 && child > i + 1 && WuHfFirstChild(f, _resolutionStack[i + 1]) && WuHfObserverSuffix(i) && RecoveryReplacementFrameRidesOn(r, _resolutionStack[child - 1]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30000)]
    private void AssertWuHf(ProgramSkillFrame f) { if (f.WuHuangFuSong is not null && !ValidWuHf(f)) throw new InvalidOperationException("Wu Huangfu Song changed the owning native receipt or paid physical movement."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30000)]
    private bool ContinuePaidWuHf(ProgramSkillFrame f) => f.WuHuangFuSong is { Paid: true } && ValidWuHf(f) ||
        f.WuHuangFuSong is { Operation: SkillProgramEffectOp.SettleWuHuangFuSongJiangxian } && ValidWuHf(f) ||
        f.WuHuangFuSong is null && f.InstructionIndex is 0 or 1 && f.WindowContext is { } c && f.TriggerId == "lose-after-turn" &&
        IsIssuedWuHfEnding(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0, c.OccurrenceIndex), c);
    [ProgramContinuation(ProgramContinuationKind.Resume, 30000)]
    private bool ResumeWuHf(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuHuangFuSong: not null } f || f.Id != id) return false; ContinueWuHf(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30000)]
    private bool ReturnWuHfMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeWuHf(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30000)]
    private bool AdvanceWuHfChildren() => _pendingDecision is null && WuHfObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
