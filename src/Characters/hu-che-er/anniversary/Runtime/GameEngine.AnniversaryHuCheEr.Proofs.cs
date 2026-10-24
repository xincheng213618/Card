namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HuCheErParent(ProgramSkillFrame f) => f.SkillId == AnniversaryHuCheErComposition.Daoji && f.TriggerId == "first-foreign-weapon" && f.ActivationId == f.TriggerId && f.InstructionIndex == 1 && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && f.WindowContext is { } c &&
        HuCheErFirstWeapon(f.OwnerSeat, c) is not null && _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } parent && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex]));
    private bool ValidHuCheEr(ProgramSkillFrame f)
    {
        if (f.AnniversaryHuCheEr is not { } r || !HuCheErParent(f) || r.Source != HuCheErSource(f) || r.Hash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash || r.ParentId != f.WindowContext!.ParentFrameId ||
            HuCheErFirstWeapon(f.OwnerSeat, f.WindowContext) is not { Action: { } a } use || r.UseId != use.Id || r.ActionId != a.ActionId || r.Actor != a.ActorSeat || r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<AnniversaryHuCheErStartedEvent>().Count(e => e == new AnniversaryHuCheErStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.UseId, r.ActionId, r.Actor, r.Turn, r.TurnOwner)) != 1 ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.SkillInstanceId == f.SkillInstanceId && e.BindingId == f.TriggerId && e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) != 1) return false;
        if (r.Stage == AnniversaryHuCheErStage.Choice) return r.Materials.Count == 0 && r.Moves.Count == 0 && r.Gain is null && f.PendingMovementContinuation is null;
        if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat || r.Materials.Count == 0 || r.Materials.Distinct().Count() != r.Materials.Count || r.Materials.Any(id => !a.PhysicalCards.Any(c => c.CardId == id)) || r.Gain is not { } gain || gain.Reason.Value != HuCheErClaimReason ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !gain.Movements.SequenceEqual(r.Moves)) return false;
        ValidateHappyWeiYanGainRequestSettlement(gain, f.Id, r.Materials, CardLocation.Hand(f.OwnerSeat), r.Before);
        return ProgramEventHistory<AnniversaryHuCheErWeaponClaimedEvent>().Count(e => e == new AnniversaryHuCheErWeaponClaimedEvent(f.Id, r.UseId, r.ActionId, f.OwnerSeat, r.Materials.Count, r.Before, r.After)) == 1 &&
            r.Moves.All(m => r.Materials.Contains(m.CardId) && m.From == CardLocation.Processing && _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
    }
    private bool IsAnniversaryHuCheErClaimedUseMaterial(long useId, int cardId)
    {
        if (!HasAnniversaryHuCheErRules || LifecycleCardUse(useId) is not { Action: { Type: CardActionType.Use } a } use || !HuCheErWeapon(a.EffectiveKind) || !a.PhysicalCards.Any(c => c.CardId == cardId)) return false;
        return ProgramEventHistory<AnniversaryHuCheErWeaponClaimedEvent>().Any(e => e.UseId == useId && e.ActionId == a.ActionId && e.Count > 0 && e.Before >= 0 && e.After >= e.Before && e.After <= _movementSequence &&
            ProgramEventHistory<AnniversaryHuCheErStartedEvent>().Count(s => s.FrameId == e.FrameId && s.UseId == useId && s.ActionId == a.ActionId && s.Actor == a.ActorSeat && s.Source.OwnerSeat == e.Recipient && s.Source.SkillId == AnniversaryHuCheErComposition.Daoji && s.Source.BindingId == "first-foreign-weapon" && _contentRegistry.GetSkill(s.Source.SkillId).Program?.GameplayHash == s.Hash) == 1 &&
            _cardMovements.Count(m => m.CardId == cardId && m.From == CardLocation.Processing && (m.To == CardLocation.Hand(e.Recipient) || m.To == CardLocation.OutsideGame && GetAdvancedCard(cardId).IsGeneralWeapon || IsHappyWeiYanRedirectedGain(m, CardLocation.Hand(e.Recipient))) && m.Reason.Value == HuCheErClaimReason && m.Sequence > e.Before && m.Sequence <= e.After) == 1);
    }
    private bool HasAnniversaryHuCheErPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryHuCheEr.Stage: AnniversaryHuCheErStage.Choice, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && ValidHuCheEr(f) && AssistedChoicesEqual(p.Choices, HuCheErChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49500)] private void AssertHuCheEr(ProgramSkillFrame f)
    { if (f.AnniversaryHuCheEr is not null && !ValidHuCheEr(f)) throw new InvalidOperationException("Daoji lost its original first Weapon Use, private choice or once-paid entity gain."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49500)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49500)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49500)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 49500)]
    private bool HuCheErMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.AnniversaryHuCheEr is { Stage: AnniversaryHuCheErStage.MovementChildren } && e?.Op == SkillProgramEffectOp.ResolveAnniversaryHuCheErDaoji && p is not null && p == f.PendingMovementContinuation && ValidHuCheEr(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49500)] private bool RetainHuCheEr(ProgramSkillFrame f) => f.AnniversaryHuCheEr is { Stage: AnniversaryHuCheErStage.MovementChildren } && ValidHuCheEr(f);
    private bool HuCheErFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryHuCheEr is not { Stage: AnniversaryHuCheErStage.MovementChildren } r || !ValidHuCheEr(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && HuCheErMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(r.Moves.Contains);
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && RecoveryReplacementFrameRidesOn(recovery, f);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool HuCheErObserverEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && HuCheErFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private ProgramSkillFrame? HuCheErObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.HuCheErFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.HuCheErObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49500)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49500)] private bool HuCheErDamageObserver(long id) => HuCheErObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49500)] private bool HuCheErBeforeDamageObserver(long id) => HuCheErObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49500)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49500)] private bool HuCheErDyingObserver() => ActiveDying is { } dying && HuCheErObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49500)] private bool AdvanceHuCheErChildren() => _pendingDecision is null && HuCheErObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
