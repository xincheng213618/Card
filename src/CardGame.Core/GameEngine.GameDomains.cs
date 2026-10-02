namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksFirstGameDomainCrossings=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveFirstGameDomainCrossing);
    private static bool IsRuleOutsideGame(CardLocation at)=>at.Zone is CardZoneKind.PrivateTurnHold or CardZoneKind.PublicPersistentPile or CardZoneKind.Authority or CardZoneKind.BuquWound or CardZoneKind.Chunlao or CardZoneKind.PrivateReserve or CardZoneKind.PojunHold or CardZoneKind.PublicDeferredPile or CardZoneKind.WoodenOxGrain or CardZoneKind.OutsideGame;
    private void CaptureFirstGameDomainCrossings(long batchId,int turn,IReadOnlyList<CardMovementRecord> movements)
    {
        if(!TracksFirstGameDomainCrossings||turn<1||!CompleteProgramEventHistory().OfType<TurnStartedEvent>().Any(e=>e.TurnNumber==turn))return;
        foreach(var movement in movements.OrderBy(m=>m.Sequence))
        {
            var from=IsRuleOutsideGame(movement.From);var to=IsRuleOutsideGame(movement.To);if(from==to)continue;
            var movedOut=to;
            if(CompleteProgramEventHistory().OfType<FirstGameDomainCrossingEvent>().Any(e=>e.TurnNumber==turn&&e.MovedOut==movedOut))continue;
            AdvanceEventRulesAndQueueFact(new FirstGameDomainCrossingEvent(turn,batchId,movedOut,movement.Sequence));
        }
    }
    private IEnumerable<ProgramTriggerCandidate> CollectFirstDomainCandidates(CardMovementBatchContext batch)
    {
        foreach(var fact in CompleteProgramEventHistory().OfType<FirstGameDomainCrossingEvent>().Where(e=>e.BatchId==batch.Id).OrderBy(e=>e.MovementOrdinal))
        foreach(var owner in _players.Where(p=>p.IsAlive).OrderBy(p=>(p.Seat-_currentSeat+_players.Count)%_players.Count))
        foreach(var group in CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.FirstGameDomainCrossing).GroupBy(c=>c.SkillId,StringComparer.Ordinal))
            yield return group.OrderByDescending(c=>c.Priority).ThenBy(c=>c.SkillInstanceId,StringComparer.Ordinal).First() with{OccurrenceIndex=fact.MovedOut?0:1};
    }
    private FirstGameDomainCrossingEvent? FirstDomainFact(ProgramSkillWindowContext context)=>context.MovementBatch is {} batch?CompleteProgramEventHistory().OfType<FirstGameDomainCrossingEvent>().SingleOrDefault(e=>e.BatchId==batch.Id&&e.MovedOut==(context.OccurrenceIndex==0)):null;
    private ProgramSkillWindowContext CreateFirstDomainContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate candidate)
    {
        var fact=CompleteProgramEventHistory().OfType<FirstGameDomainCrossingEvent>().Single(e=>e.BatchId==frame.Batch.Id&&e.MovedOut==(candidate.OccurrenceIndex==0));
        var index=frame.Batch.Movements.Select((m,i)=>(m,i)).Single(x=>x.m.Sequence==fact.MovementOrdinal).i;
        var movement=frame.Batch.Movements[index];
        return new(SkillProgramTriggerWindow.FirstGameDomainCrossing,frame.Id,candidate.OwnerSeat,SourceSeat:movement.From.OwnerSeat,TargetSeat:candidate.OwnerSeat,OccurrenceIndex:candidate.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]),MovementBatch:frame.Batch,MovementIndex:index);
    }
}
