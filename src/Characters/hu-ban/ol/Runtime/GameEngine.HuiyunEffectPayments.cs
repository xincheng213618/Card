namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27383)]
    private void RecordHuiyunEffectPayment(IGameEvent fact)
    {
        if(!HasHuBanCapability||fact is not FireAttackResolvedEvent{CausedDamage:true,MatchingDiscardCardId:{}card}e||LifecycleCardUse(e.ResolutionId)is not{Action:{}action,FireAttackSelection.RevealedCardId:{}revealed}use||use.ColorFireAttack?.PaidCardId is not null||use.MouZhitianPayment is not null||!TryGetHuiyunFirePayment(use,out var initial,out _)||revealed!=e.RevealedCardId||use.SourceSeat!=e.SourceSeat||GetFireAttackTargetSeat(use)!=e.TargetSeat)return;
        var finish=_cardMovements.LastOrDefault(m=>m.CardId==card&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.FireAttackDiscardFinished&&m.Sequence>initial.Sequence);
        var hand=finish is null?null:_cardMovements.LastOrDefault(m=>m.CardId==card&&m.From==CardLocation.Hand(e.SourceSeat)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.FireAttackDiscard&&m.Sequence>initial.Sequence&&m.Sequence<finish.Sequence);
        if(hand is null||finish is null)throw new InvalidOperationException("Huiyun FireAttack effect lost its two real source-Hand discard movements.");
        var handBatches=_pendingCardsMovedBatches.Where(b=>b.ParentFrameId==use.Id&&b.ParentBatchId is null&&b.AwaitingProgramFrameId is null&&b.Movements.SequenceEqual([hand])).ToArray();
        var discardBatches=_pendingCardsMovedBatches.Where(b=>b.ParentFrameId==use.Id&&b.ParentBatchId is null&&b.AwaitingProgramFrameId is null&&b.Movements.SequenceEqual([finish])).ToArray();
        if(handBatches is not[var hb]||discardBatches is not[var db]||hb.Id==db.Id)throw new InvalidOperationException("Huiyun FireAttack effect lost its independent original cost batches.");
        var r=new HuiyunEffectPaymentReceipt(use.Id,action.ActionId,NativeExtraEffectRoundOrdinal(use.Id),use.TargetIndex,e.SourceSeat,e.TargetSeat,revealed,card,hb.Id,db.Id,hand.Sequence,finish.Sequence);
        if(ProgramEventHistory<HuiyunEffectPaymentIssuedEvent>().Any(p=>p.Receipt.UseId==r.UseId&&p.Receipt.EffectRound==r.EffectRound&&p.Receipt.TargetIndex==r.TargetIndex))throw new InvalidOperationException("One Huiyun FireAttack effect cannot pay its discard twice.");
        ReplaceRuntimeFrame(use.Id,use with{HuiyunEffectPayment=r});AdvanceEventRulesAndQueueFact(new HuiyunEffectPaymentIssuedEvent(r));
    }
    private bool HuiyunEffectPaymentProof(CardUseFrame use)
    {
        if(use.HuiyunEffectPayment is not{}r||!TryGetHuiyunFirePayment(use,out var initial,out _)||use.Action?.ActionId!=r.ActionId||use.Id!=r.UseId||use.SourceSeat!=r.SourceSeat||use.CardAttack is not{EffectiveCardKind:CardKind.FireAttack}attack||attack.SourceSeat!=r.SourceSeat||attack.TargetSeat!=r.TargetSeat||use.TargetIndex!=r.TargetIndex||NativeExtraEffectRoundOrdinal(use.Id)!=r.EffectRound||r.HandSequence<=initial.Sequence||r.DiscardSequence<=r.HandSequence||r.DiscardSequence>_movementSequence||r.HandBatchId==r.DiscardBatchId||ProgramEventHistory<HuiyunEffectPaymentIssuedEvent>().Count(e=>e.Receipt==r)!=1||!ProgramEventHistory<FireAttackResolvedEvent>().Any(e=>e.ResolutionId==use.Id&&e.SourceSeat==r.SourceSeat&&e.TargetSeat==r.TargetSeat&&e.RevealedCardId==r.RevealedCardId&&e.MatchingDiscardCardId==r.DiscardCardId&&e.CausedDamage))return false;
        return _cardMovements.Any(m=>m.Sequence==r.HandSequence&&m.CardId==r.DiscardCardId&&m.From==CardLocation.Hand(r.SourceSeat)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.FireAttackDiscard)&&_cardMovements.Any(m=>m.Sequence==r.DiscardSequence&&m.CardId==r.DiscardCardId&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.FireAttackDiscardFinished);
    }
    private bool HuiyunEffectBatchMatches(CardUseFrame use,HuiyunEffectPaymentReceipt r,CardMovementBatchContext batch)=>batch.ParentFrameId==use.Id&&batch.ParentBatchId is null&&batch.AwaitingProgramFrameId is null&&batch.NativeDrawInvocation is null&&batch.NativeBuZhiDiscardInvocation is null&&batch.Movements is[var m]&&_cardMovements.Contains(m)&&m.CardId==r.DiscardCardId&&(batch.Id==r.HandBatchId&&m.Sequence==r.HandSequence&&m.From==CardLocation.Hand(r.SourceSeat)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.FireAttackDiscard||batch.Id==r.DiscardBatchId&&m.Sequence==r.DiscardSequence&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.FireAttackDiscardFinished);
}
