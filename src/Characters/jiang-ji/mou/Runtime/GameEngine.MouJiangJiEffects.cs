namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void IssueMouJjRepeat(ProgramSkillFrame f)
 {
  if(f.MouJiangJi is not{}r||_resolutionStack.SingleOrDefault(x=>x.Id==r.WindowId)is not ProgramCardTriggerWindowFrame window||LifecycleCardUse(r.UseId)is not{}use)throw new InvalidOperationException("Yingshi lost its issued original native window.");
  var state=MouJiangJi.IssueEffect(f,use,window);UpdateLifecycleCardUse(use.Id,u=>u with{MouJiangJiRounds=state});
 }
 private bool HasUnstartedMouJjEffect(long id)=>LifecycleCardUse(id)is{MouJiangJiRounds:{}r,Action:{}a}u&&a.ActionId==r.ActionId&&r.Started<r.Grants.Count&&!IsNamedAmbushUseIneffective(u)&&_winner==Winner.None;
 private bool IsMouJjEffectRound(long id)=>LifecycleCardUse(id)?.MouJiangJiRounds is{Started:>0};
 private bool IsTargetScopedMouJjEffect(long id)=>LifecycleCardUse(id)is{MouJiangJiRounds:{Active:true,Started:>0}r,Action:{}a}&&r.ActionId==a.ActionId;
 private bool IsMouJjEffectSubset(CardUseFrame use,IReadOnlyList<int> targets)=>use is{MouJiangJiRounds:{Active:true,Started:>0}r,Action:{}a}&&a.ActionId==r.ActionId&&r.ActiveIndex>=0&&r.ActiveIndex<use.TargetSeats.Count&&use.TargetSeats[r.ActiveIndex]==r.ActiveTarget&&targets.SequenceEqual(use.CardKind==CardKind.BorrowedSword?use.TargetSeats.Skip(r.ActiveIndex).Take(2):[r.ActiveTarget])&&ProgramEventHistory<MouJiangJiEffectStartedEvent>().Count(e=>e.UseId==use.Id&&e.ActionId==a.ActionId&&e.FrameId==r.Grants[r.Started-1].FrameId&&e.Round==r.Started&&e.Target==r.ActiveTarget)==1;
 private void CompleteMouJjEffect(long id)
 {
  if(LifecycleCardUse(id)is not{MouJiangJiRounds:{Active:true}}use)return;
  if(_resolutionStack.LastOrDefault()?.Id!=id||use.NativeDraw is not null||use.NativeCardEffectDraw is not null||use.WumiaoFlood is not null||use.DragonBoatCardEffect is not null||use.GuanduCardEffect is not null)throw new InvalidOperationException("Yingshi cannot retire its target-local native effect before actual children return.");
  var state=MouJiangJi.CompleteEffect(use);UpdateLifecycleCardUse(id,u=>u with{MouJiangJiRounds=state,TargetIndex=u.TargetSeats.Count,SequentialTrick=null});
 }
 private bool TryBeginMouJjEffect(long id,bool afterAttack)
 {
  if(!HasUnstartedMouJjEffect(id)||LifecycleCardUse(id)is not{MouJiangJiRounds:{}r,Action:{}a}use||use.ExtraCardEffect is{WholeGroupRepeated:false}||HasUnstartedFeiYiExtraEffect(id)||HasUnstartedZixiExtraEffect(id)||HasRemainingSequentialTrickTargets(id))return false;
  if(_resolutionStack.LastOrDefault()?.Id!=id)throw new InvalidOperationException("Yingshi cannot pass its original whole native effect.");
  var state=MouJiangJi.BeginEffect(use,NativeExtraEffectState(use));ReplaceRuntimeFrame(id,use with{MouJiangJiRounds=state,TargetIndex=state.ActiveIndex<0?use.TargetSeats.Count:state.ActiveIndex,Step=ResolutionFrameStep.ResolvingEffect,SequentialTrick=null,FireAttackSelection=null,ColorFireAttack=null,UnexpectedAssaultReveal=null,VirtualBasicEffectApplied=false});
  if(state.ActiveIndex<0||!_players[state.ActiveTarget].IsAlive){CompleteMouJjEffect(id);return false;}
  AdvanceEventRulesAndQueueFact(new ExtraCardEffectStartedEvent(id,a.ActionId,state.ActiveIndex,state.ActiveTarget,use.CardKind));
  if(afterAttack){ActiveCardAttack=null;ActiveDuel=null;ClearPendingDecision();}ActiveGroupCard=null;ActiveBorrowedSword=null;
  var card=GetTrickRepresentation(id,use.CardId,requireProcessing:true);var trick=state.Native.Trick!;IReadOnlyList<int> targets=use.CardKind==CardKind.BorrowedSword?use.TargetSeats.Skip(state.ActiveIndex).Take(2).ToArray():[state.ActiveTarget];BeginNullificationWindow(id,card,use.SourceSeat,targets,trick.ActionKind,null,trick.RequiredCardKind,use.CardKind);return true;
 }
 private void AssertMouJjEffects(){foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.MouJiangJiRounds is not null))if(!IsOrdinaryTrick(use.CardKind)||!MouJiangJi.ValidEffects(use))throw new InvalidOperationException("Yingshi lost its original Use, once-issued selected target or ordered native effect return.");}
}
