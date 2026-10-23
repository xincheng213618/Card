namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool MaMiaoOriginalParent(ProgramSkillFrame f)
    {
        if (f.AnniversaryMaMiao is not { } r || f.WindowContext is not { } w || r.ParentId != w.ParentFrameId) return false;
        if (r.Mode == "congfeng") return f.SkillId == AnniversaryMaMiaoComposition.Congfeng && MaMiaoGainQualification(MaMiaoCandidate(f), w) is { } e && r.Holder == e.Holder && r.Recipient == e.Recipient;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return r.Mode == "zhangguan" && f.SkillId == AnniversaryMaMiaoComposition.Zhangguan && w.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            r.Holder == _turnProgression.OwnerSeat && r.Recipient == f.OwnerSeat && i > 0 && _resolutionStack[i - 1] is ProgramLifecycleTriggerWindowFrame parent &&
            parent.Id == r.ParentId && parent.Window == w.Window && parent.OwnerSeat == _turnProgression.OwnerSeat && parent.Continuation == ProgramLifecycleContinuation.NormalTurnStart &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]);
    }
    private bool ValidMaMiao(ProgramSkillFrame f)
    {
        if (f.AnniversaryMaMiao is not { } r || r.Source != MaMiaoSource(f) || r.Hash != f.GameplayHash || f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId ||
            r.Turn != _turnNumber || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            GetPausedPrivateOfferEffect(f)?.Op != (r.Mode == "zhangguan" ? SkillProgramEffectOp.ResolveAnniversaryMaMiaoZhangguan : SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng) ||
            !MaMiaoOriginalParent(f) || r.Stage is not ("adjust" or "drawing" or "discard" or "adjust-children" or "after-adjust" or "recovery-children" or "obtain-choice" or "extra-choice" or "gain-children") ||
            r.InitialHand < 0 || r.AdjustmentHp < 0 || r.Count < 0 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Cards.Distinct().Count() != r.Cards.Count || r.Cards.Count > r.Count ||
            ProgramEventHistory<AnniversaryMaMiaoStartedEvent>().Count(e => e == new AnniversaryMaMiaoStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.Mode, r.Holder, r.Recipient, r.InitialHand, r.AdjustmentHp)) != 1) return false;
        if (r.Stage == "extra-choice") return r.Mode == "congfeng" && r.RandomPool.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null;
        if (r.Mode == "zhangguan" && r.Stage is "recovery-children" or "obtain-choice" or "gain-children")
            if (r.HandAfter < 0 || r.HandAfter > r.InitialHand || ProgramEventHistory<AnniversaryMaMiaoAdjustedEvent>().Count(e => e == new AnniversaryMaMiaoAdjustedEvent(f.Id, r.InitialHand, r.HandAfter, r.AdjustmentHp)) != 1 ||
                ProgramEventHistory<AnniversaryMaMiaoRecoveryIssuedEvent>().Count(e => e == new AnniversaryMaMiaoRecoveryIssuedEvent(f.Id, f.OwnerSeat, 1)) != 1) return false;
        if (r.Stage == "gain-children" && r.Mode == "congfeng")
        {
            if (ProgramEventHistory<AnniversaryMaMiaoDecisionEvent>().Count(e => e.FrameId == f.Id && e.Chooser == r.Recipient && e.Accepted) != 1 || r.RandomPool.Count == 0 ||
                r.RandomPool.Distinct().Count() != r.RandomPool.Count || r.RandomIndex < 0 || r.RandomIndex >= r.RandomPool.Count || r.RandomPool[r.RandomIndex] != new AnniversaryMaMiaoEntity(r.CardId ?? 0, r.From ?? CardLocation.Processing)) return false;
            var random = new DeterministicRandom(unchecked((int)r.RandomBefore));
            if (random.Next(r.RandomPool.Count) != r.RandomIndex || random.State != r.RandomAfter || ProgramEventHistory<AnniversaryMaMiaoRandomEvent>().Count(e =>
                e == new AnniversaryMaMiaoRandomEvent(f.Id, r.Recipient, r.Holder, r.CardId!.Value, r.From!.Value, r.RandomBefore, r.RandomAfter, r.RandomIndex, r.RandomPool.Count)) != 1) return false;
        }
        if (r.Stage is "adjust-children" or "gain-children")
        {
            if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat) return false;
            if (r.Stage == "gain-children")
            {
                if (r.CardId is not { } id || r.From is not { } from || from.OwnerSeat != r.Holder || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || r.Gain is not { } gain || gain.Reason != MaMiaoGainReason(f)) return false;
                ValidateHappyWeiYanGainRequestSettlement(gain, f.Id, [id], CardLocation.Hand(r.Recipient), r.Before);
                return ProgramEventHistory<AnniversaryMaMiaoMovementEvent>().Count(e => e == new AnniversaryMaMiaoMovementEvent(f.Id, "gain", 1, r.Before, r.After)) == 1 &&
                    gain.Movements.All(m => m.CardId == id && m.From == from && _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
            }
            var draw = r.Draw is not null;
            if (ProgramEventHistory<AnniversaryMaMiaoMovementEvent>().Count(e => e == new AnniversaryMaMiaoMovementEvent(f.Id, draw ? "draw" : "discard", r.Count, r.Before, r.After)) != 1) return false;
            return draw ? r.Draw!.RecipientSeat == f.OwnerSeat && r.Draw.RequestedCount == r.Count && r.Draw.SequenceBefore == r.Before && r.Draw.SequenceAfter == r.After :
                r.Cards.Count == r.Count && r.Cards.All(id => _cardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason == MaMiaoDiscardReason && m.Sequence > r.Before && m.Sequence <= r.After) == 1);
        }
        return f.PendingMovementContinuation is null;
    }
    private bool HasAnniversaryMaMiaoPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryMaMiao.Stage: "discard" or "obtain-choice" or "extra-choice", Step: ResolutionFrameStep.AwaitingResponse } f &&
        ValidMaMiao(f) && p.PlayerSeat == MaMiaoChooser(f) && p.TargetSeat == MaMiaoChooser(f) && AssistedChoicesEqual(p.Choices, MaMiaoChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47200)] private void AssertMaMiao(ProgramSkillFrame f)
    {
        if (f.AnniversaryMaMiao is null) return;
        if (!ValidMaMiao(f)) throw new InvalidOperationException("Ma Miao lost his original turn/gain parent, private native payment, deterministic random choice or recovery-before-obtain sequence.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is not null && f.AnniversaryMaMiao.Stage is "discard" or "obtain-choice" or "extra-choice" && !HasAnniversaryMaMiaoPrompt())
            throw new InvalidOperationException("Ma Miao's exact current recipient choice or hidden owned-card slot changed.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 47200)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 47200)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 47200)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 47200)]
    private bool MaMiaoMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.AnniversaryMaMiao is { Stage: "adjust-children" or "gain-children" } &&
        e?.Op is SkillProgramEffectOp.ResolveAnniversaryMaMiaoZhangguan or SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng && p == f.PendingMovementContinuation && ValidMaMiao(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47200)] private bool RetainMaMiao(ProgramSkillFrame f) => f.AnniversaryMaMiao is { Stage: "adjust-children" or "gain-children" or "recovery-children" } && ValidMaMiao(f);
    private bool MaMiaoFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryMaMiao is not { } r || !ValidMaMiao(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == "drawing" && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && MaMiaoMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id &&
            recovery.Return.Continuation == (r.Stage == "recovery-children" ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) && RecoveryReplacementFrameRidesOn(recovery, f);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id &&
            hp.Continuation == (r.Stage == "recovery-children" ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool MaMiaoObserverEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MaMiaoFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private ProgramSkillFrame? MaMiaoObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.MaMiaoFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.MaMiaoObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 47200)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 47200)] private bool MaMiaoDamageObserver(long id) => MaMiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 47200)] private bool MaMiaoBeforeDamageObserver(long id) => MaMiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 47200)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 47200)] private bool MaMiaoDying() => ActiveDying is { } d && MaMiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 47200)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 47200)] private bool MaMiaoRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryMaMiao is null) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var j = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && j > i && MaMiaoFirstChild(f, _resolutionStack[i + 1]) && (j == i + 1 || SameNameHandObserverSuffix(i)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 47200)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 47200)] private bool MaMiaoNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MaMiaoObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 47200)] private bool AdvanceMaMiaoChildren() => _pendingDecision is null && MaMiaoObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
