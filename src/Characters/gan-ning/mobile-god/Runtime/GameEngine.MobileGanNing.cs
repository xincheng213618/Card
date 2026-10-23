namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MobileJieying = "mobile-god:jieying", MobileCampClaimReason = "skill.mobile-god.jieying.ended-hand";
    [ProgramCapability] private sealed class MobileCampClaimHost(GameEngine engine) : IMobileCampClaimHost
    { public SkillProgramStepOutcome Claim(ProgramSkillFrame frame) => engine.ClaimMobileCampHand(frame); }
    private bool IsMobileCampClaimContext(ProgramSkillFrame frame) => frame.SkillId is MobileJieying or "happy:jieying" &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.AfterTurnEnded } context &&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId) is { } parent &&
        parent.OwnerSeat != frame.OwnerSeat && MatchesAfterTurnEndedChild(parent, frame, context);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39825)]
    private bool AllowsMobileCampClaim(ProgramTriggerLegalityContext gate) => gate.SkillId is not (MobileJieying or "happy:jieying") ||
        gate.WindowContext?.Window != SkillProgramTriggerWindow.AfterTurnEnded ||
        gate.WindowContext.SourceSeat is { } seat && seat != gate.OwnerSeat &&
        _players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Camp, gate.OwnerSeat)) > 0;
    private SkillProgramStepOutcome ClaimMobileCampHand(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!IsMobileCampClaimContext(frame) || frame.MobileCampClaim is not null || GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.ClaimMobileEndedCampHand)
            throw new InvalidOperationException("Mobile Jieying requires its actual completed-turn child and original instruction.");
        var seat = frame.WindowContext!.SourceSeat!.Value;
        if (_players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Camp, frame.OwnerSeat)) <= 0) return SkillProgramStepOutcome.Continue;
        // The mobile text removes Camp before the gain, and never returns it to the source.
        SetStrategicMarker(seat, PlayerMarkerKind.Camp, frame.OwnerSeat, 0, frame);
        var cards = GetHand(_players[seat]).Select(c => c.Id).ToArray();
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        var receipt = new MobileCampClaimReceipt(source, frame.GameplayHash, frame.InstructionIndex,
            frame.WindowContext.ParentFrameId, seat, _turnNumber, _movementSequence) { Cards = cards };
        ReplaceRuntimeTop(frame with { MobileCampClaim = receipt });
        void Complete(long batch, IReadOnlyList<CardMovementRecord> moves)
        {
            var live = GetActiveProgramFrame(frame.Id);
            var paid = receipt with { SequenceAfter = _movementSequence, BatchId = batch };
            ReplaceRuntimeTop(live with { MobileCampClaim = paid });
            AdvanceEventRulesAndQueueFact(new MobileCampClaimedEvent(frame.Id, source, receipt.EndedFrameId, seat,
                receipt.ActualTurn, cards.Length, receipt.SequenceBefore, paid.SequenceAfter, batch));
        }
        if (cards.Length == 0)
        { Complete(0, []); ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { MobileCampClaim = null }); return SkillProgramStepOutcome.Continue; }
        MoveProgramCardsFromMultipleSources(cards, CardLocation.Hand(frame.OwnerSeat), new(MobileCampClaimReason), Complete);
        var outcome = AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
        if (outcome == SkillProgramStepOutcome.Continue) ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { MobileCampClaim = null });
        return outcome;
    }
    private bool ValidMobileCampClaim(ProgramSkillFrame frame)
    {
        if (frame.MobileCampClaim is not { } r || r.Source != new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) ||
            r.GameplayHash != frame.GameplayHash || r.InstructionIndex != frame.InstructionIndex || r.EndedFrameId != frame.WindowContext?.ParentFrameId ||
            r.EndedSeat != frame.WindowContext?.SourceSeat || r.ActualTurn != _turnNumber || r.Cards.Count == 0 ||
            r.Cards.Distinct().Count() != r.Cards.Count || r.BatchId <= 0 || r.SequenceAfter <= r.SequenceBefore || !IsMobileCampClaimContext(frame)) return false;
        var moves = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        return moves.Length == r.Cards.Count && moves.Select(m => m.CardId).SequenceEqual(r.Cards) &&
            moves.All(m => m.From == CardLocation.Hand(r.EndedSeat) && m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == MobileCampClaimReason) &&
            ProgramEventHistory<PlayerMarkerChangedEvent>().Count(e => e.ResolutionId == frame.Id && e.PlayerSeat == r.EndedSeat && e.Marker == PlayerMarkerKind.Camp && e.Count == 0 && e.SkillOwnerSeat == frame.OwnerSeat) == 1 &&
            ProgramEventHistory<MobileCampClaimedEvent>().Count(e => e.ProgramFrameId == frame.Id && e.Source == r.Source && e.EndedFrameId == r.EndedFrameId &&
                e.EndedSeat == r.EndedSeat && e.ActualTurn == r.ActualTurn && e.Count == r.Cards.Count && e.BatchId == r.BatchId &&
                e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39825)]
    private void AssertMobileCampClaim(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.MobileCampClaim is null) return;
        if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count || plan.Instructions[frame.InstructionIndex - 1].Op != SkillProgramEffectOp.ClaimMobileEndedCampHand || !ValidMobileCampClaim(frame))
            throw new InvalidOperationException("Mobile Jieying lost its exact source, removed Camp, paid frozen hand or native completed-turn return.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 39825)]
    private bool IsMobileCampClaimMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ClaimMobileEndedCampHand && pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidMobileCampClaim(frame);
    [ProgramContinuation(ProgramContinuationKind.Movement, 39825)] private bool ReturnMobileCampClaim(ProgramSkillFrame frame)
    {
        if (frame.MobileCampClaim is null) return false;
        if (!ValidMobileCampClaim(frame)) throw new InvalidOperationException("Mobile Jieying lost its already-paid movement child.");
        ReplaceRuntimeTop(frame with { MobileCampClaim = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(frame.Id); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39825)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39825)]
    private bool RetainMobileCampClaim(ProgramSkillFrame frame) => frame.MobileCampClaim is not null;
}
