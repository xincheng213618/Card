namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool TryGetHuiyunFirePayment(CardUseFrame use,out HuiyunPaymentRecordedEvent paid,out long before)
    {
        paid=null!;before=0;
        if(use.CardKind!=CardKind.FireAttack||use.Action is not{Type:CardActionType.Use,EffectiveKind:CardKind.FireAttack}a)return false;
        if(use.HuiyunOrigin is{}origin){if(!HuiyunUseProof(use))return false;before=origin.BeforePayment;}
        else if(use.HuiyunFollowup is{}native&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==native.ProgramFrameId)is{}parent&&HuiyunNativeUseRidesOn(use,parent)&&HuiyunPaymentProof(use.Id,native.ActionId,native.CardId,CardLocation.Hand(native.ActorSeat),CardLocation.Processing,CardMoveReasons.Use,native.BeforePayment,true))before=native.BeforePayment;
        else return false;
        var facts=ProgramEventHistory<HuiyunPaymentRecordedEvent>().Where(e=>e.UseId==use.Id).ToArray();
        if(facts is not[var payment]||payment.ActionId!=a.ActionId)return false;paid=payment;return true;
    }
    private void CaptureHuiyunTrickPaymentBatch(CardMovementBatchContext batch)
    {
        if(_resolutionStack.LastOrDefault()is not CardUseFrame{Action:{}action}use||!TryGetHuiyunFirePayment(use,out var payment,out var before)||batch.ParentFrameId!=use.Id||batch.ParentBatchId is not null||batch.AwaitingProgramFrameId is not null||batch.NativeDrawInvocation is not null||batch.NativeBuZhiDiscardInvocation is not null||batch.Movements is not[var move]||move.CardId!=payment.CardId||move.From!=payment.From||move.To!=payment.To||move.Reason!=CardMoveReasons.Use||move.Sequence!=payment.Sequence||!_cardMovements.Contains(move))return;
        if(use.HuiyunOriginalPayment is not null)throw new InvalidOperationException("A Huiyun original use cannot issue another material payment tail.");
        var native=use.PaymentMovementReceipt;
        if(native is not null&&!CardUsePaymentBatchMatchesReceipt(use,batch))throw new InvalidOperationException("Huiyun cannot replace another native use's physical payment invoice.");
        native??=new(use.Id,action.ActionId,move.Sequence-1,move.Sequence){Batch=batch};
        var receipt=new HuiyunOriginalPaymentReceipt(use.Id,action.ActionId,batch.Id,before,_movementSequence);
        ReplaceRuntimeFrame(use.Id,use with{PaymentMovementReceipt=native,HuiyunOriginalPayment=receipt});AdvanceEventRulesAndQueueFact(new HuiyunOriginalPaymentIssuedEvent(receipt));
    }
    private bool HuiyunOriginalPaymentProof(CardUseFrame use)
    {
        if(use.HuiyunOriginalPayment is not{}r||use.PaymentMovementReceipt is not{}native||!TryGetHuiyunFirePayment(use,out var payment,out var before)||r.UseId!=use.Id||r.ActionId!=payment.ActionId||r.PrimaryBatchId!=native.Batch.Id||r.Before!=before||r.After<r.Before||r.After>_movementSequence||ProgramEventHistory<HuiyunOriginalPaymentIssuedEvent>().Count(e=>e.Receipt==r)!=1||!CardUsePaymentBatchMatchesReceipt(use,native.Batch)||native.Batch.ParentFrameId!=use.Id||native.Batch.ParentBatchId is not null||native.Batch.Movements is not[var paid])return false;
        return paid.CardId==payment.CardId&&paid.From==payment.From&&paid.To==payment.To&&paid.Reason==CardMoveReasons.Use&&paid.Sequence==payment.Sequence&&paid.Sequence>r.Before&&paid.Sequence<=r.After;
    }
    private bool IsHuiyunTrickPaymentBatch(CardUseFrame use,CardMovementBatchContext batch)
    {
        if(HuiyunEffectPaymentProof(use)&&use.HuiyunEffectPayment is{}effect)return HuiyunEffectBatchMatches(use,effect,batch);
        if(!HuiyunOriginalPaymentProof(use)||use.Action is not{PhysicalCards:[var material]}a||use.HuiyunOriginalPayment is not{}r||use.PaymentMovementReceipt is not{}native)return false;
        if(batch.Id==r.PrimaryBatchId)return CardUsePaymentBatchMatchesReceipt(use,batch);
        return material is{CardKind:CardKind.WoodenOx,From.Zone:CardZoneKind.Equipment}&&batch.ParentFrameId==use.Id&&batch.ParentBatchId==r.PrimaryBatchId&&batch.AwaitingProgramFrameId is null&&batch.NativeDrawInvocation is null&&batch.NativeBuZhiDiscardInvocation is null&&batch.TurnNumber==native.Batch.TurnNumber&&batch.Movements.Count>0&&batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>native.SequenceAfter&&m.Sequence<=r.After&&m.From==CardLocation.WoodenOxGrain(a.ProviderSeat)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard);
    }
    private bool HasHuiyunTrickPaymentTail(CardUseFrame use,RecoveryPaidCardUseContinuation continuation)=>continuation.SourceSeat==use.SourceSeat&&(continuation.Kind==RecoveryPaidCardUseKind.AttackDamage?HuiyunEffectPaymentProof(use):continuation is{Kind:RecoveryPaidCardUseKind.Trick,CardId:{}card,ActionKind:LegalActionKind.FireAttack,TargetSeats:{}targets}&&card==use.CardId&&(continuation.PlayedCardKind??use.CardKind)==CardKind.FireAttack&&targets.SequenceEqual(use.TargetSeats)&&HuiyunOriginalPaymentProof(use));
    private bool HasHuiyunTrickPaymentProcessing(IReadOnlyList<Card> processing)=>_resolutionStack.Count==2&&_resolutionStack[0]is CardUseFrame{RecoveryPaidContinuation:{}continuation}use&&_resolutionStack[1]is CardsMovedTriggerWindowFrame moved&&HasHuiyunTrickPaymentTail(use,continuation)&&IsPaidCardUseMovementReturn(use,moved)&&processing.All(c=>ProgramEventHistory<HuiyunPaymentRecordedEvent>().Any(p=>p.UseId==use.Id&&p.CardId==c.Id&&p.To==CardLocation.Processing&&_cardMovements.Any(m=>m.Sequence==p.Sequence&&m.CardId==c.Id&&m.From==p.From&&m.To==p.To&&m.Reason==p.Reason)));
}
