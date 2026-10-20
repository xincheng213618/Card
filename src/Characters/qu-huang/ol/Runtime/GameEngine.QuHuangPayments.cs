namespace CardGame.Core;
public sealed record QiejianUsePaymentCapturedEvent(long UseId,long ActionId,long BatchId,int ProviderSeat):IGameEvent;
public sealed partial class GameEngine
{
    private bool QiejianUseMaterialsMatch(CardUseFrame use,CardMovementBatchContext batch)
    {
        if(!HasQuHuangCapability||use.Action is not{Type:CardActionType.Use}a||use.CardKind!=a.EffectiveKind||use.SourceSeat!=a.ActorSeat||
            batch.ParentFrameId!=use.Id||batch.ParentBatchId is not null||batch.AwaitingProgramFrameId is not null||
            batch.NativeDrawInvocation is not null||batch.NativeBuZhiDiscardInvocation is not null||batch.TurnNumber!=_turnNumber||
            batch.Movements.Count==0||batch.Movements.Count!=a.PhysicalCards.Count||batch.Movements.Select(m=>m.CardId).Distinct().Count()!=batch.Movements.Count||
            !batch.SourceCounts.Any(c=>c.Location==CardLocation.Hand(a.ProviderSeat)&&c.CountBefore>0&&c.CountAfter==0))return false;
        var equipment=EquipmentCatalog.IsEquipment(use.CardKind)&&a.PhysicalCards is[var nativeEquipment]&&nativeEquipment.CardKind==use.CardKind&&nativeEquipment.From.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain;
        var expectedReason=equipment?CardMoveReasons.EquipmentUse:CardMoveReasons.Use;
        foreach(var m in batch.Movements)
        {
            var cost=a.PhysicalCards.SingleOrDefault(c=>c.CardId==m.CardId);
            if(cost is null||cost.From!=m.From||cost.CardKind!=m.CardKind||m.From.OwnerSeat!=a.ProviderSeat||
                m.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)||m.Reason!=expectedReason||
                m.TurnNumber!=batch.TurnNumber||m.Sequence<=0||m.Sequence>_cardMovements.Count||_cardMovements[m.Sequence-1]!=m)return false;
            var card=GetAdvancedCard(m.CardId);var destination=card.IsGeneralWeapon&&m.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:
                IsProgramAlternativeCost(a,m.CardId)?CardLocation.DrawPile:CardLocation.Processing;
            if(m.To!=destination)return false;
        }
        return true;
    }
    private void CaptureQiejianUsePaymentBatch(CardMovementBatchContext batch)
    {
        if(_resolutionStack.LastOrDefault()is not CardUseFrame use||!QiejianUseMaterialsMatch(use,batch)||_winner!=Winner.None)return;
        var a=use.Action!;if(!_players[a.ProviderSeat].IsAlive||!_players.Where(p=>p.IsAlive&&!QiejianBlocked(p.Seat,a.ProviderSeat)).Any(p=>
            CollectProgramTriggerCandidates(p,SkillProgramTriggerWindow.CardsMoved).Any(c=>GetProgramTrigger(c).Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveQiejianLastHandLoss))))return;
        if(ProgramEventHistory<QiejianUsePaymentCapturedEvent>().Any(e=>e.UseId==use.Id))throw new InvalidOperationException("A Qiejian-observed use cannot pay its original physical materials twice.");
        if(use.PaymentMovementReceipt is not null&&!CardUsePaymentBatchMatchesReceipt(use,batch))throw new InvalidOperationException("Qiejian cannot replace another native payment invoice.");
        if(use.PaymentMovementReceipt is null)ReplaceRuntimeFrame(use.Id,use with{PaymentMovementReceipt=new(use.Id,a.ActionId,batch.Movements[0].Sequence-1,batch.Movements[^1].Sequence){Batch=batch}});
        AdvanceEventRulesAndQueueFact(new QiejianUsePaymentCapturedEvent(use.Id,a.ActionId,batch.Id,a.ProviderSeat));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 70)]
    private bool IsQiejianUsePaymentBatch(CardUseFrame use,CardMovementBatchContext batch)=>QiejianUseMaterialsMatch(use,batch)&&
        CardUsePaymentBatchMatchesReceipt(use,batch)&&ProgramEventHistory<QiejianUsePaymentCapturedEvent>().Count(e=>e.UseId==use.Id&&e.ActionId==use.Action!.ActionId&&e.BatchId==batch.Id&&e.ProviderSeat==use.Action.ProviderSeat)==1;
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 60)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 60)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 60)]
    private bool HasQiejianUsePaymentTail(CardUseFrame use,RecoveryPaidCardUseContinuation continuation)
    {
        if(continuation.SourceSeat!=use.SourceSeat||use.PaymentMovementReceipt is not{}paid||!IsQiejianUsePaymentBatch(use,paid.Batch))return false;
        return continuation.Kind switch
        {
            RecoveryPaidCardUseKind.CommittedSlash=>IsSlashCard(use.CardKind),
            RecoveryPaidCardUseKind.Trick=>continuation.CardId==use.CardId&&continuation.ActionKind is not null&&continuation.TargetSeats is{}targets&&targets.SequenceEqual(use.TargetSeats)&&(continuation.PlayedCardKind??use.CardKind)==use.CardKind,
            RecoveryPaidCardUseKind.Simple=>continuation.CardId==use.CardId&&continuation.Simple?.CardId==use.CardId,
            RecoveryPaidCardUseKind.EquipmentUse=>continuation.CardId==use.CardId&&EquipmentCatalog.IsEquipment(use.CardKind)&&continuation.EquipmentSlot==EquipmentCatalog.Get(use.CardKind).Slot,
            _=>false
        };
    }
}
