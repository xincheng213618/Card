namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MouGjLunshiEligible(CharacterState actor,IReadOnlyList<int> cards)
 {
  if(!actor.IsAlive||cards is not[var card]||_cardZones.GetLocation(card)!=CardLocation.Hand(actor.Seat)||!HasRuntimeSkill(actor,MouGuoJiaRuntime.Lunshi)||ActiveNullificationWindow is not{}w||w.DelayedReturn is not null||!IsOrdinaryTrick(w.EffectCardKind)||LifecycleCardUse(w.ParentFrameId)is not{Action:{Type:CardActionType.Use}action}use||use.CardKind!=w.EffectCardKind||action.ActorSeat==actor.Seat||use.SourceSeat!=action.ActorSeat||!w.TargetSeats.Any(t=>t!=action.ActorSeat)||!w.TargetSeats.All(use.TargetSeats.Contains))return false;
  var hand=GetHand(actor);return hand.Count(c=>EffectiveSuit(actor,c)is Suit.Heart or Suit.Diamond)==hand.Count(c=>EffectiveSuit(actor,c)is Suit.Spade or Suit.Club);
 }
 [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan,37100)]private bool MouGjLunshiBan(ProgramViewAsLegalityQuery q)=>q.Source.SkillId==MouGuoJiaRuntime.Lunshi&&(q.Source.BindingId!="balanced-hand-nullification"||q.Kind!=CardKind.Nullification||!q.ForResponse||q.DyingUse||!MouGjLunshiEligible(q.Owner,q.CardIds));
 private CardConversionSource? MouGjLunshiSource(CharacterState owner,IReadOnlyList<int> cards,CardConversionSource? selected)=>selected is{SkillId:MouGuoJiaRuntime.Lunshi,BindingId:"balanced-hand-nullification"}&&selected.OwnerSeat==owner.Seat&&HasRuntimeSkillInstance(owner,selected.SkillId,selected.SkillInstanceId)&&MouGjLunshiEligible(owner,cards)?selected:null;
}
