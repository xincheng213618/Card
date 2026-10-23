namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool AnniversaryZtgOriginal(ProgramSkillFrame f)
    {
        if (f.SkillId is not (AnniversaryZhaoTongZhaoGuangComposition.Longyuan or AnniversaryZhaoTongZhaoGuangComposition.Qingren) ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
                e.SkillInstanceId == f.SkillInstanceId && e.BindingId == f.TriggerId && e.Window == c.Window) != 1) return false;
        if (f.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Longyuan)
            return f.TriggerId == "three-yizan-awakening" && c.Window == SkillProgramTriggerWindow.AfterTurnEnded &&
                _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == c.ParentFrameId) is { } parent && MatchesAfterTurnEndedChild(parent, f, c);
        return f.TriggerId == "own-ending-yizan-count" && c.Window == SkillProgramTriggerWindow.TurnEnding && c.SourceSeat == f.OwnerSeat && c.TargetSeat == f.OwnerSeat &&
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == c.ParentFrameId) is { } ending &&
            ending.OwnerSeat == f.OwnerSeat && ending.TurnNumber == _turnNumber && ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count &&
            ending.Items[ending.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate) && candidate.OccurrenceIndex == c.OccurrenceIndex;
    }
    private bool ValidAnniversaryQingren(ProgramSkillFrame f)
    {
        if (f.AnniversaryQingren is not { } r || !AnniversaryZtgOriginal(f) || f.SkillId != AnniversaryZhaoTongZhaoGuangComposition.Qingren ||
            r.Source != AnniversaryZtgSource(f) || r.Hash != f.GameplayHash || r.ParentId != f.WindowContext!.ParentFrameId || r.Turn != _turnNumber ||
            !Enum.IsDefined(r.Stage) || f.InstructionIndex is < 1 or > 2 || f.InstructionIndex == 2 && r.Stage != AnniversaryQingrenStage.Completed ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.CutoffActionId < 0 || r.CutoffActionId > _cardActionSequence ||
            r.Count != AnniversaryYizanCount(f.OwnerSeat, r.Turn, r.CutoffActionId) ||
            ProgramEventHistory<AnniversaryQingrenIssuedEvent>().Count(e => e == new AnniversaryQingrenIssuedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.CutoffActionId, r.Count)) != 1) return false;
        if (r.Draw is not { } d)
            return r.Count == 0 ? r.Stage == AnniversaryQingrenStage.Completed : r.Stage == AnniversaryQingrenStage.Drawing;
        return r.Count > 0 && r.Stage is AnniversaryQingrenStage.DrawChildren or AnniversaryQingrenStage.Completed &&
            d.RecipientSeat == f.OwnerSeat && d.RequestedCount == r.Count && d.SequenceBefore >= 0 && d.SequenceAfter >= d.SequenceBefore && d.SequenceAfter <= _movementSequence &&
            d.ActualDrawIds is System.Collections.IList { IsReadOnly: true } && d.ActualDrawIds.Distinct().Count() == d.ActualDrawIds.Count &&
            ProgramEventHistory<AnniversaryQingrenDrawReturnedEvent>().Count(e => e == new AnniversaryQingrenDrawReturnedEvent(f.Id, r.Count, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter)) == 1 &&
            d.ActualDrawIds.All(id => _cardMovements.Count(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter && m.CardId == id &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == AnniversaryQingrenDrawReason) == 1);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 44600)]
    private void AssertAnniversaryQingren(ProgramSkillFrame f)
    { if (f.AnniversaryQingren is not null && !ValidAnniversaryQingren(f)) throw new InvalidOperationException("Qingren lost its original ending candidate, frozen actual-turn count or native draw invoice."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 44600)]
    private bool AnniversaryQingrenMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.AnniversaryQingren is { Stage: AnniversaryQingrenStage.DrawChildren } && ValidAnniversaryQingren(f) && e?.Op == SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren &&
        f.PendingMovementContinuation == p && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44600)]
    private bool KeepPaidAnniversaryQingren(ProgramSkillFrame f) => f.AnniversaryQingren is { Stage: AnniversaryQingrenStage.Drawing or AnniversaryQingrenStage.DrawChildren } && ValidAnniversaryQingren(f);
    private bool AnniversaryQingrenFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryQingren is not { } r || !ValidAnniversaryQingren(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversaryQingrenStage.Drawing && NativeDrawAttemptRidesOn(draw, f) &&
            draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == r.Count;
        if (child is CardsMovedTriggerWindowFrame moved) return r.Stage == AnniversaryQingrenStage.DrawChildren && r.Draw is { } outcome &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private bool AnniversaryQingrenObserverSuffix(int root) => HasObserverSuffix(root,
        static (e, i) => e.IsMovementObserverEdge(i) || NativeDrawAttemptRidesOn(e._resolutionStack[i], e._resolutionStack[i - 1]) || e.RecoveryReplacementFrameRidesOn(e._resolutionStack[i], e._resolutionStack[i - 1]),
        static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? AnniversaryQingrenObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.AnniversaryQingrenFirstChild(f, child), static (e, i) => e.AnniversaryQingrenObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 44600)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 44600)]
    private bool AnniversaryQingrenDamageObserver(long id) => AnniversaryQingrenObserverRoot() is { } root &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 44600)]
    private bool AnniversaryQingrenBeforeDamageObserver(long id) => AnniversaryQingrenObserverRoot() is { } root &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 44600)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 44600)]
    private bool AnniversaryQingrenDyingObserver() => ActiveDying is { } dying && AnniversaryQingrenObserverRoot() is { } root &&
        _resolutionStack.FindIndex(x => x.Id == dying.Id) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 44600)]
    private bool AdvanceAnniversaryQingrenChildren() => _pendingDecision is null && AnniversaryQingrenObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
