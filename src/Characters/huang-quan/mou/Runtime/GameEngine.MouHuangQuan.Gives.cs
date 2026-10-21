namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasMouHuangQuan=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouHuangQuanTuicheng);
    // Called only by an original, audited Give producer in its physical-record callback.
    // The whole intended operation is supplied, so a HE or Processing split seals once.
    private void RecordNativeGivenCards(long producerId,int giver,int recipient,IReadOnlyList<int> intended,long before)
    {
        if(!HasMouHuangQuan||intended.Count==0||giver==recipient)return;
        if(producerId<=0||!IsValidPlayerSeat(giver)||!IsValidPlayerSeat(recipient)||intended.Distinct().Count()!=intended.Count||before<0||before>_movementSequence)
            throw new InvalidOperationException("A native Give requires its original producer and distinct intended physical entities.");
        var terminal=intended.Select(id=>_cardMovements.LastOrDefault(m=>m.CardId==id&&m.Sequence>before)).ToArray();
        if(terminal.Any(m=>m is null||m.To!=CardLocation.Hand(recipient)&&m.To!=CardLocation.OutsideGame))return;
        var delivered=terminal.Where(m=>m!.To==CardLocation.Hand(recipient)).Cast<CardMovementRecord>().OrderBy(m=>m.Sequence).ToArray();
        if(delivered.Length==0)return;
        var last=terminal.Max(m=>(long)m!.Sequence);
        var existing=_players[giver].MouHqActualGifts??[];
        if(existing.Any(g=>g.ProducerId==producerId&&g.Recipient==recipient&&g.Movements.Any(m=>delivered.Any(d=>d.Sequence==m.Sequence))))return;
        var gift=new MouHqGift(++_resolutionSequence,producerId,giver,recipient,_turnNumber,_turnProgression.OwnerSeat,last){Movements=delivered};
        _players[giver].MouHqActualGifts=Array.AsReadOnly(existing.Append(gift).ToArray());
        AdvanceEventRulesAndQueueFact(new NativeCardsGivenEvent(gift.Id,producerId,giver,recipient,delivered.Length,gift.Turn,gift.TurnOwner,last));
    }
    private Action<CardMovementRecord> NativeGiveCompletion(long producer,int giver,int recipient,IReadOnlyList<int> ids,long before,Action<CardMovementRecord>? original=null)=>m=>
    {original?.Invoke(m);RecordNativeGivenCards(producer,giver,recipient,ids,before);};
    private Action<long,IReadOnlyList<CardMovementRecord>> NativeGiveCompletionRecords(long producer,int giver,int recipient,IReadOnlyList<int> ids,long before,Action<long,IReadOnlyList<CardMovementRecord>>? original=null)=>(batch,moves)=>
    {original?.Invoke(batch,moves);RecordNativeGivenCards(producer,giver,recipient,ids,before);};
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,35700)]
    private void ExpireMouHqGivenEntities(TurnEndedEvent ended)
    {foreach(var p in _players)if(p.MouHqActualGifts is{}gifts)p.MouHqActualGifts=gifts.Where(g=>g.Turn!=ended.TurnNumber||g.TurnOwner!=ended.ActorSeat).ToArray()is{Length:>0}left?Array.AsReadOnly(left):null;}
    private IReadOnlyList<MouHqGift> MouHqGiftsForBatch(CardMovementBatchContext batch,int giver)=>Array.AsReadOnly((_players[giver].MouHqActualGifts??[]).Where(g=>
        g.Turn==batch.TurnNumber&&batch.Movements.Any(m=>m.Sequence==g.CompletionSequence)&&g.Movements.All(m=>_cardMovements.Contains(m))&&
        ProgramEventHistory<NativeCardsGivenEvent>().Count(e=>e.Id==g.Id&&e.Giver==g.Giver&&e.Recipient==g.Recipient&&e.Count==g.Movements.Count&&e.CompletionSequence==g.CompletionSequence)==1).ToArray());
    private IReadOnlyList<ProgramTriggerCandidate> CollectMouHqGiveCandidates(CardMovementBatchContext batch)
    {
        var result=new List<ProgramTriggerCandidate>();
        foreach(var p in _players.Where(p=>p.IsAlive))
        foreach(var c in CollectProgramTriggerCandidates(p,SkillProgramTriggerWindow.CardsMoved).Where(c=>c.SkillId==MouHqTuicheng&&c.BindingId=="after-native-give"))
        foreach(var gift in MouHqGiftsForBatch(batch,p.Seat))
            result.Add(c with{OccurrenceIndex=Array.FindIndex(batch.Movements.ToArray(),m=>m.Sequence==gift.CompletionSequence)});
        return Array.AsReadOnly(result.ToArray());
    }
    private bool IsMouHqGiveTrigger(SkillProgramTrigger trigger)=>trigger.Effects is[{Op:SkillProgramEffectOp.ResolveMouHuangQuanTuicheng}];
    private ProgramSkillWindowContext CreateMouHqGiveContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate c)
    {
        if(c.OccurrenceIndex<0||c.OccurrenceIndex>=frame.Batch.Movements.Count)throw new InvalidOperationException("Tuicheng lost its exact real Give batch.");
        var gift=MouHqGiftsForBatch(frame.Batch,c.OwnerSeat).Single(g=>g.CompletionSequence==frame.Batch.Movements[c.OccurrenceIndex].Sequence);
        return new(SkillProgramTriggerWindow.CardsMoved,frame.Id,c.OwnerSeat,SourceSeat:c.OwnerSeat,TargetSeat:gift.Recipient,Amount:gift.Movements.Count,
            OccurrenceIndex:c.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[c.OwnerSeat]),MovementBatch:frame.Batch,MovementIndex:c.OccurrenceIndex){MouHqGift=gift};
    }
}
public sealed partial record ProgramSkillWindowContext
{[System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]public MouHqGift? MouHqGift{get;init;}}
