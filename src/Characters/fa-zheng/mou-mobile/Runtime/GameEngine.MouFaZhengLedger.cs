namespace CardGame.Core;

public sealed record MouFaZhengActualGainEvent(long MarkId, long BatchId, long Sequence, int SourceSeat, int RecipientSeat) : IGameEvent;
public sealed partial class GameEngine
{
    private bool HasMouFaZhengCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain);
    private static CardConversionSource MouFaZhengSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private IEnumerable<MouFaZhengMarkEvent> MouFaZhengMarks() => ProgramEventHistory<MouFaZhengMarkEvent>().Where(m => !ProgramEventHistory<MouFaZhengRemovedEvent>().Any(e => e.MarkId == m.MarkId));
    private int MouFaZhengExtracted(long mark) => ProgramEventHistory<MouFaZhengObtainedEvent>().Where(e => e.MarkId == mark).Sum(e => e.Actual);
    private int MouFaZhengReceived(long mark) => ProgramEventHistory<MouFaZhengActualGainEvent>().Count(e => e.MarkId == mark);
    private bool IsLiveMouFaZhengSource(MouFaZhengMarkEvent mark) => _players[mark.Source.OwnerSeat].IsAlive && _players[mark.TargetSeat].IsAlive &&
        HasRuntimeSkill(_players[mark.Source.OwnerSeat], MouFaZhengComposition.Xuanhuo) &&
        _contentRegistry.GetSkill(mark.Source.SkillId).Program?.GameplayHash == mark.GameplayHash;
    private static bool IsMouFaZhengGainTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain }];
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 31800)]
    private void SynchronizeMouFaZhengMarks()
    {
        if (!HasMouFaZhengCapability) return;
        var marks = MouFaZhengMarks().ToArray();
        foreach (var player in _players)
        {
            var kind = PlayerMarkerKind.MouFaZhengXuan; var previous = player.Markers.GetValueOrDefault(kind);
            foreach (var key in player.MarkerSourceCounts.Keys.Where(k => k.Marker == kind).ToArray()) player.MarkerSourceCounts.Remove(key);
            var sources = marks.Where(m => m.TargetSeat == player.Seat).GroupBy(m => m.Source.OwnerSeat).ToArray();
            foreach (var source in sources) player.MarkerSourceCounts[(kind, source.Key)] = source.Count();
            var count = sources.Sum(s => s.Count());
            if (count == 0) player.Markers.Remove(kind); else player.Markers[kind] = count;
            if (previous != count) AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(0, player.Seat, kind, count - previous, count, null, "skill-program.mou-mobile:xuanhuo.marker"));
        }
    }
    // Called at native batch completion, before any gain observer can change the
    // current phase, mark, hand or source. Every published qualification is scalar.
    private void CaptureMouFaZhengGainBatch(CardMovementBatchContext batch)
    {
        if (!HasMouFaZhengCapability) return;
        var marks = MouFaZhengMarks().ToArray();
        foreach (var mark in marks)
        foreach (var move in batch.Movements.Where(m => m.Sequence > mark.GiftSequence && m.To == CardLocation.Hand(mark.Source.OwnerSeat) &&
            m.From.OwnerSeat == mark.TargetSeat && m.From != m.To))
            AdvanceEventRulesAndQueueFact(new MouFaZhengActualGainEvent(mark.MarkId, batch.Id, move.Sequence, mark.TargetSeat, mark.Source.OwnerSeat));
        if (batch.MovementTiming is not { Phase: not TurnPhase.Draw }) return;
        foreach (var mark in marks.Where(m => IsLiveMouFaZhengSource(m) && MouFaZhengExtracted(m.MarkId) < 5))
        {
            var gain = batch.Movements.Select((m, i) => (m, i)).FirstOrDefault(x => x.m.Sequence > mark.GiftSequence && x.m.To == CardLocation.Hand(mark.TargetSeat) &&
                ActualHandGainOriginalSource(x.m, batch) != x.m.To);
            if (gain.m is null) continue;
            foreach (var c in CollectProgramTriggerCandidates(_players[mark.Source.OwnerSeat], SkillProgramTriggerWindow.CardsGained).Where(c => IsMouFaZhengGainTrigger(GetProgramTrigger(c)))
                .OrderBy(c => c.SkillInstanceId == mark.Source.SkillInstanceId ? 0 : 1).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).Take(1))
                AdvanceEventRulesAndQueueFact(new MouFaZhengGainQualifiedEvent(batch.Id, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash,
                    mark.MarkId, gain.i, gain.m.Sequence, mark.TargetSeat));
        }
    }
    private IEnumerable<ProgramTriggerCandidate> CollectMouFaZhengGainCandidates(CardMovementBatchContext batch) => ProgramEventHistory<MouFaZhengGainQualifiedEvent>()
        .Where(q => q.BatchId == batch.Id && _players[q.Source.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[q.Source.OwnerSeat], q.Source.SkillId, q.Source.SkillInstanceId) &&
            _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash == q.GameplayHash)
        .Select(q => new ProgramTriggerCandidate(q.Source.OwnerSeat, q.Source.SkillId, q.Source.BindingId, q.Source.SkillInstanceId, q.GameplayHash,
            _contentRegistry.GetSkill(q.Source.SkillId).Program!.Triggers.Single(t => t.Id == q.Source.BindingId).Priority, q.MovementIndex)).ToArray();
    private MouFaZhengGainQualifiedEvent? MouFaZhengQualification(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardsGained || context.OwnerSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat ||
            context.MovementBatch is not { MovementTiming.Phase: not TurnPhase.Draw } batch || context.ParentFrameId != batch.Id || context.MovementIndex != c.OccurrenceIndex ||
            context.OccurrenceIndex != c.OccurrenceIndex || c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c || !parent.Batch.Movements.SequenceEqual(batch.Movements)) return null;
        var q = ProgramEventHistory<MouFaZhengGainQualifiedEvent>().SingleOrDefault(q => q.BatchId == batch.Id && q.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) &&
            q.GameplayHash == c.GameplayHash && q.MovementIndex == c.OccurrenceIndex);
        return q is not null && context.SourceSeat == q.TargetSeat && batch.Movements[q.MovementIndex] is { } move && move.Sequence == q.GainSequence &&
            move.To == CardLocation.Hand(q.TargetSeat) && ActualHandGainOriginalSource(move, batch) != move.To &&
            ProgramEventHistory<MouFaZhengMarkEvent>().Any(m => m.MarkId == q.MarkId && m.TargetSeat == q.TargetSeat && m.Source.OwnerSeat == c.OwnerSeat && m.GiftSequence < q.GainSequence) ? q : null;
    }
    private ProgramSkillWindowContext CreateMouFaZhengGainContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate c)
    {
        var q = ProgramEventHistory<MouFaZhengGainQualifiedEvent>().Single(q => q.BatchId == parent.Id && q.Source.OwnerSeat == c.OwnerSeat && q.Source.SkillId == c.SkillId &&
            q.Source.SkillInstanceId == c.SkillInstanceId && q.MovementIndex == c.OccurrenceIndex);
        return new(SkillProgramTriggerWindow.CardsGained, parent.Id, c.OwnerSeat, SourceSeat: q.TargetSeat, TargetSeat: c.OwnerSeat, OccurrenceIndex: c.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: parent.Batch, MovementIndex: q.MovementIndex);
    }
}
