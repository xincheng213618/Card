namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidAnniversaryBly(ProgramSkillFrame f)
    {
        if (f.AnniversaryBly is not { } r || f.InstructionIndex != 1 || f.SkillId != AnniversaryBaiLingYunComposition.Linghui || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } w ||
            r.Source != AnniversaryBlySource(f) || r.Hash != f.GameplayHash || r.Turn != _turnNumber || r.ParentId != w.ParentFrameId || !Enum.IsDefined(r.Stage) ||
            r.Viewed.Count > 3 || r.Viewed.Distinct().Count() != r.Viewed.Count || r.ViewBefore < 0 || r.ViewAfter < r.ViewBefore || r.ViewAfter > _movementSequence || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<AnniversaryBlyStartedEvent>().Count(e => e == new AnniversaryBlyStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.Viewed.Count)) != 1) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1] is not TurnEndingBoundaryFrame p || p.Id != r.ParentId || p.OwnerSeat != _currentSeat || p.ItemIndex < 0 || p.ItemIndex >= p.Items.Count ||
            p.Items[p.ItemIndex].Candidate is not { } c || !MountObserverCandidateMatches(f, c)) return false;
        var viewed = _cardMovements.Where(m => m.Sequence > r.ViewBefore && m.Sequence <= r.ViewAfter && m.Reason == AnniversaryBlyReason("view")).ToArray();
        return viewed.Select(m => m.CardId).SequenceEqual(r.Viewed) && viewed.All(m => m.From == CardLocation.DrawPile && m.To == AnniversaryBlyLease(f));
    }
    private bool AnniversaryBlyUsePaid(ProgramSkillFrame f) => f.AnniversaryBly is { Plan: { } p } r && r.Viewed.Contains(p.CardId) &&
        _cardMovements.Count(m => m.CardId == p.CardId && m.Sequence > r.ViewAfter && m.From == AnniversaryBlyLease(f) && m.To == CardLocation.Processing && m.Reason == AnniversaryBlyReason("use")) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43600)] private void RequireAnniversaryBly(ProgramSkillFrame f)
    {
        if (f.AnniversaryBly is not { } r) return;
        if (!ValidAnniversaryBly(f)) throw new InvalidOperationException("Linghui lost its original ending, private viewed entities, source or actual turn.");
        if (r.Stage is AnniversaryBlyStage.NativeUse or AnniversaryBlyStage.NativeTail &&
            (r.Plan is null || !AnniversaryBlyUsePaid(f) || ProgramEventHistory<AnniversaryBlyNativeIssuedEvent>().Count(e => e.Origin.FrameId == f.Id && e.Origin.UseId == r.UseId && e.Origin.ActionId == r.ActionId && e.Origin.Source == r.Source && e.Origin.Hash == r.Hash) != 1 ||
             r.Returned && ProgramEventHistory<AnniversaryBlyNativeReturnedEvent>().Count(e => e == new AnniversaryBlyNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1))
            throw new InvalidOperationException("Linghui lost its exact paid and once-completed native use.");
        if (f.PendingMovementContinuation is { } pending && !AnniversaryBlyMovementProof(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("Linghui lost its original private viewing, use, gain or return payment.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 43600)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 43600)]
    private bool AnniversaryBlyMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.AnniversaryBly is not { } r || !ValidAnniversaryBly(f) || effect?.Op != SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui || pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray(); var lease = AnniversaryBlyLease(f);
        return r.Stage switch
        {
            AnniversaryBlyStage.Viewing => moves.Select(m => m.CardId).SequenceEqual(r.Viewed) && moves.All(m => m.From == CardLocation.DrawPile && m.To == lease && m.Reason == AnniversaryBlyReason("view")),
            AnniversaryBlyStage.UsePayment => moves is [var one] && r.Plan?.CardId == one.CardId && one.From == lease && one.To == CardLocation.Processing && one.Reason == AnniversaryBlyReason("use"),
            AnniversaryBlyStage.GainChildren => ProgramEventHistory<AnniversaryBlyGainCompletedEvent>().Count(e => e == new AnniversaryBlyGainCompletedEvent(f.Id, r.GainId is null ? 0 : 1, r.Before, r.After)) == 1 &&
                moves.All(m => r.Viewed.Contains(m.CardId) && (m.From == lease && (m.Reason == AnniversaryBlyReason("gain") && m.CardId == r.GainId || m.To == CardLocation.DrawPile && m.Reason == AnniversaryBlyReason("return")) ||
                    r.UseId == 0 && r.Plan?.CardId == m.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == AnniversaryBlyReason("cancelled"))),
            AnniversaryBlyStage.NativeTail => r.Returned && _pendingCardsMovedBatches.Where(b => r.NativeBatches.Contains(b.Id)).All(b => b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null &&
                b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m)) &&
                ProgramEventHistory<AnniversaryBlyNativeBatchEvent>().Count(e => e == new AnniversaryBlyNativeBatchEvent(f.Id, r.UseId, b.Id, b.Movements.Count)) == 1),
            _ => false
        };
    }
    private bool HasAnniversaryBaiLingYunPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryBly.Stage: AnniversaryBlyStage.Choice, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && ValidAnniversaryBly(f) && AssistedChoicesEqual(p.Choices, AnniversaryBlyChoices(f));
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.ProcessingAllowance, 43600)] private bool AnniversaryBlyProcessingAllowance(IReadOnlyList<Card> cards) => cards is [var card] &&
        _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryBly is not null) is { AnniversaryBly.Plan.CardId: var id } root && id == card.Id && ValidAnniversaryBly(root) && AnniversaryBlyUsePaid(root);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43600)]
    private bool RetainAnniversaryBly(ProgramSkillFrame f) => f.AnniversaryBly is not null && ValidAnniversaryBly(f);
    private bool AnniversaryBlyFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryBly is not { } r || !ValidAnniversaryBly(f)) return false;
        if (child is CardUseFrame use) return r.Stage == AnniversaryBlyStage.NativeUse && IsAnniversaryBlyNative(use);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && AnniversaryBlyMovementProof(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? AnniversaryBlyObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.AnniversaryBlyFirstChild(f, child), static (e, index) => e.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 43600)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 43600)]
    private bool AnniversaryBlyDamageObserver(long id) => AnniversaryBlyObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 43600)] private bool AnniversaryBlyBeforeDamageObserver(long id) => AnniversaryBlyObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 43600)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 43600)] private bool AnniversaryBlyDyingObserver() => ActiveDying is { } dying && AnniversaryBlyObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 43600)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 43600)]
    private bool AnniversaryBlyRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryBly is null) return false; var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent && AnniversaryBlyFirstChild(f, _resolutionStack[parent + 1]) && (child == parent + 1 || Enumerable.Range(parent + 2, child - parent - 1).All(i => SameNameHandObserverSuffix(i))) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 43600)] private bool AdvanceAnniversaryBlyChildren() => _pendingDecision is null && AnniversaryBlyObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
