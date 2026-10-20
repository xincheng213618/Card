namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool FeiYiTrickPaymentMaterialsMatch(CardUseFrame use,CardMovementBatchContext batch)
    {
        if(!HasFeiYiHezhong||use.Action is not{Type:CardActionType.Use}action||!IsOrdinaryTrick(use.CardKind)||use.CardKind==CardKind.Nullification||
            action.EffectiveKind!=use.CardKind||action.ActorSeat!=use.SourceSeat||batch.ParentFrameId!=use.Id||batch.ParentBatchId is not null||
            batch.AwaitingProgramFrameId is not null||batch.NativeDrawInvocation is not null||batch.NativeBuZhiDiscardInvocation is not null||
            batch.TurnNumber!=_turnNumber||batch.Movements.Count==0||batch.Movements.Count!=action.PhysicalCards.Count||
            batch.Movements.Select(m=>m.CardId).Distinct().Count()!=batch.Movements.Count||
            batch.FeiYiSingletons?.SingleOrDefault(t=>t.OwnerSeat==action.ProviderSeat) is not{}singleton||singleton.Before==1||singleton.After!=1||
            !batch.Movements.Any(m=>m.From==CardLocation.Hand(singleton.OwnerSeat)))return false;
        foreach(var move in batch.Movements)
        {
            var cost=action.PhysicalCards.SingleOrDefault(c=>c.CardId==move.CardId);
            if(cost is null||cost.From!=move.From||cost.CardKind!=move.CardKind||move.From.OwnerSeat!=action.ProviderSeat||
                move.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)||
                move.Reason!=CardMoveReasons.Use||move.TurnNumber!=batch.TurnNumber||move.Sequence<=0||move.Sequence>_cardMovements.Count||_cardMovements[move.Sequence-1]!=move)return false;
            var card=_cardZones.CardsAt(_cardZones.GetLocation(move.CardId)).Single(c=>c.Id==move.CardId);
            var destination=card.IsGeneralWeapon&&cost.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:
                IsProgramAlternativeCost(action,move.CardId)?CardLocation.DrawPile:CardLocation.Processing;
            if(move.To!=destination)return false;
        }
        return true;
    }
    private void CaptureFeiYiTrickPaymentBatch(CardMovementBatchContext batch)
    {
        if(_resolutionStack.LastOrDefault() is not CardUseFrame use||!FeiYiTrickPaymentMaterialsMatch(use,batch))return;
        var owner=use.Action!.ProviderSeat;
        // This is an opt-in at the real payment boundary. Later tail validation
        // uses the immutable invoice even after its direction is paid or lost.
        if(_winner!=Winner.None||!_players[owner].IsAlive||
            !FeiYiDirectionAvailable(owner,true)&&!FeiYiDirectionAvailable(owner,false)||
            !CollectFeiYiSingletonCandidates(batch).Any(c=>c.OwnerSeat==owner&&HasRuntimeSkillInstance(_players[owner],c.SkillId,c.SkillInstanceId)))return;
        if(use.PaymentMovementReceipt is not null)throw new InvalidOperationException("One native Hezhong trick cannot issue its physical payment twice.");
        ReplaceRuntimeFrame(use.Id,use with{PaymentMovementReceipt=new(use.Id,use.Action!.ActionId,batch.Movements[0].Sequence-1,batch.Movements[^1].Sequence){Batch=batch}});
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 40)]
    private bool IsFeiYiTrickPaymentBatch(CardUseFrame use,CardMovementBatchContext batch)=>
        FeiYiTrickPaymentMaterialsMatch(use,batch)&&CardUsePaymentBatchMatchesReceipt(use,batch)&&
        (use.PaymentMovementReceipt!.Batch.FeiYiSingletons??[]).SequenceEqual(batch.FeiYiSingletons??[]);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 30)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 30)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 30)]
    private bool HasFeiYiTrickPaymentTail(CardUseFrame use,RecoveryPaidCardUseContinuation continuation)=>
        continuation is{Kind:RecoveryPaidCardUseKind.Trick,CardId:{}card,ActionKind:not null,TargetSeats:{}targets}&&card==use.CardId&&continuation.SourceSeat==use.SourceSeat&&
        targets.SequenceEqual(use.TargetSeats)&&use.PaymentMovementReceipt is{}receipt&&IsFeiYiTrickPaymentBatch(use,receipt.Batch);
}
