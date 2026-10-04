namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksNeighborDiscardOpportunities =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PutOwnOrPreviousFirstDiscardOnTop);

    private int PreviousLivingSeatFor(int owner)
    {
        for (var offset = 1; offset < _players.Count; offset++)
        {
            var seat = (owner - offset + _players.Count) % _players.Count;
            if (_players[seat].IsAlive) return seat;
        }
        return owner;
    }

    private static bool IsNeighborDiscardTopTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.PutOwnOrPreviousFirstDiscardOnTop);
    private IEnumerable<IGameEvent> NeighborDiscardHistory() => _events.Select(e => e.Payload).Concat(_pendingEvents);

    private void CaptureNeighborDiscardOpportunity(long batchId, int actualTurn,
        IReadOnlyList<CardMovementRecord> movements)
    {
        if (!TracksNeighborDiscardOpportunities || actualTurn <= 0) return;
        var owners = movements.Select(GetProgramDiscardSource).Where(location => location is
                { OwnerSeat: not null, Zone: CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment })
            .Select(location => location!.Value.OwnerSeat!.Value).Distinct().ToArray();
        if (owners.Length == 0) return;
        foreach (var source in owners)
            if (!NeighborDiscardHistory().OfType<ActualTurnFirstOwnedDiscardBatchEvent>().Any(e =>
                    e.ActualTurnNumber == actualTurn && e.DiscardOwnerSeat == source))
                AdvanceEventRulesAndQueueFact(new ActualTurnFirstOwnedDiscardBatchEvent(actualTurn, _turnProgression.OwnerSeat, source, batchId));
        // Capture every public living-seat relation, without enumerating skill
        // shards, hidden generals or private libraries. Firstness survives a
        // later source loss/acquisition and a refused optional opportunity.
        foreach (var owner in _players.Where(p => p.IsAlive))
        {
            var previous = PreviousLivingSeatFor(owner.Seat);
            var previousFirst = previous != owner.Seat && owners.Contains(previous) &&
                NeighborDiscardHistory().OfType<ActualTurnFirstOwnedDiscardBatchEvent>().Any(e =>
                    e.ActualTurnNumber == actualTurn && e.DiscardOwnerSeat == previous && e.BatchId == batchId);
            AdvanceEventRulesAndQueueFact(new DiscardNeighborhoodFrozenEvent(batchId, actualTurn, _turnProgression.OwnerSeat,
                owner.Seat, previous, owners.Contains(owner.Seat), previousFirst));
        }
    }

    private DiscardNeighborhoodFrozenEvent? NeighborDiscardFact(CardMovementBatchContext batch, int owner) =>
        NeighborDiscardHistory().OfType<DiscardNeighborhoodFrozenEvent>().SingleOrDefault(e =>
            e.BatchId == batch.Id && e.ActualTurnNumber == batch.TurnNumber && e.OwnerSeat == owner);

    private int[] NeighborDiscardIndexes(CardMovementBatchContext batch, int owner)
    {
        if (NeighborDiscardFact(batch, owner) is not { } fact) return [];
        return batch.Movements.Select((move, index) => (move, index)).Where(item =>
            GetProgramDiscardSource(item.move) is { OwnerSeat: { } source,
                Zone: CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment } &&
            (source == owner && fact.IncludesOwnDiscard || source == fact.PreviousLivingSeat && fact.IncludesFirstPreviousDiscard))
            .Select(item => item.index).ToArray();
    }

    private IEnumerable<ProgramTriggerCandidate> CollectNeighborDiscardCandidates(CardMovementBatchContext batch)
    {
        if (!TracksNeighborDiscardOpportunities) yield break;
        var seen = new HashSet<(int Owner, string Skill, string Binding, string Instance)>();
        foreach (var owner in _players.Where(p => p.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsMoved))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!IsNeighborDiscardTopTrigger(trigger) || NeighborDiscardIndexes(batch, owner.Seat).Length == 0 ||
                !seen.Add((candidate.OwnerSeat, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId))) continue;
            yield return candidate;
        }
    }

    private int? NeighborDiscardSourceSeat(CardMovementBatchContext batch, int owner)
    {
        var sources = NeighborDiscardIndexes(batch, owner).Select(index =>
            GetProgramDiscardSource(batch.Movements[index])!.Value.OwnerSeat!.Value).Distinct().ToArray();
        return sources.Length == 1 ? sources[0] : null;
    }

    private ProgramSkillWindowContext CreateNeighborDiscardContext(CardsMovedTriggerWindowFrame window,
        ProgramTriggerCandidate candidate)
    {
        var fact = NeighborDiscardFact(window.Batch, candidate.OwnerSeat) ??
            throw new InvalidOperationException("A neighbor discard candidate lost its frozen actual batch relation.");
        return new(SkillProgramTriggerWindow.CardsMoved, window.Id, candidate.OwnerSeat,
            SourceSeat: NeighborDiscardSourceSeat(window.Batch, candidate.OwnerSeat), TargetSeat: candidate.OwnerSeat,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with
                { MovedCardCount = NeighborDiscardIndexes(window.Batch, candidate.OwnerSeat).Length,
                  FrozenPreviousLivingSeat = fact.PreviousLivingSeat }, MovementBatch: window.Batch);
    }

    private bool IsProgramDiscardTopMovement(ProgramSkillFrame frame, CardMovementRecord move)
    {
        var paused = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (paused.Op != SkillProgramEffectOp.PutOwnOrPreviousFirstDiscardOnTop)
            return GetProgramDiscardSource(move)?.OwnerSeat == frame.OwnerSeat;
        if (frame.WindowContext?.MovementBatch is not { } batch || NeighborDiscardFact(batch, frame.OwnerSeat) is not { } fact ||
            frame.WindowContext.Facts?.FrozenPreviousLivingSeat != fact.PreviousLivingSeat) return false;
        return NeighborDiscardIndexes(batch, frame.OwnerSeat).Any(index => batch.Movements[index] == move);
    }

    private bool MatchesTurnEndingScope(ProgramTriggerCandidate candidate, int actualOwner)
    {
        var scope = GetProgramTrigger(candidate).TurnOwnerScope;
        if (scope == SkillProgramTurnOwnerScope.OwnOrPreviousLiving)
            return candidate.OwnerSeat == actualOwner || PreviousLivingSeatFor(candidate.OwnerSeat) == actualOwner;
        return scope == (candidate.OwnerSeat == actualOwner ? SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving);
    }

    private bool MatchesFrozenOwnOrPreviousEnding(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        context.SourceSeat == candidate.OwnerSeat || context.Facts?.FrozenPreviousLivingSeat == context.SourceSeat;
}
