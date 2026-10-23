namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WangChangOriginal(ProgramSkillFrame f, SkillProgramEffectOp operation)
    {
        if (operation == SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji)
            return f.SkillId == AnniversaryWangChangComposition.Kaiji && f.ActivationId == "convert" && f.TriggerId is null && f.WindowContext is null && f.OwnerSeat == _currentSeat && _phase == TurnPhase.Play;
        if (operation != SkillProgramEffectOp.ResolveAnniversaryWangChangPingxi || f.SkillId != AnniversaryWangChangComposition.Pingxi || f.ActivationId != "ending-discard-and-slashes" || f.TriggerId != f.ActivationId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } c || c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1] is TurnEndingBoundaryFrame p && p.Id == c.ParentFrameId && p.OwnerSeat == f.OwnerSeat && p.OwnerSeat == _currentSeat && p.TurnNumber == _turnNumber &&
            p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate) &&
            c.OccurrenceIndex == candidate.OccurrenceIndex && c.Facts == (p.Items[p.ItemIndex].Facts ?? p.Facts);
    }
    private bool ValidWangChang(ProgramSkillFrame f)
    {
        if (f.AnniversaryWangChang is not { } r || f.InstructionIndex != 1 || !WangChangOriginal(f, r.Operation) || r.Source != WangChangSource(f) || r.Hash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash || r.ParentId != (f.WindowContext?.ParentFrameId ?? 0) || r.Turn != _turnNumber || r.Phase != _cardUseDebitPhaseInstanceId || r.Maximum < 1 ||
            !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.Polarity) || f.SelectedCardIds.Count != 0 || r.Cards.Count > r.Maximum || r.Cards.Distinct().Count() != r.Cards.Count ||
            r.Targets.Count > r.Maximum || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(t => !IsValidPlayerSeat(t) || t == f.OwnerSeat) ||
            r.Cursor < 0 || r.Cursor > r.Targets.Count || r.SlashCursor < 0 || r.SlashCursor > r.Targets.Count || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Moves.Select(m => m.Sequence).Distinct().Count() != r.Moves.Count || r.Moves.Any(m => !_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After || m.Reason.Value != WangChangDiscardReason ||
                m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || m.To != CardLocation.DiscardPile && !IsRedirectedPaidDiscardMovement(m, m.From, new(WangChangDiscardReason))) ||
            ProgramEventHistory<AnniversaryWangChangStartedEvent>().Count(e => e == new AnniversaryWangChangStartedEvent(f.Id, r.Source, r.Hash, r.Operation, r.ParentId, r.Turn, r.Phase, r.Maximum, r.Polarity)) != 1) return false;
        if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryWangChangKaiji)
        {
            if (r.Targets.Count != 0 || r.TargetsSealed || r.Cursor != 0 || r.SlashCursor != 0 || f.SelectedTargetSeats.Count != 0 || r.UseId != 0 || r.ActionId != 0 || r.NativeReturned ||
                r.Stage is not (AnniversaryWangChangStage.OwnCards or AnniversaryWangChangStage.Drawing or AnniversaryWangChangStage.MovementChildren or AnniversaryWangChangStage.Completed)) return false;
            if (r.Stage == AnniversaryWangChangStage.OwnCards) return r.Polarity == SkillPolarity.Yin && f.ConversionPreviousPolarity is null && r.Draw is null && r.Moves.Count == 0;
            if (f.ConversionPreviousPolarity != r.Polarity) return false;
            if (r.Polarity == SkillPolarity.Yang)
                return r.Cards.Count == 0 && r.Moves.Count == 0 && (r.Stage == AnniversaryWangChangStage.Drawing ? r.Draw is null : r.Draw is { } d && d.RecipientSeat == f.OwnerSeat && d.RequestedCount == r.Maximum && d.SequenceBefore == r.Before && d.SequenceAfter == r.After &&
                    ProgramEventHistory<AnniversaryWangChangDrawReturnedEvent>().Count(e => e == new AnniversaryWangChangDrawReturnedEvent(f.Id, d.ActualDrawIds.Count, r.Before, r.After)) == 1);
            return r.Cards.Count >= 1 && r.Draw is null && r.Moves.All(m => m.From.OwnerSeat == f.OwnerSeat && r.Cards.Contains(m.CardId)) &&
                ProgramEventHistory<AnniversaryWangChangDiscardPaidEvent>().Count(e => e.FrameId == f.Id && e.Cursor == 0 && e.SubjectSeat == f.OwnerSeat && e.Count == r.Moves.Count && e.Before == r.Before && e.After == r.After) == 1;
        }
        if (r.Cards.Count != 0 || r.Draw is not null || f.ConversionPreviousPolarity is not null || r.Stage is AnniversaryWangChangStage.OwnCards or AnniversaryWangChangStage.Drawing) return false;
        if (ProgramEventHistory<AnniversaryWangChangTargetSelectedEvent>().Count(e => e.FrameId == f.Id) != r.Targets.Count ||
            r.Targets.Select((target, index) => new AnniversaryWangChangTargetSelectedEvent(f.Id, index, target)).Any(e => ProgramEventHistory<AnniversaryWangChangTargetSelectedEvent>().Count(x => x == e) != 1)) return false;
        var sealedCount = ProgramEventHistory<AnniversaryWangChangTargetsSealedEvent>().Count(e => e.FrameId == f.Id && e.Count == r.Targets.Count);
        if (!r.TargetsSealed) return r.Stage == AnniversaryWangChangStage.Targets && sealedCount == 0 && r.Cursor == 0 && r.SlashCursor == 0 && r.Moves.Count == 0 && f.SelectedTargetSeats.Count == 0;
        if (sealedCount != 1 || r.Stage == AnniversaryWangChangStage.Targets) return false;
        if (r.Stage is AnniversaryWangChangStage.IssuingSlash or AnniversaryWangChangStage.NativeSlash or AnniversaryWangChangStage.ReturnedSlash)
        {
            if (r.Cursor != r.Targets.Count) return false;
            if (r.Stage == AnniversaryWangChangStage.NativeSlash)
                return r.SlashCursor < r.Targets.Count && f.SelectedTargetSeats.Count == 0 && r.UseId > 0 && r.ActionId > 0 &&
                    ProgramEventHistory<AnniversaryWangChangSlashIssuedEvent>().Count(e => e == new AnniversaryWangChangSlashIssuedEvent(f.Id, r.SlashCursor, r.UseId, r.ActionId, r.Targets[r.SlashCursor])) == 1;
            if (r.Stage == AnniversaryWangChangStage.IssuingSlash) return r.SlashCursor < r.Targets.Count && f.SelectedTargetSeats.Count == 0 && r.UseId == 0 && r.ActionId == 0 && !r.NativeReturned;
        }
        return f.SelectedTargetSeats.Count == 0;
    }
    private bool HasAnniversaryWangChangPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse, AnniversaryWangChang.Stage: AnniversaryWangChangStage.OwnCards or AnniversaryWangChangStage.Targets or AnniversaryWangChangStage.ForeignCard } f &&
        p.PlayerSeat == f.OwnerSeat && ValidWangChang(f) && AssistedChoicesEqual(p.Choices, WangChangChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 46500)] private void AssertWangChang(ProgramSkillFrame f)
    {
        if (f.AnniversaryWangChang is null) return;
        if (!ValidWangChang(f) || f.PendingMovementContinuation is { } p && !WangChangMovement(f, GetPausedPrivateOfferEffect(f), p)) throw new InvalidOperationException("Wang Chang lost his exact original phase, frozen target group, conversion payment or native child receipt.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.AnniversaryWangChang.Stage is AnniversaryWangChangStage.OwnCards or AnniversaryWangChangStage.Targets or AnniversaryWangChangStage.ForeignCard &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || !AssistedChoicesEqual(prompt.Choices, WangChangChoices(f)))) throw new InvalidOperationException("Wang Chang lost his exact private published menu.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 46500)] private bool WangChangMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.AnniversaryWangChang is { Stage: AnniversaryWangChangStage.MovementChildren } r && effect?.Op == r.Operation && f.PendingMovementContinuation == p && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidWangChang(f);
}
