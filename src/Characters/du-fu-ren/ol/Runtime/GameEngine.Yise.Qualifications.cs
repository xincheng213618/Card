namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasDuFuRenYise => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYiseOwnedGain) == true;
    private static bool IsDuFuRenYiseTrigger(SkillProgramTrigger t) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_du_fu_ren_ol_Yise_Qualifications.IsDuFuRenYiseTrigger(t);
    private void CaptureDuFuRenOwnedOrigin(CardMovementRecord m)
    {
        if (!HasDuFuRenYise || !IsDuFuRenOwnedZone(m.From) || m.From == m.To || !_cardMovements.Contains(m) ||
            ProgramEventHistory<DuFuRenOwnedOriginEvent>().Any(p => p.MovementSequence == m.Sequence)) return;
        AdvanceEventRulesAndQueueFact(new DuFuRenOwnedOriginEvent(m.Sequence, m.From.OwnerSeat!.Value,
            GetProgramEffectiveSuit(_players[m.From.OwnerSeat.Value], GetAdvancedCard(m.CardId))));
        if (m.To == CardLocation.Processing && _resolutionStack.LastOrDefault() is CardUseFrame { Action: { } a } use &&
            a.ActorSeat == use.SourceSeat && a.ActorSeat != m.From.OwnerSeat && a.EffectiveKind == CardKind.Snatch &&
            a.EffectiveDesignatedTargetSeats.Contains(m.From.OwnerSeat.Value) && m.Reason == CardMoveReasons.Snatch)
            RecordDuFuRenOwnedTransferProducer(m, a.ActorSeat, use.Id);
        if (m.To == CardLocation.Processing && _resolutionStack.LastOrDefault() is ProgramSkillFrame program &&
            GetPausedPrivateOfferEffect(program) is { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard,
                Destination: SkillProgramCardDestination.SelectedTargetHand, CardOwnerRef: { } cardOwner, TargetReference: { } recipient } effect &&
            effect.Zones.Contains(m.From.Zone) && ResolveProgramParticipant(program,cardOwner) == m.From.OwnerSeat &&
            m.Reason.Value == $"skill-program.{program.SkillId}.{effect.Op}")
            RecordDuFuRenOwnedTransferProducer(m,ResolveProgramParticipant(program,recipient),program.Id);
    }
    private void RecordDuFuRenOwnedTransferProducer(CardMovementRecord m, int recipientSeat, long producerId)
    {
        if (!HasDuFuRenYise || !IsDuFuRenOwnedZone(m.From) || m.To != CardLocation.Processing || m.From.OwnerSeat == recipientSeat ||
            !IsValidPlayerSeat(recipientSeat) || producerId <= 0 || !_cardMovements.Contains(m) ||
            ProgramEventHistory<DuFuRenTransferProducerEvent>().Any(p => p.EntrySequence == m.Sequence)) return;
        AdvanceEventRulesAndQueueFact(new DuFuRenTransferProducerEvent(m.Sequence, producerId, m.From.OwnerSeat!.Value, recipientSeat));
    }
    private CardMovementBatchContext CaptureDuFuRenGainBatch(CardMovementBatchContext batch)
    {
        if (!HasDuFuRenYise) return batch;
        var gains = new List<DuFuRenGain>();
        foreach (var m in batch.Movements.Where(m => m.To is { Zone: CardZoneKind.Hand, OwnerSeat: not null }))
        {
            CardMovementRecord? entry = IsDuFuRenOwnedZone(m.From) ? m : null;
            if (entry is null && m.From == CardLocation.Processing)
            {
                var previous = _cardMovements.LastOrDefault(p => p.CardId == m.CardId && p.Sequence < m.Sequence);
                if (previous is { To.Zone: CardZoneKind.Processing } && IsDuFuRenOwnedZone(previous.From) &&
                    ProgramEventHistory<DuFuRenTransferProducerEvent>().Any(p => p.EntrySequence == previous.Sequence &&
                        p.OwnerSeat == previous.From.OwnerSeat && p.RecipientSeat == m.To.OwnerSeat)) entry = previous;
            }
            if (entry is null || entry.From.OwnerSeat is not { } owner || m.To.OwnerSeat == owner) continue;
            var origin = ProgramEventHistory<DuFuRenOwnedOriginEvent>().SingleOrDefault(p => p.MovementSequence == entry.Sequence && p.OwnerSeat == owner);
            if (origin is null) continue;
            gains.Add(new(batch.Id, owner, m.To.OwnerSeat!.Value, m.CardId, entry.Sequence, m.Sequence, entry.From, origin.Suit));
        }
        return gains.Count == 0 ? batch : batch with { DuFuRenGains = gains };
    }
    private IEnumerable<ProgramTriggerCandidate> CollectDuFuRenYiseCandidates(CardMovementBatchContext batch)
    {
        if (batch.DuFuRenGains is not { Count: > 0 } gains) return [];
        return gains.GroupBy(g => (g.OwnerSeat, g.RecipientSeat)).Where(g => _players[g.Key.OwnerSeat].IsAlive && _players[g.Key.RecipientSeat].IsAlive)
            .SelectMany(g => CollectProgramTriggerCandidates(_players[g.Key.OwnerSeat], SkillProgramTriggerWindow.CardsMoved)
                .Where(c => IsDuFuRenYiseTrigger(GetProgramTrigger(c))).Select(c => c with
                { OccurrenceIndex = batch.Movements.ToList().FindIndex(m => m.Sequence == g.Min(g => g.GainSequence)) })).ToArray();
    }
    private ProgramSkillWindowContext CreateDuFuRenYiseContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate c)
    {
        var movement = frame.Batch.Movements[c.OccurrenceIndex];
        var gains = frame.Batch.DuFuRenGains!.Where(g => g.OwnerSeat == c.OwnerSeat && g.RecipientSeat == movement.To.OwnerSeat).ToArray();
        return new(SkillProgramTriggerWindow.CardsMoved, frame.Id, c.OwnerSeat, SourceSeat: c.OwnerSeat, TargetSeat: movement.To.OwnerSeat,
            OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]) with { MovedCardCount = gains.Length },
            MovementBatch: frame.Batch, MovementIndex: c.OccurrenceIndex);
    }
    private IReadOnlyList<DuFuRenGain>? CurrentDuFuRenGains(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsDuFuRenYiseTrigger(GetProgramTrigger(c)) || context.Window != SkillProgramTriggerWindow.CardsMoved || context.OwnerSeat != c.OwnerSeat ||
            context.SourceSeat != c.OwnerSeat || context.OccurrenceIndex != c.OccurrenceIndex || context.MovementIndex != c.OccurrenceIndex ||
            context.TargetSeat is not { } target || target == c.OwnerSeat || context.MovementBatch is not { DuFuRenGains: { } gains } batch ||
            batch.Id != context.ParentFrameId || c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c ||
            !parent.Batch.Movements.SequenceEqual(batch.Movements) || parent.Batch.DuFuRenGains is not { } parentGains || !parentGains.SequenceEqual(gains)) return null;
        var selected = gains.Where(g => g.OwnerSeat == c.OwnerSeat && g.RecipientSeat == target).ToArray();
        if (selected.Length == 0 || batch.Movements[c.OccurrenceIndex].Sequence != selected.Min(g => g.GainSequence) ||
            selected.Any(g => g.BatchId != batch.Id || !_cardMovements.Any(m => m.Sequence == g.EntrySequence && m.CardId == g.CardId && m.From == g.From) ||
                !batch.Movements.Any(m => m.Sequence == g.GainSequence && m.CardId == g.CardId && m.To == CardLocation.Hand(target)))) return null;
        return Array.AsReadOnly(selected);
    }
}
