namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void PayTongduGift(ProgramSkillFrame f,LiuBaEntity card)
    {
        var r=f.LiuBa!;var entry=CurrentHandEntry(r.Recipient,card.CardId)??throw new InvalidOperationException("Tongdu requires the selected original Hand entry.");PrepareLiuBaMovement(ref f,LiuBaStage.GiftChildren,[card]);
        if(r.Recipient==f.OwnerSeat)AdvanceEventRulesAndQueueFact(new TongduGiftIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.Recipient,card.CardId,card.Kind,entry,null));
        else MoveProgramCardsFromMultipleSources([card.CardId], CardLocation.Hand(f.OwnerSeat), new(TongduGiftReason), NativeGiveCompletionRecords(f.Id,r.Recipient,f.OwnerSeat,[card.CardId],_movementSequence,(batch,records)=>
        {RecordLiuBaMovement(f,batch,records);AdvanceEventRulesAndQueueFact(new TongduGiftIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.Recipient,card.CardId,card.Kind,entry,records.Single().Sequence));}));
        CompleteLiuBaMovement(f,r.Recipient==f.OwnerSeat?0:1);
    }
    private void PayTongduReturn(ProgramSkillFrame f)
    {
        var r=f.LiuBa!;var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,f.WindowContext!.OccurrenceIndex);
        var debt=CurrentTongduDebt(c,f.WindowContext!)??throw new InvalidOperationException("Tongdu lost its original issued entity debt.");var material=LiuBaOwnedEntities(f.OwnerSeat).Where(e=>e.CardId==debt.CardId).ToArray();
        ReplaceRuntimeTop(f=f with{LiuBa=r with{DebtId=debt.FrameId}});AdvanceEventRulesAndQueueFact(new TongduDebtConsumedEvent(debt.FrameId,f.Id,r.ParentId,r.ActualTurn,r.PhaseInstance));PrepareLiuBaMovement(ref f,LiuBaStage.ReturnChildren,material);
        if(material.Length!=0)MoveProgramCardsFromMultipleSources([debt.CardId],CardLocation.DrawPile,new(TongduReturnReason),(batch,records)=>RecordLiuBaMovement(f,batch,records));CompleteLiuBaMovement(f,material.Length);
    }
}
