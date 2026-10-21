namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsBiriTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveBiriFirstNonDrawGain }];
    private void CaptureBiriPotentialGainBatch(CardMovementBatchContext batch)
    {
        if (!HasZhaoZhongCapability || !ZhaoZhongActualTurnOpen || batch.TurnNumber != _turnNumber) return;
        var gains = batch.Movements.Where(m => m.To.Zone == CardZoneKind.Hand && m.To.OwnerSeat is { } owner && IsValidPlayerSeat(owner) && ActualHandGainOriginalSource(m, batch) != CardLocation.Hand(owner))
            .GroupBy(m => (m.CardId, m.To.OwnerSeat)).Select(g => g.OrderBy(m => m.Sequence).Last()).OrderBy(m => m.Sequence).ToArray();
        if (gains.Length == 0) return;
        foreach (var m in gains) AdvanceEventRulesAndQueueFact(new BiriPotentialGainEvent(batch.Id, m.Sequence, m.CardId, m.To.OwnerSeat!.Value, batch.TurnNumber, _turnProgression.OwnerSeat));
        foreach (var owner in _players.Where(p => p.IsAlive).OrderBy(p => RelativeTableOrder(_turnProgression.OwnerSeat, p.Seat)))
        {
            var source = GetSkillBindingShard(owner).ProgramInstances.SelectMany(i => i.Program.Triggers.Where(IsBiriTrigger).Select(t => (Source: new CardConversionSource(i.SkillId, t.Id, owner.Seat, i.SkillInstanceId), Hash: i.Program.GameplayHash)))
                .OrderBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            if (source.Source is not null) AdvanceEventRulesAndQueueFact(new BiriPotentialHolderEvent(batch.Id, source.Source, source.Hash));
        }
    }
    private BiriPotentialGainEvent? FirstBiriGain(int turn, int owner) => ProgramEventHistory<BiriPotentialGainEvent>().Where(e => e.ActualTurn == turn && e.TurnOwner == owner).OrderBy(e => e.Sequence).FirstOrDefault(e =>
        _cardMovements.SingleOrDefault(m => m.Sequence == e.Sequence) is { } m && m.CardId == e.CardId && m.To == CardLocation.Hand(e.RecipientSeat) && !IsNativeDrawGainMovement(m));
    private IReadOnlyList<ProgramTriggerCandidate> CollectBiriMovementCandidates(CardMovementBatchContext batch)
    {
        var potential = ProgramEventHistory<BiriPotentialGainEvent>().FirstOrDefault(e => e.BatchId == batch.Id);
        if (potential is null || FirstBiriGain(potential.ActualTurn, potential.TurnOwner) is not { } first || first.BatchId != batch.Id) return [];
        return Array.AsReadOnly(ProgramEventHistory<BiriPotentialHolderEvent>().Where(e => e.BatchId == batch.Id && _players[e.Source.OwnerSeat].IsAlive &&
            !ProgramEventHistory<BiriDrawIssuedEvent>().Any(p => p.Source.OwnerSeat == e.Source.OwnerSeat && p.ActualTurn == first.ActualTurn && p.TurnOwner == first.TurnOwner))
            .Select(e => new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId, e.Source.SkillInstanceId, e.GameplayHash, GetProgramTrigger(new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId, e.Source.SkillInstanceId, e.GameplayHash, 0)).Priority)).ToArray());
    }
    private ProgramSkillWindowContext CreateBiriMovementContext(CardsMovedTriggerWindowFrame w, ProgramTriggerCandidate c) => new(SkillProgramTriggerWindow.CardsGained, w.Id, c.OwnerSeat, SourceSeat: w.Batch.OriginOwnerSeat, TargetSeat: c.OwnerSeat, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: w.Batch, OccurrenceIndex: c.OccurrenceIndex);
    private bool IsIssuedBiriCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsBiriTrigger(GetProgramTrigger(c)) || context.Window != SkillProgramTriggerWindow.CardsGained || context.OwnerSeat != c.OwnerSeat || context.MovementBatch is not { } batch || context.ParentFrameId != batch.Id || !_players[c.OwnerSeat].IsAlive ||
            _resolutionStack.SingleOrDefault(x => x.Id == batch.Id) is not CardsMovedTriggerWindowFrame w || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex] != c ||
            !ProgramEventHistory<BiriPotentialHolderEvent>().Any(e => e.BatchId == batch.Id && e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && e.GameplayHash == c.GameplayHash)) return false;
        var potential = ProgramEventHistory<BiriPotentialGainEvent>().FirstOrDefault(e => e.BatchId == batch.Id);
        return potential is not null && FirstBiriGain(potential.ActualTurn, potential.TurnOwner) is { } first && first.BatchId == batch.Id &&
            !ProgramEventHistory<BiriDrawIssuedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.ActualTurn == first.ActualTurn && e.TurnOwner == first.TurnOwner);
    }
}
