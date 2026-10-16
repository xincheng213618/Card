namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsFeiYiSingletonTrigger(SkillProgramTrigger t)=>t.Effects is [{Op:SkillProgramEffectOp.RevealSingletonAndArmRankEffects}];
    private CardMovementBatchContext CaptureFeiYiSingletonBatch(CardMovementBatchContext batch)
    {
        if(!HasFeiYiHezhong)return batch;
        var transitions=batch.SourceCounts.Concat(batch.DestinationCounts??[]).Where(c=>c.Location.Zone==CardZoneKind.Hand&&c.Location.OwnerSeat is not null)
            .GroupBy(c=>c.Location.OwnerSeat!.Value).Select(group=>
            {
                var counts=group.Distinct().ToArray();if(counts.Length!=1)throw new InvalidOperationException("One atomic hand operation cannot have contradictory singleton counts.");
                var c=counts[0];var owner=group.Key;
                return c.CountBefore!=1&&c.CountAfter==1&&GetHand(_players[owner]) is [var card]
                    ?new FeiYiSingletonTransition(owner,c.CountBefore,c.CountAfter,card.Id,card.Rank):null;
            }).OfType<FeiYiSingletonTransition>().ToArray();
        return transitions.Length==0?batch:batch with{FeiYiSingletons=transitions};
    }
    private IEnumerable<ProgramTriggerCandidate> CollectFeiYiSingletonCandidates(CardMovementBatchContext batch)
    {
        foreach(var t in (batch.FeiYiSingletons??[]).Where(t=>FeiYiSingletonIsWholeOperation(batch,t)))
        foreach(var c in CollectProgramTriggerCandidates(_players[t.OwnerSeat],SkillProgramTriggerWindow.CardsMoved).Where(c=>IsFeiYiSingletonTrigger(GetProgramTrigger(c))))
            yield return c;
    }
    private bool FeiYiSingletonIsWholeOperation(CardMovementBatchContext batch,FeiYiSingletonTransition transition)=>
        !batch.Movements.Where(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(transition.OwnerSeat)).Any(m=>
            ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Any(draw=>draw.OwnerSeat==transition.OwnerSeat&&draw.ActualCount>1&&draw.TurnNumber==batch.TurnNumber&&
                draw.ParentFrameId==batch.ParentFrameId&&draw.Reason==m.Reason&&m.Sequence>draw.SequenceBefore&&m.Sequence<=draw.SequenceAfter));
    private ProgramSkillWindowContext CreateFeiYiSingletonContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate c)=>
        new(SkillProgramTriggerWindow.CardsMoved,frame.Id,c.OwnerSeat,SourceSeat:c.OwnerSeat,TargetSeat:c.OwnerSeat,
            OccurrenceIndex:c.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[c.OwnerSeat]),MovementBatch:frame.Batch);
    private FeiYiSingletonTransition? CurrentFeiYiSingleton(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!IsFeiYiSingletonTrigger(GetProgramTrigger(c))||context.Window!=SkillProgramTriggerWindow.CardsMoved||context.OwnerSeat!=c.OwnerSeat||context.OccurrenceIndex!=c.OccurrenceIndex||
            context.MovementBatch is not{}batch||batch.TurnNumber!=_turnNumber||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window||
            window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex]!=c||window.Batch.Id!=batch.Id||
            !window.Batch.Movements.SequenceEqual(batch.Movements)||!(window.Batch.FeiYiSingletons??[]).SequenceEqual(batch.FeiYiSingletons??[])||
            batch.FeiYiSingletons?.SingleOrDefault(t=>t.OwnerSeat==c.OwnerSeat) is not{}transition||transition.Before==1||transition.After!=1||!FeiYiSingletonIsWholeOperation(batch,transition)||
            !_cardZones.Contains(CardLocation.Hand(c.OwnerSeat),transition.CardId)||
            !FeiYiDirectionAvailable(c.OwnerSeat,true)&&!FeiYiDirectionAvailable(c.OwnerSeat,false))return null;
        return transition;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,26600)]
    private bool CanOfferFeiYiSingleton(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>!IsFeiYiSingletonTrigger(t)||CurrentFeiYiSingleton(c,context)is not null;
}
