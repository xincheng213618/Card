namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiGy(ProgramSkillFrame f) => f.ShiGuoYuan is { } r && f.InstructionIndex == 1 && r.Source == ShiGySource(f) && r.Hash == f.GameplayHash && r.Turn == _turnNumber &&
        r.WindowId == f.WindowContext?.ParentFrameId && Enum.IsDefined(r.Stage) && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<ShiGyStartedEvent>().Count(e => e == new ShiGyStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.WindowId, f.TriggerId!)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 48100)] private void RequireShiGy(ProgramSkillFrame f)
    {
        if (f.ShiGuoYuan is not { } r) return;
        if (!ValidShiGy(f) || f.SkillId == ShiGyXiugeng && r.Stage == ShiGyStage.Record && (r.Seats.Count > 2 || r.Seats.Distinct().Count() != r.Seats.Count || r.Seats.Any(s => !IsValidPlayerSeat(s))) ||
            f.SkillId == ShiGyChenshe && (r.Seats.Count != 3 || r.Seats[0] != f.OwnerSeat || r.Seats[1] != r.Victim || r.Seats[2] != r.Killer || r.Index is < 0 or > 3 ||
                !ExactDyingOwnedCardEntry(f, out var dying, out _) || dying.Id != r.DyingId || dying.VictimSeat != r.Victim || dying.KillerSeat != (r.Killer < 0 ? null : r.Killer)))
            throw new InvalidOperationException("Shi Guo Yuan lost the accepted native window, source, actual turn or ordered rescue participants.");
        if (r.Paid.Any(p => p.ParticipantIndex < 0 || p.ParticipantIndex >= r.Index || !IsValidPlayerSeat(p.OwnerSeat) || !Enum.IsDefined(p.Suit) ||
            f.SkillId == ShiGyChenshe && r.Seats[p.ParticipantIndex] != p.OwnerSeat || p.From.OwnerSeat != p.OwnerSeat ||
            ProgramEventHistory<ShiGyDiscardPaidEvent>().Count(e => e.FrameId == f.Id && e.Card == p) != 1 ||
            _cardMovements.Count(m => m.Sequence == p.Sequence && m.CardId == p.CardId && m.From == p.From && m.To == p.To && m.Reason == ShiGyDiscardReason) != 1) ||
            r.Paid.Select(p => p.ParticipantIndex).Distinct().Count() != r.Paid.Count || r.Paid.Select(p => p.Sequence).Order().SequenceEqual(r.Paid.Select(p => p.Sequence)) == false)
            throw new InvalidOperationException("Chenshe must retain each genuinely discarded occurrence and its original effective suit exactly once.");
        if (r.Native is { } n && (n.FrameId != f.Id || n.Source != r.Source || n.Hash != r.Hash || n.Turn != r.Turn || n.Actor != f.OwnerSeat || n.CardId <= 0 || n.Action.CardId != n.CardId ||
            r.Stage is ShiGyStage.NativeUse or ShiGyStage.NativeTail && (n.UseId <= 0 || n.ActionId <= 0 ||
                ProgramEventHistory<ShiGyNativeIssuedEvent>().Count(e => e == new ShiGyNativeIssuedEvent(f.Id, n.UseId, n.ActionId, n.CardId)) != 1 ||
                r.Returned && ProgramEventHistory<ShiGyNativeReturnedEvent>().Count(e => e == new ShiGyNativeReturnedEvent(f.Id, n.UseId, n.ActionId)) != 1)))
            throw new InvalidOperationException("Qingdao lost its exact issued or once-returned physical Hand use.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ShiGyStage.Benefit or ShiGyStage.Record or ShiGyStage.Discard or ShiGyStage.UseChoice &&
            (_pendingDecision is not { } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, ShiGyChoices(f))))
            throw new InvalidOperationException("Shi Guo Yuan lost its original published choice set.");
    }
    private bool HasShiGyPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShiGuoYuan: not null, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && ValidShiGy(f) && AssistedChoicesEqual(p.Choices, ShiGyChoices(f));
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 48100)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 48100)] [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 48100)]
    private bool ShiGyMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p)
    {
        if (f.ShiGuoYuan is not { } r || p != f.PendingMovementContinuation || p.SubjectSeat != f.OwnerSeat || p.BeforeCount != 0 || p.CoverageResultBind is not null || !ValidShiGy(f) ||
            effect?.Op is not (SkillProgramEffectOp.ResolveMobileShiGuoYuanQingdao or SkillProgramEffectOp.ResolveMobileShiGuoYuanXiugeng or SkillProgramEffectOp.ResolveMobileShiGuoYuanChenshe)) return false;
        return r.Stage is ShiGyStage.Movement or ShiGyStage.DrawChildren or ShiGyStage.RecoveryChildren or ShiGyStage.LostSkillChildren || r.Stage == ShiGyStage.NativeTail && r.Returned && r.Native is not null;
    }
    private bool IsShiGyNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.ShiGuoYuan is { Stage: ShiGyStage.NativeTail, Returned: true, Native: { } n } r &&
        ValidShiGy(f) && r.NativeBatches.Contains(b.Id) && b.ParentFrameId == n.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId &&
        b.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m)) && ProgramEventHistory<ShiGyNativeBatchEvent>().Count(e => e == new ShiGyNativeBatchEvent(f.Id, n.UseId, b.Id)) == 1;
    private bool ShiGyFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ShiGuoYuan is not { } r || !ValidShiGy(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == ShiGyStage.Drawing && draw.Context.RecipientSeat == r.Victim && draw.Context.RequestedCount == 2 && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardUseFrame use) return r.Stage == ShiGyStage.NativeUse && IsShiGyNative(use);
        if (child is CardDeclarationFrame d) return MatchesShiGyDeclaration(f, d);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ShiGyMovement(f, GetPausedPrivateOfferEffect(f), p) &&
            ProgramMovementReturnMatches(f, moved) && (IsShiGyNativeSettlementBatch(f, moved.Batch) || moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m)));
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiGyChildSuffix(int i) => HasObserverSuffix(i, static (e, index) => e.IsSameNameHandObserverEdge(index) || e.ShiGyDeclarationEdge(index),
        static (e, index, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(index, dying) || e.TieredRoundZeroDyingRescueRide(index, dying) || e.DrawFundedDistinctBasicDyingRescueRide(index, dying) || e.HuanpeiDyingRescueRide(index, dying));
    private ProgramSkillFrame? ShiGyObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.ShiGyFirstChild(f, child), static (e, i) => e.ShiGyChildSuffix(i));
    private bool ShiGyDeclarationEdge(int i)
    {
        if (i < 1 || _resolutionStack[i - 1] is not CardDeclarationFrame d || _resolutionStack.SingleOrDefault(f => f.Id == d.Return.ParentFrameId) is not ProgramSkillFrame root || !MatchesShiGyDeclaration(root, d)) return false;
        if (_resolutionStack[i] is CardDeclarationChallengeFrame challenge) return d.Stage == CardDeclarationStage.Challenging && challenge.ParentFrameId == d.Id && challenge.SeatIndex >= 0 && challenge.SeatIndex < challenge.EligibleSeats.Count;
        if (_resolutionStack[i] is CardsMovedTriggerWindowFrame moved) return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning && d.ActiveChildFrameId == moved.Id && moved.ResumeDeclarationFrameId == d.Id && moved.Batch.ParentFrameId == d.Id && moved.Batch.Movements is [var m] &&
            _cardMovements.Contains(m) && m.CardId == d.Payment.Cost.CardId && (d.Stage == CardDeclarationStage.Paying ? m.From == d.Payment.Cost.From && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay" :
                m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled");
        return _resolutionStack[i] is ProgramLifecycleTriggerWindowFrame skills && d.Stage == CardDeclarationStage.Granting && skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == d.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeCardDeclaration;
    }
    private bool HasShiGyCompletedUseObserver(long id) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ShiGuoYuan is { OriginalUse: var use } && use == id && f.SkillId == ShiGyQingdao && ValidShiGy(f));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 48100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 48100)] private bool ShiGyDamageChild(long id) => ShiGyObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 48100)] private bool ShiGyBeforeDamageChild(long id) => ShiGyObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 48100)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 48100)] private bool ShiGyDyingChild() => ActiveDying is { } d && ShiGyObserverRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 48100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 48100)] private bool RetainShiGy(ProgramSkillFrame f) =>
        f.ShiGuoYuan is not null && ValidShiGy(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 48100)] private bool AdvanceShiGyChildren() => _pendingDecision is null && ShiGyObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 48100)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 48100)]
    private bool ShiGyRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.ShiGuoYuan is null) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent && ShiGyFirstChild(f, _resolutionStack[parent + 1]) && (child == parent + 1 || ShiGyChildSuffix(parent)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiGuoYuanPublicStates(CharacterState owner, string skillId)
    {
        if (skillId is not (ShiGyXiugeng or ShiGyQingdao or ShiGyChenshe)) return [];
        var states = new List<ProgramPublicRuleStateSnapshot>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId))
        {
            if (skillId == ShiGyXiugeng)
            {
                states.Add(new(instance.SkillInstanceId, "xiugeng-hand-limit", ProgramPublicRuleStateKind.PersistentHandLimit, ShiGyHandLimitBonus(owner.Seat)));
                if (ShiGyRecord(owner.Seat, instance.SkillInstanceId) is { } record)
                    states.AddRange(ProgramEventHistory<ShiGyHandRecordedEvent>().Where(e => e.FrameId == record.FrameId).Select(e =>
                        new ProgramPublicRuleStateSnapshot(instance.SkillInstanceId, "xiugeng-seat-" + e.Target, ProgramPublicRuleStateKind.GameActivationProgress, e.HandCount)));
            }
            else states.Add(new(instance.SkillInstanceId, skillId == ShiGyQingdao ? "qingdao-ready" : "chenshe-ready", ProgramPublicRuleStateKind.GameActivationProgress, 1));
        }
        return states.ToArray();
    }
}
