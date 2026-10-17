namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMoQuanyuRoot(ProgramSkillFrame f)
    {
        if (f.MoQuanyu is not { } r || f.SkillId != "ol:mo-quanyu" || f.InstructionIndex != 1 ||
            r.Source != MoQuanyuSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.RoundNumber <= 0 || r.RoundNumber > _roundNumber || r.ActualTurn < 1 || r.ActualTurn > _turnNumber ||
            !IsValidPlayerSeat(r.TurnOwner) || !Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Stage) ||
            r.Participants.Count == 0 || r.Participants is not System.Collections.IList { IsReadOnly: true } ||
            r.Votes is not System.Collections.IList { IsReadOnly: true } ||
            ProgramEventHistory<MoQuanyuChoicesStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new MoQuanyuChoicesStartedEvent(f.Id, r.Source, r.GameplayHash, r.Kind, r.RoundNumber,
                r.ActualTurn, r.TurnOwner, r.OwnerChoiceBefore, r.Participants.Count) ||
            !ProgramEventHistory<MoQuanyuParticipantFrozenEvent>().Where(e => e.FrameId == f.Id)
                .Select(e => new MoQuanyuParticipant(e.Index, e.Seat, e.HistoryMask, e.AvailableMask)).SequenceEqual(r.Participants)) return false;
        if (r.Kind == MoQuanyuInvocationKind.Round)
        {
            return r.TargetRequest is null && f.TriggerId == "round-choices" && f.ActivationId == f.TriggerId &&
                f.WindowContext is { Window: SkillProgramTriggerWindow.RoundStarting } context &&
                context.OwnerSeat == f.OwnerSeat && context.SourceSeat == r.TurnOwner &&
                _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame
                    { Window: SkillProgramTriggerWindow.RoundStarting, Continuation: ProgramLifecycleContinuation.RoundProgramsTurnStart } mother &&
                mother.OwnerSeat == r.TurnOwner && mother.CandidateIndex >= 0 && mother.CandidateIndex < mother.Candidates.Count &&
                MountObserverCandidateMatches(f, mother.Candidates[mother.CandidateIndex]) &&
                _resolutionStack.FindIndex(x => x.Id == f.Id) == _resolutionStack.FindIndex(x => x.Id == mother.Id) + 1 &&
                r.Participants.Count(p => p.Seat == f.OwnerSeat) == 1 && r.OwnerChoiceBefore == MoQuanyuEffect.None &&
                ProgramEventHistory<MoQuanyuChoicesStartedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat &&
                    e.RoundNumber == r.RoundNumber && e.Kind == MoQuanyuInvocationKind.Round) == 1;
        }
        if (r.TargetRequest is not { } request || f.TriggerId is not null || f.ActivationId != "target-only-choices" ||
            f.WindowContext is not null || r.Participants is not [var p] || p.Seat != request.TargetSeat ||
            r.Source != request.QuanyuSource || r.GameplayHash != request.QuanyuGameplayHash ||
            r.Source.SkillInstanceId != "issued:tianen:" + request.ParentFrameId ||
            r.RoundNumber != request.RoundNumber || r.ActualTurn != request.ActualTurn || r.TurnOwner != request.TurnOwner ||
            ProgramEventHistory<MoQuanyuTargetRequestIssuedEvent>().Where(e => e.ChildFrameId == f.Id).ToArray() is not [var issued] ||
            issued.Request != request || _resolutionStack.SingleOrDefault(x => x.Id == request.ParentFrameId) is not ProgramSkillFrame parent)
            return false;
        return IsMoSunQuanTianenQuanyuRequest(parent, request) &&
            _resolutionStack.FindIndex(x => x.Id == f.Id) == _resolutionStack.FindIndex(x => x.Id == parent.Id) + 1;
    }

    private bool ValidMoQuanyuReceipt(ProgramSkillFrame f)
    {
        if (f.MoQuanyu is not { } r || !ValidMoQuanyuRoot(f) || r.Participants.Select(p => p.Seat).Distinct().Count() != r.Participants.Count ||
            r.Votes.Count > r.Participants.Count || r.Participants.Where((p, i) => p.Index != i || !IsValidPlayerSeat(p.Seat) ||
                p.HistoryMask is < 0 or > MoQuanyuAllEffects || p.AvailableMask != (MoQuanyuAllEffects & ~p.HistoryMask) ||
                ProgramEventHistory<MoQuanyuChoiceRevealedEvent>().Where(e => e.FrameId < f.Id && e.IssuerSeat == f.OwnerSeat && e.ParticipantSeat == p.Seat)
                    .Aggregate(0, (mask, e) => mask | MoQuanyuBit(e.Effect)) != p.HistoryMask).Any() ||
            r.Votes.Where((v, i) => v.Seat != r.Participants[i].Seat || !Enum.IsDefined(v.Effect) ||
                (v.Effect == MoQuanyuEffect.None ? r.Participants[i].AvailableMask != 0 && _players[v.Seat].IsAlive :
                    (r.Participants[i].AvailableMask & MoQuanyuBit(v.Effect)) == 0)).Any()) return false;
        var revealed = ProgramEventHistory<MoQuanyuChoiceRevealedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var finish = ProgramEventHistory<MoQuanyuRevealCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var requests = ProgramEventHistory<MoQuanyuDrawRequestedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var returns = ProgramEventHistory<MoQuanyuDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == MoQuanyuStage.Choosing)
            return revealed.Length == 0 && finish.Length == 0 && requests.Length == 0 && returns.Length == 0 && r.DrawCount == 0 &&
                r.DrawOutcome is null && f.NativeDraw is null && f.PendingMovementContinuation is null;
        if (r.Votes.Count != r.Participants.Count || revealed.Length != r.Participants.Count ||
            revealed.Where((e, i) => e != new MoQuanyuChoiceRevealedEvent(f.Id, i, f.OwnerSeat, r.Participants[i].Seat,
                r.RoundNumber, r.Votes[i].Effect, r.Participants[i].HistoryMask, r.Participants[i].HistoryMask | MoQuanyuBit(r.Votes[i].Effect))).Any()) return false;
        var ownerChoice = r.Kind == MoQuanyuInvocationKind.Round ? r.Votes.Single(v => v.Seat == f.OwnerSeat).Effect : r.OwnerChoiceBefore;
        var count = ownerChoice == MoQuanyuEffect.None ? 0 : r.Votes.Count(v => v.Effect == ownerChoice);
        if (finish is not [var completed] || completed != new MoQuanyuRevealCompletedEvent(f.Id, ownerChoice, count, Math.Min(3, count)) ||
            r.DrawCount != Math.Min(3, count) || requests.Length > 1 ||
            requests.Any(e => e.RecipientSeat != f.OwnerSeat || e.RequestedCount != r.DrawCount)) return false;
        if (r.Stage == MoQuanyuStage.Drawing)
            return r.DrawOutcome is null && returns.Length == 0 && f.PendingMovementContinuation is null;
        return r.DrawOutcome is { } o && o.RecipientSeat == f.OwnerSeat && o.RequestedCount == r.DrawCount && r.DrawCount > 0 &&
            o.SequenceBefore >= 0 && o.SequenceAfter >= o.SequenceBefore && o.SequenceAfter <= _movementSequence &&
            requests.Length == 1 && returns is [var returned] && returned == new MoQuanyuDrawReturnedEvent(f.Id,
                r.DrawCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId) &&
            f.NativeDraw is null && f.PendingMovementContinuation is { } pending && IsMoQuanyuMovement(f, GetPausedPrivateOfferEffect(f), pending);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28910)]
    private void AssertMoQuanyu(ProgramSkillFrame f)
    {
        if (f.MoQuanyu is null) return;
        if (!ValidMoQuanyuReceipt(f)) throw new InvalidOperationException("Quanyu lost its exact round or paid Tianen root, frozen private choices, permanent history or native draw return.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Step == ResolutionFrameStep.AwaitingResponse &&
            f.MoQuanyu.Stage == MoQuanyuStage.Choosing && (_pendingDecision is not { IsPrivate: true } prompt ||
                f.MoQuanyu.Votes.Count >= f.MoQuanyu.Participants.Count || prompt.PlayerSeat != f.MoQuanyu.Participants[f.MoQuanyu.Votes.Count].Seat ||
                !AssistedChoicesEqual(prompt.Choices, MoQuanyuChoices(f))))
            throw new InvalidOperationException("Quanyu's private chooser cannot receive another participant's choice or a stale option set.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28910)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28910)]
    private bool CanContinueIssuedMoQuanyu(ProgramSkillFrame f) => f.MoQuanyu is not null && ValidMoQuanyuReceipt(f);

    private bool MoQuanyuChildRidesOn(ResolutionFrame child, ProgramSkillFrame parent) => child is
        ProgramSkillFrame { MoQuanyu: { Kind: MoQuanyuInvocationKind.TargetOnly, TargetRequest: { } request } } f &&
        request.ParentFrameId == parent.Id && IsMoSunQuanTianenQuanyuRequest(parent, request) && ValidMoQuanyuReceipt(f);
}
