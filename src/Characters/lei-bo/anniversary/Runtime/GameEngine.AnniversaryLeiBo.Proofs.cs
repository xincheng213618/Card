namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidLeiBo(ProgramSkillFrame f)
    {
        if (f.AnniversaryLeiBo is not { } r || r.Source != LeiBoSource(f) || r.Hash != f.GameplayHash || !IsValidPlayerSeat(r.Marked) || !Enum.IsDefined(r.Stage) || r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat) return false;
        if (f.SkillId == AnniversaryLeiBoComposition.Silei)
        {
            if (f.TriggerId != "marked-victim-reply" || f.ActivationId != f.TriggerId || !LeiBoDamageParent(f) || f.WindowContext is not { } c || c.TargetSeat != r.Marked || c.SourceSeat != r.DamageSource || c.ParentFrameId != r.ParentId || c.DamageFrameId != r.DamageId ||
                f.InstructionIndex is < 1 or > 2 || ProgramEventHistory<AnniversaryLeiBoReplyStartedEvent>().Count(e => e == new AnniversaryLeiBoReplyStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.DamageId, r.Marked, r.DamageSource, r.Turn, r.TurnOwner)) != 1) return false;
            if ((r.UseId == 0) != (r.ActionId == 0) || r.UseId != 0 && (r.DamageSource is not { } target || ProgramEventHistory<AnniversaryLeiBoSlashUsedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId && e.Target == target) != 1 || ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == r.UseId && e.SourceSeat == f.OwnerSeat && IsSlashCard(e.CardKind)) != 1)) return false;
            if (r.Stage == AnniversaryLeiBoStage.SlashRequest) return r.Selected.Count == 0 && r.Moves.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null;
            if (r.Stage == AnniversaryLeiBoStage.PenaltyChoice) return f.InstructionIndex == 2 && r.UseId == 0 && r.Selected.Count == 0 && r.Moves.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null;
            if (r.Stage != AnniversaryLeiBoStage.MovementChildren || f.InstructionIndex != 2 || r.UseId != 0 || r.Branch != "discard" || r.Selected.Count != 1) return false;
        }
        else if (f.SkillId == AnniversaryLeiBoComposition.Shuaijie)
        {
            if (f.TriggerId is not null || f.ActivationId != "seize-and-return-mark" || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.ParentId != 0 || r.DamageId != 0 || r.DamageSource is not null || r.UseId != 0 || r.ActionId != 0 || f.InstructionIndex is < 1 or > 3 ||
                ProgramEventHistory<AnniversaryLeiBoShuaijieStartedEvent>().Count(e => e == new AnniversaryLeiBoShuaijieStartedEvent(f.Id, r.Source, r.Hash, r.Marked, r.Turn, r.TurnOwner, r.MaximumBefore, r.HpBefore)) != 1) return false;
            if (r.Stage == AnniversaryLeiBoStage.BeforeMaximum) return r.Branch is null && r.Selected.Count == 0 && r.Moves.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null;
            if (f.InstructionIndex != 3 || !LeiBoMaximumPaid(f)) return false;
            if (r.Stage == AnniversaryLeiBoStage.RewardChoice) return r.Branch is null && r.Selected.Count == 0 && r.Moves.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null;
            if (r.Branch is not ("marked" or "deck") || ProgramEventHistory<AnniversaryLeiBoBranchChosenEvent>().Count(e => e.FrameId == f.Id && e.Branch == r.Branch) != 1) return false;
            if (r.Stage == AnniversaryLeiBoStage.PickReward) return r.Branch == "marked" && r.Selected.Count < 3 && r.Selected.Distinct().Count() == r.Selected.Count && r.Moves.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null && r.Selected.All(id => _cardZones.GetLocation(id) is { OwnerSeat: var owner, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } && owner == r.Marked);
            if (r.Stage != AnniversaryLeiBoStage.MovementChildren || r.Selected.Count is < 1 or > 3) return false;
        }
        else return false;
        if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat || r.Gain is not { } gain || gain.ProducerFrameId != f.Id || gain.SequenceBefore != r.Before || gain.Reason.Value != "program.lei-bo." + r.Branch || r.Selected.Distinct().Count() != r.Selected.Count || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !gain.Movements.SequenceEqual(r.Moves) ||
            ProgramEventHistory<AnniversaryLeiBoMovementSettledEvent>().Count(e => e == new AnniversaryLeiBoMovementSettledEvent(f.Id, r.Branch!, r.Selected.Count, r.Before, r.After)) != 1 ||
            r.Moves.Any(m => !r.Selected.Contains(m.CardId) || !_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After || m.Reason.Value != "program.lei-bo." + r.Branch)) return false;
        if (r.Branch == "discard") return r.Moves.Count == 1 && r.Moves[0].From == CardLocation.Hand(f.OwnerSeat) && (r.Moves[0].To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame || IsRedirectedPaidDiscardMovement(r.Moves[0], CardLocation.Hand(f.OwnerSeat), new("program.lei-bo.discard")));
        ValidateHappyWeiYanGainRequestSettlement(gain, f.Id, r.Selected, CardLocation.Hand(f.OwnerSeat), r.Before);
        return r.Moves.All(m => r.Branch == "deck" ? m.From == CardLocation.DrawPile : m.From.OwnerSeat == r.Marked && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment) &&
            (r.Branch != "deck" || r.Selected.Select(id => CardCatalog.Get(GetAdvancedCard(id).Kind).CategoryName).Distinct().Count() == r.Selected.Count);
    }
    private bool LeiBoMaximumPaid(ProgramSkillFrame f)
    {
        var facts = _events.Select(x => x.Payload).Concat(_pendingEvents).ToArray();
        var start = Array.FindIndex(facts, e => e is AnniversaryLeiBoShuaijieStartedEvent s && s.FrameId == f.Id);
        return start >= 0 && facts.Skip(start + 1).OfType<MaximumHpChangedEvent>().Any(e => e.PlayerSeat == f.OwnerSeat && e.SkillId == f.SkillId);
    }
    private bool HasAnniversaryLeiBoPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryLeiBo.Stage: AnniversaryLeiBoStage.PenaltyChoice or AnniversaryLeiBoStage.RewardChoice or AnniversaryLeiBoStage.PickReward, Step: ResolutionFrameStep.AwaitingResponse } f && p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && ValidLeiBo(f) && AssistedChoicesEqual(p.Choices, LeiBoChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49800)] private void AssertLeiBo(ProgramSkillFrame f)
    { if (f.AnniversaryLeiBo is not null && !ValidLeiBo(f)) throw new InvalidOperationException("Lei Bo lost its original damage, paid maximum-HP instruction or actual movement receipt."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49800)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49800)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49800)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 49800)]
    private bool LeiBoMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.AnniversaryLeiBo is { Stage: AnniversaryLeiBoStage.MovementChildren } && e?.Op is SkillProgramEffectOp.SettleAnniversaryLeiBoSileiPenalty or SkillProgramEffectOp.FinishAnniversaryLeiBoShuaijie && p == f.PendingMovementContinuation && ValidLeiBo(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49800)] private bool RetainLeiBo(ProgramSkillFrame f) => f.AnniversaryLeiBo is { Stage: AnniversaryLeiBoStage.MovementChildren } && ValidLeiBo(f);
    private bool LeiBoFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryLeiBo is not { Stage: AnniversaryLeiBoStage.MovementChildren } r || !ValidLeiBo(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && LeiBoMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(r.Moves.Contains);
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && RecoveryReplacementFrameRidesOn(recovery, f);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool LeiBoObserverEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && LeiBoFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private ProgramSkillFrame? LeiBoObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.LeiBoFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.LeiBoObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49800)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49800)] private bool LeiBoDamageObserver(long id) => LeiBoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49800)] private bool LeiBoBeforeDamageObserver(long id) => LeiBoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49800)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49800)] private bool LeiBoDyingObserver() => ActiveDying is { } dying && LeiBoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49800)] private bool AdvanceLeiBoChildren() => _pendingDecision is null && LeiBoObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
