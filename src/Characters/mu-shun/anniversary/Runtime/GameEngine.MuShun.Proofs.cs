namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramTriggerCandidate MuShunCandidate(ProgramSkillFrame f) => new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, f.WindowContext!.OccurrenceIndex);
    private bool MuShunLossCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c, bool issued)
    {
        if (!HasMuShunShizhaoCapability || candidate.SkillId != MuShunComposition.Shizhao || candidate.BindingId != MuShunComposition.Loss ||
            c is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch } || c.OwnerSeat != candidate.OwnerSeat || c.SourceSeat != candidate.OwnerSeat ||
            batch.TurnNumber != _turnNumber || batch.MovementTiming is not { } timing || timing.ActualTurnOwnerSeat == candidate.OwnerSeat || timing.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent || parent.Batch.Id != batch.Id ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != candidate ||
            !batch.SourceCounts.Any(n => n.Location == CardLocation.Hand(candidate.OwnerSeat) && n.CountBefore > 0 && n.CountAfter == 0)) return false;
        return issued || !ProgramEventHistory<MuShunShizhaoIssuedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.ActualTurn == batch.TurnNumber);
    }
    private bool ValidMuShunJinjian(ProgramSkillFrame f)
    {
        if (f.MuShunJinjian is not { } r || f.SkillId != MuShunComposition.Jinjian || f.TriggerId != MuShunComposition.Damage || f.ActivationId != f.TriggerId ||
            f.InstructionIndex is < 1 or > 2 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != MuShunSource(f) || r.Hash != f.GameplayHash ||
            !_contentRegistry.Skills.TryGetValue(f.SkillId, out var skill) || skill.Program?.GameplayHash != r.Hash || !Enum.IsDefined(r.Stage) ||
            r.ActualTurn != _turnNumber || r.MarkerBefore < 0 || r.MarkerAfter != r.MarkerBefore + 1 ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) || window.Id != r.WindowId || damage.Id != r.DamageFrameId ||
            r.DamageSourceSeat != (attack.IsSourceLess ? null : (int?)attack.SourceSeat) ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1 ||
            ProgramEventHistory<MuShunJinjianIssuedEvent>().Count(e => e == new MuShunJinjianIssuedEvent(f.Id, r.Source, r.Hash, r.WindowId, r.DamageFrameId, r.ActualTurn, r.DamageSourceSeat, r.MarkerBefore, r.MarkerAfter)) != 1 ||
            ProgramEventHistory<PlayerMarkerChangedEvent>().Count(e => e.ResolutionId == f.Id && e.PlayerSeat == f.OwnerSeat && e.Marker == PlayerMarkerKind.MuShunJin && e.Delta == 1 && e.Count == r.MarkerAfter && e.SkillOwnerSeat == f.OwnerSeat) != 1) return false;
        var issued = ProgramEventHistory<MuShunContestIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var closed = ProgramEventHistory<MuShunContestClosedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == MuShunContestStage.Choice) return f.InstructionIndex == 1 && r.ContestId == 0 && issued.Length == 0 && closed.Length == 0 && f.PindianResultBindings.Count == 0;
        if (r.Stage == MuShunContestStage.Declined) return r.ContestId == 0 && issued.Length == 0 && closed is [{ Accepted: false, ContestId: 0 }];
        if (r.ContestId <= f.Id || r.DamageSourceSeat is not { } opponent || opponent == f.OwnerSeat || issued is not [var started] ||
            started != new MuShunContestIssuedEvent(f.Id, r.ContestId, f.OwnerSeat, opponent, r.MovementBefore) || r.MovementBefore < 0 || r.MovementBefore > _movementSequence) return false;
        var results = f.PindianResultBindings.Where(b => b.Name == MuShunComposition.Result).ToArray();
        if (results.Length == 0) return r.Stage == MuShunContestStage.Contest && closed.Length == 0 &&
            _resolutionStack.OfType<PindianFrame>().Any(p => p.Id == r.ContestId && p.ParentFrameId == f.Id && p.SourceSeat == f.OwnerSeat && p.OpponentSeat == opponent && p.SkillId == f.SkillId && p.ProgramResultBind == MuShunComposition.Result);
        if (results is not [var result] || ProgramEventHistory<PindianResultDeterminedEvent>().Where(e => e.FrameId == r.ContestId && e.SkillId == f.SkillId).ToArray() is not [var fact] ||
            fact.Result.SourceSeat != f.OwnerSeat || fact.Result.OpponentSeat != opponent || result.SourceSeat != f.OwnerSeat || result.OpponentSeat != opponent ||
            fact.Result.SourceRank != result.SourceRank || fact.Result.OpponentRank != result.OpponentRank || fact.Result.SourceWon != result.SourceWon) return false;
        if (r.Stage == MuShunContestStage.Contest) return closed.Length == 0 && r.MovementAfter == 0;
        if (r.MovementAfter <= r.MovementBefore || r.MovementAfter > _movementSequence ||
            ProgramEventHistory<MuShunContestReturnedEvent>().Count(e => e == new MuShunContestReturnedEvent(f.Id, r.ContestId, r.MovementBefore, r.MovementAfter)) != 1) return false;
        return r.Stage == MuShunContestStage.ContestChildren ? closed.Length == 0 :
            r.Stage == MuShunContestStage.Returned && closed is [var done] && done == new MuShunContestClosedEvent(f.Id, r.ContestId, true, result.SourceWon);
    }
    private bool IsMuShunPindianSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext batch)
    {
        if (f.MuShunJinjian is not { Stage: MuShunContestStage.ContestChildren } r || !ValidMuShunJinjian(f) ||
            batch.ParentFrameId != r.ContestId || batch.AwaitingProgramFrameId is not null || batch.TurnNumber != r.ActualTurn ||
            batch.OriginOwnerSeat != f.OwnerSeat || batch.OriginSkillId != f.SkillId || batch.OriginSkillInstanceId != f.SkillInstanceId ||
            batch.Movements.Count is < 1 or > 2 || ProgramEventHistory<PindianResultDeterminedEvent>().Where(e => e.FrameId == r.ContestId && e.SkillId == f.SkillId).ToArray() is not [var fact]) return false;
        return batch.Movements.All(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter && _cardMovements.Count(actual => actual == m) == 1 &&
            (m.CardId == fact.Result.SourceCardId && m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal ||
             m.CardId == fact.Result.OpponentCardId && m.From == CardLocation.Hand(fact.Result.OpponentSeat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal ||
             (m.CardId == fact.Result.SourceCardId || m.CardId == fact.Result.OpponentCardId) && m.From == CardLocation.Processing &&
             (m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.PindianFinish || m.Reason.Value == "program.pindian.claim")));
    }
    private bool MuShunPindianStructuralEdge(ResolutionFrame child, ResolutionFrame parent) =>
        parent is ProgramSkillFrame f && child is CardsMovedTriggerWindowFrame moved &&
        moved.ResumeProgramFrameId == f.Id && IsMuShunPindianSettlementBatch(f, moved.Batch);
    private bool HasAnniversaryMuShunPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse, MuShunJinjian.Stage: MuShunContestStage.Choice } f &&
        p.PlayerSeat == f.OwnerSeat && ValidMuShunJinjian(f) && AssistedChoicesEqual(p.Choices, MuShunChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 45300)] private void AssertMuShun(ProgramSkillFrame f)
    {
        if (f.MuShunJinjian is not null && !ValidMuShunJinjian(f)) throw new InvalidOperationException("Jinjian lost its original damage, once-granted Jin or native Pindian return.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 45300)] private bool ContinueIssuedMuShun(ProgramSkillFrame f) => f.MuShunJinjian is not null ? ValidMuShunJinjian(f) :
        f.SkillId == MuShunComposition.Shizhao && f.WindowContext is { } c && MuShunLossCandidate(MuShunCandidate(f), c, true) &&
        ProgramEventHistory<MuShunShizhaoIssuedEvent>().Count(e => e.FrameId == f.Id && e.Source == MuShunSource(f) && e.Hash == f.GameplayHash && e.ParentWindowId == c.ParentFrameId && e.BatchId == c.MovementBatch!.Id && e.Draw) == 1;
}
