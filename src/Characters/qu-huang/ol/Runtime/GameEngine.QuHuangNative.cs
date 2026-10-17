namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanIssueQuHuangNishouLightning(ProgramSkillFrame f)
    {
        if(f.QuHuang is not{Stage:QuHuangStage.NishouChoice,Equipment:{}lost}||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||_cardZones.GetLocation(lost.CardId)!=CardLocation.DiscardPile)return false;
        var owner=_players[f.OwnerSeat];var card=GetAdvancedCard(lost.CardId);var suit=EffectiveSuit(owner,card);
        return !owner.JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(owner,CardKind.Lightning)&&!HasCardPolicy(owner,SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget)&&!IsCardUseForbidden(owner.Seat,CardKind.Lightning,CardActionType.Use)&&!IsDirectedCardTargetProhibited(owner.Seat,owner.Seat,CardKind.Lightning)&&!IsSelfTargetForbiddenAction(owner,CardKind.Lightning,[owner.Seat])&&!IsCardTargetProhibited(owner,CardKind.Lightning,suit,SuitColor(suit))&&!HasBeneficiarySuitShield(owner.Seat,owner.Seat,suit)&&!IsTurnPhysicalUseForbidden(owner.Seat,[card.Id])&&!IsTurnSuitUseForbidden(owner.Seat,suit);
    }
    private void IssueQuHuangLightning(ProgramSkillFrame f)
    {
        if(!CanIssueQuHuangNishouLightning(f))throw new InvalidOperationException("Nishou may use only its original still-discarded equipment and a legal native Lightning target.");
        var r=f.QuHuang!;var lost=r.Equipment!;var card=GetAdvancedCard(lost.CardId);var o=new QuHuangNishouUseOrigin(f.Id,_resolutionSequence+1,_cardActionSequence+1,r.Source,r.GameplayHash,f.OwnerSeat,card.Id,card.Kind,EffectiveSuit(_players[f.OwnerSeat],card),card.Rank,lost.Sequence);
        ReplaceRuntimeTop(f=f with{QuHuang=r with{Paid=true,Stage=QuHuangStage.Lightning,Native=o,Before=_movementSequence,After=_movementSequence}});
        var id=BeginCardUse(card,o.ActorSeat,[o.ActorSeat],CardKind.Lightning,conversionSource:o.Source,quHuangNishou:o);
        if(id!=o.CardUseFrameId||LifecycleCardUse(id)?.Action?.ActionId!=o.ActionId)throw new InvalidOperationException("Nishou changed its reserved native physical use identity.");
        MoveCard(card,CardLocation.DiscardPile,CardLocation.Processing,CardMoveReasons.Use,m=>
        {var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(p=>p.Id==f.Id);if(parent.QuHuang!.NativePaymentSequence is not null||m.CardId!=o.CardId||m.CardKind!=o.PrintedKind||m.From!=CardLocation.DiscardPile||m.To!=CardLocation.Processing||m.Reason!=CardMoveReasons.Use)throw new InvalidOperationException("Nishou cannot replace or pay its original discarded equipment twice.");ReplaceRuntimeFrame(parent.Id,parent with{QuHuang=parent.QuHuang with{NativePaymentSequence=m.Sequence,After=m.Sequence}});AdvanceEventRulesAndQueueFact(new NishouLightningPaidEvent(o,m.Sequence));});
        if(!IsQuHuangNishouUse(id))throw new InvalidOperationException("Nishou lost its real equipment, effective appearance or physical native cost.");
        BeginJizhiOrNullificationWindow(id,card,o.ActorSeat,[o.ActorSeat],LegalActionKind.Lightning,playedCardKind:CardKind.Lightning);
    }
    private bool MatchesQuHuangNishou(ProgramSkillFrame f,QuHuangNishouUseOrigin o)=>f.QuHuang is{Stage:QuHuangStage.Lightning,Native:{}expected}r&&expected==o&&r.Source==o.Source&&r.GameplayHash==o.GameplayHash&&f.Id==o.ProgramFrameId&&f.OwnerSeat==o.ActorSeat&&r.Equipment is{}e&&e.CardId==o.CardId&&e.Kind==o.PrintedKind&&e.Sequence==o.LossSequence;
    private bool IsQuHuangNishouUse(long id)
    {
        if(LifecycleCardUse(id)is not{QuHuangNishou:{}o,CardKind:CardKind.Lightning,Action:{Type:CardActionType.Use}a}use||id!=o.CardUseFrameId||use.CardId!=o.CardId||a.ActionId!=o.ActionId||a.EffectiveKind!=CardKind.Lightning||a.EffectiveSuit!=o.Suit||a.EffectiveRank!=o.Rank||a.EffectiveIsRed!=SuitColor(o.Suit)||a.ProviderSeat!=o.ActorSeat||a.RequesterSeat is not null||a.ResponderSeat is not null||a.PhysicalCards is not[var cost]||cost.CardId!=o.CardId||cost.CardKind!=o.PrintedKind||cost.From!=CardLocation.DiscardPile||!a.ConversionChain.SequenceEqual([o.Source])||use.PhysicalCardIds is not[var material]||material!=o.CardId||!ShownEntityUseActorMatches(use,o.ActorSeat,o.ActorSeat))return false;
        var index=_resolutionStack.FindIndex(p=>p.Id==id);return index>0&&_resolutionStack[index-1]is ProgramSkillFrame f&&MatchesQuHuangNishou(f,o)&&f.QuHuang!.NativePaymentSequence is{}sequence&&ProgramEventHistory<NishouLightningPaidEvent>().Count(e=>e.Origin==o&&e.Sequence==sequence)==1&&_cardMovements.Any(m=>m.Sequence==sequence&&m.Sequence>o.LossSequence&&m.CardId==o.CardId&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use);
    }
    private bool TrySkipUnavailableQuHuangLightning(NullificationWindowFrame p)
    {
        if(p.EffectCardKind!=CardKind.Lightning||!IsQuHuangNishouUse(p.ParentFrameId))return false;var o=LifecycleCardUse(p.ParentFrameId)!.QuHuangNishou!;var target=_players[GetNullificationTargetSeat(p)??o.ActorSeat];
        if(target.IsAlive&&!target.JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(target,CardKind.Lightning)&&_cardZones.GetLocation(o.CardId)==CardLocation.Processing)return false;
        if(_cardZones.GetLocation(o.CardId)!=CardLocation.Processing&&!IsClaimedUseCardEntity(p.ParentFrameId,o.CardId))throw new InvalidOperationException("Nishou's paid equipment left native Processing without an exact current use claim.");SkipUnavailableTargetCardEffect(p,target,CardEffectSkipReason.SkillNullified);return true;
    }
    [NativeUseValidation(27421)]private void ValidateQuHuangNishou(CardUseFrame use)
    {if(use.QuHuangNishou is not null&&!IsQuHuangNishouUse(use.Id))throw new InvalidOperationException("Nishou changed its exact discarded entity, native action or typed owning return.");}
    [NativeUseReturn(27421)]private void ReturnQuHuangLightning(CardUseFrame use)
    {
        if(use.QuHuangNishou is not{}o)return;if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||!MatchesQuHuangNishou(f,o)||use.Step!=ResolutionFrameStep.Completed||ProgramEventHistory<NishouLightningReturnedEvent>().Any(e=>e.Origin==o))throw new InvalidOperationException("Nishou Lightning must finish its whole physical use exactly once before returning.");
        ReplaceRuntimeTop(f=f with{QuHuang=f.QuHuang! with{Stage=QuHuangStage.LightningChildren,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new NishouLightningReturnedEvent(o));ContinueQuHuang(f.Id);
    }
    private bool IsQuHuangNativeSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext batch)=>f.QuHuang is{Stage:QuHuangStage.LightningChildren,Native:{}o}r&&r.NativePaymentSequence is not null&&ProgramEventHistory<NishouLightningReturnedEvent>().Count(e=>e.Origin==o)==1&&ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==o.CardUseFrameId)==1&&LifecycleCardUse(o.CardUseFrameId)is null&&batch.ParentFrameId==o.CardUseFrameId&&batch.AwaitingProgramFrameId is null&&batch.OriginOwnerSeat==f.OwnerSeat&&batch.OriginSkillId==f.SkillId&&batch.OriginSkillInstanceId==f.SkillInstanceId&&batch.Movements.Count>0&&batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==o.CardId&&(m.From==CardLocation.DiscardPile&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use||m.From==CardLocation.Processing&&(m.To==CardLocation.Judgment(o.ActorSeat)&&m.Reason==CardMoveReasons.DelayedCardPlace||m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished)));
}
