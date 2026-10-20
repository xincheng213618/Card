namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void SealWuLuKangMovements(long id, long before, bool privateReserve = false)
    {
        var f = GetActiveProgramFrame(id); var after = _movementSequence;
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = f.WuLuKangShenduan is { } s ? s with { After = after } : null,
            WuLuKangKegou = f.WuLuKangKegou is { } k ? k with { After = after } : null });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after && m.Reason.Value != WuLuKangReserveReason))
        {
            var fact = new WuLuKangMovementSignedEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence);
            if (!ProgramEventHistory<WuLuKangMovementSignedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool WuLuKangSignedMovement(ProgramSkillFrame f, CardMovementRecord m) => _cardMovements.Contains(m) &&
        ProgramEventHistory<WuLuKangMovementSignedEvent>().Count(e => e == new WuLuKangMovementSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)) == 1;
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 380)]
    private bool IsWuLuKangMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        (e?.Op == SkillProgramEffectOp.ResolveWuLuKangShenduan && f.WuLuKangShenduan is { Stage: not WuLuKangShenduanStage.DiscardChoice and not WuLuKangShenduanStage.Drawing } && ValidWuLuKangShenduan(f) ||
         e?.Op == SkillProgramEffectOp.ResolveWuLuKangKegou && f.WuLuKangKegou is { Stage: WuLuKangKegouStage.GainChildren or WuLuKangKegouStage.NativeSlashChildren } && ValidWuLuKangKegou(f));
    private bool DrainWuLuKangChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool WuLuKangFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.WuLuKangShenduan is { } r && ValidWuLuKangShenduan(f))
        {
            if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == WuLuKangShenduanStage.Drawing && f.NativeDraw is not null;
            if (RecoveryReplacementFrameRidesOn(child, f)) return r.Stage == WuLuKangShenduanStage.DiscardChildren && r.Payment is not null;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                r.Stage == WuLuKangShenduanStage.DiscardChildren && r.Payment is { CostKind: CardKind.SilverLion, CostFrom.Zone: CardZoneKind.Equipment } && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Kind == HpChangeKind.Recovery;
        }
        else if (f.WuLuKangKegou is { } k && ValidWuLuKangKegou(f))
        {
            if (child is PindianFrame pair) return k.Stage == WuLuKangKegouStage.Pindian && pair.Id == k.PindianId && pair.ParentFrameId == f.Id && pair.SourceSeat == f.OwnerSeat && pair.OpponentSeat == k.OpponentSeat;
            if (child is CardUseFrame { WuLuKangKegouSlash: { } o } use) return k.Stage == WuLuKangKegouStage.NativeSlash && o.ProgramFrameId == f.Id && IsIssuedWuLuKangKegouUse(use);
        }
        else return false;
        if (f.PendingMovementContinuation is { } pending && IsWuLuKangMovement(f, GetPausedPrivateOfferEffect(f), pending) && child is CardsMovedTriggerWindowFrame moved)
        {
            var before = f.WuLuKangShenduan?.Before ?? f.WuLuKangKegou!.Before; var after = f.WuLuKangShenduan?.After ?? f.WuLuKangKegou!.After;
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => m.Sequence > before && m.Sequence <= after && WuLuKangSignedMovement(f, m));
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool WuLuKangObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && WuLuKangFirstChild(f, _resolutionStack[n]) ||
        WuLuKangPindianEdge(_resolutionStack[n], _resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool WuLuKangObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.WuLuKangObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? WuLuKangObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.WuLuKangFirstChild(f, c), static (g, n) => g.WuLuKangObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1360)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1470)]
    private bool HasWuLuKangDamageObserver(long id) => WuLuKangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasWuLuKangBeforeDamageObserver(long id) => WuLuKangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 750)]
    private bool IsWuLuKangDying() => ActiveDying is { } dying && WuLuKangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 130)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1920)]
    private bool AllowsWuLuKangNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || WuLuKangObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 530)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 520)]
    private bool IsWuLuKangRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && WuLuKangFirstChild(f, _resolutionStack[index + 1]) && WuLuKangObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29700)]
    private bool ResumeWuLuKang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.WuLuKangShenduan is not null) { ContinueWuLuKangShenduan(id); return true; }
        if (f.WuLuKangKegou is not null) { ContinueWuLuKangKegou(id); return true; } return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29700)]
    private bool ReturnWuLuKangMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeWuLuKang(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29700)]
    private bool AdvanceWuLuKangChildren() => _pendingDecision is null && WuLuKangObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29700)]
    private bool PermitAcceptedWuLuKang(ProgramSkillFrame f) => f.WuLuKangShenduan is { } r && (r.Payment is not null || r.WindowStage is WuLuKangPindianStage.PostComparison or WuLuKangPindianStage.GroupClose) && ValidWuLuKangShenduan(f) ||
        f.WuLuKangKegou is { PindianId: not null } && ValidWuLuKangKegou(f) || PermitWuLuKangIssuedCleanup(f);
    private bool PermitWuLuKangIssuedCleanup(ProgramSkillFrame f) => f.WuLuKangShenduan is null && f.TriggerId is not null && f.InstructionIndex == 1 &&
        f.WindowContext is { WuLuKangPindian.Stage: WuLuKangPindianStage.GroupClose } c && IsIssuedWuLuKangCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c);
    private void AssertWuLuKangPairProcessing(PindianFrame pair)
    {
        if (!IsWuLuKangPairSubtree(pair)) throw new InvalidOperationException("Shenduan cannot authorize an unrelated native Pindian subtree.");
        var expected = new HashSet<int>(pair.ParentProcessingCardIds ?? []); foreach (var id in WuLuKangHeldProcessing(pair)) expected.Add(id);
        var start = _resolutionStack.FindIndex(f => f.Id == pair.Id);
        foreach (var frame in _resolutionStack.Skip(start + 1))
        {
            if (frame is CardUseFrame { Action: { } action } use)
                foreach (var cost in action.PhysicalCards.Where(c => _cardZones.GetLocation(c.CardId) == CardLocation.Processing))
                {
                    var m = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId);
                    if (m is null || m.From != cost.From || m.To != CardLocation.Processing || m.CardKind != cost.CardKind || !expected.Add(cost.CardId)) throw new InvalidOperationException("Shenduan observer cannot claim an unpaid or ancestor native card.");
                }
            if (frame is ProgramCardTriggerWindowFrame { ResponseCompletion: not null } response && ValidResponseCompletionWindow(response))
                foreach (var cost in response.Action.PhysicalCards.Where(c => _cardZones.GetLocation(c.CardId) == CardLocation.Processing)) expected.Add(cost.CardId);
        }
        if (!expected.Order().SequenceEqual(_cardZones.CardsAt(CardLocation.Processing).Select(c => c.Id).Order())) throw new InvalidOperationException("Shenduan lost its exact native or observer processing materials.");
    }
}
