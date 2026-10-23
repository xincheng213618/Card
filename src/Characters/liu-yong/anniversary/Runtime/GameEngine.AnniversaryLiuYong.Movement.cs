namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasAnniversaryLiuYong => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang);
    private static CardConversionSource AnniversaryLiuYongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int? AnniversaryLiuYongLeader(int owner, CardMovementBatchContext? beforeBatch = null)
    {
        var counts = _players.Select(p =>
        {
            var hand = GetHand(p).Select(c => c.Id).ToHashSet();
            if (beforeBatch is not null) foreach (var move in beforeBatch.Movements.Reverse())
            { if (move.To == CardLocation.Hand(p.Seat)) hand.Remove(move.CardId); if (move.From == CardLocation.Hand(p.Seat)) hand.Add(move.CardId); }
            return (p.Seat, Count: p.AnniversaryLiuYongXiCards.Count(mark => mark.Source.OwnerSeat == owner && hand.Contains(mark.CardId)));
        }).ToArray();
        var maximum = counts.Select(x => x.Count).DefaultIfEmpty(0).Max(); var leaders = counts.Where(x => x.Count == maximum).ToArray();
        return maximum > 0 && leaders is [var sole] ? sole.Seat : null;
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 43400)]
    private void CaptureAnniversaryLiuYongMovement(CardMovementBatchContext batch)
    {
        if (!HasAnniversaryLiuYong) return;
        var changed = batch.SourceCounts.Concat(batch.DestinationCounts ?? []).Any(c => c.Location.Zone == CardZoneKind.Hand && c.CountBefore != c.CountAfter);
        var before = _players.Where(p => p.IsAlive && HasProgramSkill(p, AnniversaryLiuYongComposition.Fengxiang)).ToDictionary(p => p.Seat, p => AnniversaryLiuYongLeader(p.Seat, batch));
        // A Xi belongs only to its original receiver's hand. A transfer or use
        // consumes it, even when a later movement returns that entity.
        foreach (var p in _players) p.AnniversaryLiuYongXiCards = p.AnniversaryLiuYongXiCards.Where(mark => !batch.Movements.Any(m => m.CardId == mark.CardId && m.From == CardLocation.Hand(mark.RecipientSeat) && m.To != m.From)).ToArray();
        if (!changed) return;
        foreach (var p in _players.Where(p => before.ContainsKey(p.Seat)))
        {
            var current = AnniversaryLiuYongLeader(p.Seat);
            if (before[p.Seat] == current) continue;
            var source = new CardConversionSource(AnniversaryLiuYongComposition.Fengxiang, "xi-leader-changes", p.Seat, GetRuntimeSkillInstanceId(p, AnniversaryLiuYongComposition.Fengxiang));
            AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongLeaderChangedEvent(batch.Id, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, before[p.Seat], current));
        }
    }
    private void AppendAnniversaryLiuYongMovementCandidates(CardMovementBatchContext batch, List<ProgramTriggerCandidate> candidates)
    {
        foreach (var change in ProgramEventHistory<AnniversaryLiuYongLeaderChangedEvent>().Where(e => e.BatchId == batch.Id))
        {
            var source = change.Source; var program = _contentRegistry.GetSkill(source.SkillId).Program!; var trigger = program.Triggers.Single(t => t.Id == source.BindingId);
            var candidate = new ProgramTriggerCandidate(source.OwnerSeat, source.SkillId, source.BindingId, source.SkillInstanceId, change.Hash, trigger.Priority);
            if (!candidates.Contains(candidate)) candidates.Add(candidate);
        }
    }
    private ProgramSkillWindowContext CreateAnniversaryLiuYongMovementContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate candidate)
    {
        if (candidate.SkillId != AnniversaryLiuYongComposition.Fengxiang || candidate.BindingId != "xi-leader-changes" ||
            !frame.Candidates.Contains(candidate)) throw new InvalidOperationException("Fengxiang requires its exact full-batch movement candidate.");
        if (frame.Contexts is { } frozen)
        {
            var index = Enumerable.Range(0, frame.Candidates.Count).Single(i => frame.Candidates[i] == candidate);
            return frozen[index];
        }
        // The moving hand can belong to another participant. Preserve that
        // batch's actual owner/count invoices instead of inventing a source
        // hand for the skill owner, then freeze the owner's observer facts.
        var facts = CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with
        {
            MovedCardCount = frame.Batch.Movements.Count(m => m.From != m.To &&
                (m.From.Zone == CardZoneKind.Hand || m.To.Zone == CardZoneKind.Hand)),
            MovementTiming = frame.Batch.MovementTiming
        };
        return new(SkillProgramTriggerWindow.CardsMoved, frame.Id, candidate.OwnerSeat,
            SourceSeat: frame.Batch.OriginOwnerSeat, TargetSeat: candidate.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex, Facts: facts, MovementBatch: frame.Batch);
    }
    private bool IsAnniversaryLiuYongMovementCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => candidate.SkillId == AnniversaryLiuYongComposition.Fengxiang && candidate.BindingId == "xi-leader-changes" &&
        context.Window == SkillProgramTriggerWindow.CardsMoved && context.OwnerSeat == candidate.OwnerSeat && context.TargetSeat == candidate.OwnerSeat &&
        context.MovementBatch is { } batch && batch.Id == context.ParentFrameId && context.SourceSeat == batch.OriginOwnerSeat && context.OccurrenceIndex == candidate.OccurrenceIndex && context.MovementIndex is null &&
        _players[candidate.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId) && _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash == candidate.GameplayHash &&
        _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId) is { } parent && parent.Batch.Id == batch.Id &&
        parent.Batch.Movements.SequenceEqual(batch.Movements) && parent.Batch.SourceCounts.SequenceEqual(batch.SourceCounts) &&
        (parent.Batch.DestinationCounts ?? []).SequenceEqual(batch.DestinationCounts ?? []) &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == candidate &&
        batch.SourceCounts.Concat(batch.DestinationCounts ?? []).Any(c => c.Location.Zone == CardZoneKind.Hand && c.CountBefore != c.CountAfter) &&
        ProgramEventHistory<AnniversaryLiuYongLeaderChangedEvent>().Count(e => e.BatchId == batch.Id && e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId && e.Source.SkillInstanceId == candidate.SkillInstanceId && e.Hash == candidate.GameplayHash && e.Previous != e.Current) == 1;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 43400, SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang)]
    private bool CanOfferAnniversaryLiuYongFengxiang(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        trigger.Id == "xi-leader-changes" ? IsAnniversaryLiuYongMovementCandidate(candidate, context) : context.TargetSeat == candidate.OwnerSeat && context.Amount > 0;
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 43400)]
    private string? AnniversaryLiuYongXiLabel(int cardId) => _players.Any(p => p.AnniversaryLiuYongXiCards.Any(mark => mark.CardId == cardId) && _cardZones.GetLocation(cardId) == CardLocation.Hand(p.Seat)) ? "隙" : null;
}
