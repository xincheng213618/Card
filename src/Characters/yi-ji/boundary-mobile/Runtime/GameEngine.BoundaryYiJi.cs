namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryYiJiJijie = "boundary:jijie-mobile";
    private const string BoundaryYiJiJiyuan = "boundary:jiyuan-mobile";

    // Give producers already record their actual recipient and physical batch.
    // Watching hand loss would also count cards taken away and miss Jijie's deck source.
    private bool HasBoundaryYiJiNativeGives => _contentRegistry.Skills.ContainsKey(BoundaryYiJiJiyuan);

    private static bool IsBoundaryYiJiGiveCandidate(ProgramTriggerCandidate candidate) =>
        candidate.SkillId == BoundaryYiJiJiyuan && candidate.BindingId == "native-give-draw";

    private IEnumerable<ProgramTriggerCandidate> CollectBoundaryYiJiGiveCandidates(CardMovementBatchContext batch)
    {
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsMoved)
                     .Where(IsBoundaryYiJiGiveCandidate))
        foreach (var gift in MouHqGiftsForBatch(batch, owner.Seat))
        {
            if (gift.Recipient == owner.Seat || !_players[gift.Recipient].IsAlive) continue;
            yield return candidate with
            {
                OccurrenceIndex = Array.FindIndex(batch.Movements.ToArray(),
                    movement => movement.Sequence == gift.CompletionSequence)
            };
        }
    }

    private ProgramSkillWindowContext CreateBoundaryYiJiGiveContext(
        CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate candidate)
    {
        if (!IsBoundaryYiJiGiveCandidate(candidate) || candidate.OccurrenceIndex < 0 ||
            candidate.OccurrenceIndex >= frame.Batch.Movements.Count)
            throw new InvalidOperationException("Jiyuan lost its actual native Give occurrence.");
        var gift = MouHqGiftsForBatch(frame.Batch, candidate.OwnerSeat).Single(gift =>
            gift.CompletionSequence == frame.Batch.Movements[candidate.OccurrenceIndex].Sequence);
        return new(SkillProgramTriggerWindow.CardsMoved, frame.Id, candidate.OwnerSeat,
            SourceSeat: gift.Giver, TargetSeat: gift.Recipient, Amount: gift.Movements.Count,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]),
            MovementBatch: frame.Batch, MovementIndex: candidate.OccurrenceIndex);
    }

    private bool IsCurrentBoundaryYiJiGive(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        IsBoundaryYiJiGiveCandidate(candidate) && context.MovementBatch is { } batch &&
        context.Window == SkillProgramTriggerWindow.CardsMoved && context.SourceSeat == candidate.OwnerSeat &&
        context.MovementIndex == candidate.OccurrenceIndex && context.OccurrenceIndex == candidate.OccurrenceIndex &&
        candidate.OccurrenceIndex >= 0 && candidate.OccurrenceIndex < batch.Movements.Count &&
        context.TargetSeat is { } recipient && recipient != candidate.OwnerSeat &&
        IsValidPlayerSeat(recipient) && _players[recipient].IsAlive &&
        MouHqGiftsForBatch(batch, candidate.OwnerSeat).Any(gift => gift.Recipient == recipient &&
            gift.Movements.Count == context.Amount &&
            gift.CompletionSequence == batch.Movements[candidate.OccurrenceIndex].Sequence);

    // No cards move and no child can run during this private target decision.
    // Its accepted command prefix therefore reconstructs the same actual bottom card.
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 51000)]
    private CardSnapshot[] BoundaryYiJiPrivateBottomCard(int viewer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame
            { SkillId: BoundaryYiJiJijie, ActivationId: "view-and-give", TriggerId: null,
                InstructionIndex: 1, SelectedTargetSeats.Count: 0 } frame || frame.OwnerSeat != viewer ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != viewer || prompt.SkillPrompt?.SkillId != BoundaryYiJiJijie ||
            prompt.Choices.Count == 0 || prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") != "select-target" ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            return [];
        var pile = _cardZones.CardsAt(CardLocation.DrawPile);
        return pile.Count == 0 ? [] : [ToSnapshot(pile[0])];
    }
}
