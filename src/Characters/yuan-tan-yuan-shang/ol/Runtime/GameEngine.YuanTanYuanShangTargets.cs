namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<int> NeifaPrimaryShape(CardUseFrame use)=>use.CardKind==CardKind.BorrowedSword?Array.AsReadOnly(use.Action!.TargetSeats.ToArray()):use.CardKind==CardKind.DrawTwo&&use.TargetSeats.Count==0?Array.AsReadOnly(new[]{use.Action!.ActorSeat}):Array.AsReadOnly(use.TargetSeats.ToArray());
 private bool ExactNeifaCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context,out CardUseFrame use)
 {
  use=null!;if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||context.CardUse is not{}identity||CurrentNeifa(c.OwnerSeat)is not{NonBasicBranch:true}state||state.Source.SkillId!=c.SkillId||state.Source.SkillInstanceId!=c.SkillInstanceId||state.GameplayHash!=c.GameplayHash||_resolutionStack.SingleOrDefault(f=>f.Id==context.ParentFrameId)is not ProgramCardTriggerWindowFrame w||w.ParentFrameId!=identity.ParentCardUseFrameId||w.CompletedResponseReturn is not null||GetCardActionWindow(w)!=SkillProgramTriggerWindow.CardUseTargetsFinalized||w.Continuation is not(ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard)||LifecycleCardUse(w.ParentFrameId)is not{Action:{Type:CardActionType.Use}a}native||a.ActionId!=identity.CardActionId||a.ActorSeat!=c.OwnerSeat||w.Action.ActionId!=a.ActionId||!SameOverflowAction(a,w.Action)||!HasExactAcceptedActualHandGainUse(native,a)||!DesignatedExtraTargetCandidateMatches(c,context,w)||_contentRegistry.GetSkill(c.SkillId).Program is not{}program||program.GameplayHash!=c.GameplayHash)return false;
  var trigger=program.Triggers.SingleOrDefault(t=>t.Id==c.BindingId);if(trigger?.Effects is not[{Op:var op}]||op==SkillProgramEffectOp.AdjustNeifaOrdinaryTrickTargets&&!IsOrdinaryTrick(native.CardKind)||op==SkillProgramEffectOp.DrawForFirstTwoPaidNeifaEquipmentUses&&(native.NeifaEquipment is not{}q||!ValidNeifaEquipment(q,native))||op is not(SkillProgramEffectOp.AdjustNeifaOrdinaryTrickTargets or SkillProgramEffectOp.DrawForFirstTwoPaidNeifaEquipmentUses))return false;
  var index=_resolutionStack.FindIndex(f=>f.Id==w.Id);if(index<1||_resolutionStack[index-1].Id!=native.Id)return false;use=native;return true;
 }
 private bool HasIssuedNeifaCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>ExactNeifaCandidate(c,w,out _);
 [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 8)]
 private void CollectIssuedNeifaCandidates(CardActionContext action,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
 {
  if(window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||action.Type!=CardActionType.Use||CurrentNeifa(action.ActorSeat)is not{NonBasicBranch:true}state||_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u=>u.Action?.ActionId==action.ActionId)is not{}use||!HasExactAcceptedActualHandGainUse(use,action))return;
  var op=IsOrdinaryTrick(action.EffectiveKind)?SkillProgramEffectOp.AdjustNeifaOrdinaryTrickTargets:GetProgramCardCategory(action.EffectiveKind)==SkillProgramCardCategory.Equipment&&use.NeifaEquipment is{Ordinal:1 or 2}?SkillProgramEffectOp.DrawForFirstTwoPaidNeifaEquipmentUses:(SkillProgramEffectOp?)null;if(op is null)return;
  var program=_contentRegistry.GetSkill(state.Source.SkillId).Program!;
  foreach(var t in program.Triggers.Where(t=>t.Window==window&&t.Effects is[{Op:var actual}]&&actual==op))
  {
   result.RemoveAll(c=>c.OwnerSeat==action.ActorSeat&&c.SkillId==state.Source.SkillId&&c.TriggerId==t.Id&&c.SkillInstanceId!=state.Source.SkillInstanceId);
   if(result.Any(c=>c.OwnerSeat==action.ActorSeat&&c.SkillId==state.Source.SkillId&&c.TriggerId==t.Id&&c.SkillInstanceId==state.Source.SkillInstanceId))continue;
   var target=action.TargetSeats.Count==1?action.TargetSeats[0]:-1;result.Add(new(action.ActorSeat,target,state.Source.SkillId,t.Id,state.GameplayHash,state.Source.SkillInstanceId,t.Priority,CreateCardActionProgramContext(action,window,0,action.ActorSeat,target,CaptureProgramTriggerFacts(_players[action.ActorSeat],action))));
  }
 }
 private IReadOnlyList<PromptChoice> NeifaTargetChoices(ProgramSkillFrame f)
 {
  var r=f.Neifa!;if(r.UseId is not{}id||LifecycleCardUse(id)is not{Action:{}a}use||a.ActionId!=r.ActionId||!NeifaPrimaryShape(use).SequenceEqual(r.OriginalTargets))throw new InvalidOperationException("Neifa target choice lost its exact native original target shape.");
  var original=r.OriginalTargets;var pair=use.CardKind==CardKind.BorrowedSword?2:1;if(original.Count%pair!=0)throw new InvalidOperationException("Neifa cannot split BorrowedSword pairs.");
  var primary=original.Where((_,i)=>i%pair==0).ToHashSet();var choices=new List<PromptChoice>();
  foreach(var target in _players.Where(p=>!primary.Contains(p.Seat)&&CanBeUniqueLeaderTrickTarget(use,p)).OrderBy(p=>p.Seat))
  {
   if(pair==2){foreach(var victim in _players.Where(p=>IsLegalBorrowedSwordSlashTarget(target,p)).OrderBy(p=>p.Seat))choices.Add(NeifaChoice(f,"add",$"增加：{target.Name} 对 {victim.Name} 使用杀",targets:[target.Seat,victim.Seat]));}
   else choices.Add(NeifaChoice(f,"add","增加目标："+target.Name,targets:[target.Seat]));
  }
  for(var i=0;i<original.Count;i+=pair)choices.Add(NeifaChoice(f,"remove","减少目标："+_players[original[i]].Name,targets:original.Skip(i).Take(pair).ToArray()));
  choices.Add(NeifaChoice(f,"decline","不改变目标"));return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
 }
 private void ResolveNeifaTargets(ProgramSkillFrame f,PromptChoice choice)
 {
  var r=f.Neifa!;if(r.UseId is not{}id||LifecycleCardUse(id)is not{Action:{}a}use||a.ActionId!=r.ActionId||f.TriggerId is not{}binding||f.WindowContext is not{}context||!ExactNeifaCandidate(new(f.OwnerSeat,f.SkillId,binding,f.SkillInstanceId,f.GameplayHash,0),context,out _)||!NeifaTargetChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Neifa target adjustment cannot replay an expired right or illegal pair.");
  var branch=choice.Parameters["branch"];var targets=r.OriginalTargets.ToList();var pair=use.CardKind==CardKind.BorrowedSword?2:1;
  if(branch=="add")targets.AddRange(choice.Targets);
  else if(branch=="remove"){var index=Enumerable.Range(0,targets.Count/pair).Select(i=>i*pair).Single(i=>targets.Skip(i).Take(pair).SequenceEqual(choice.Targets));targets.RemoveRange(index,pair);}
  var action=CloneDesignatedExtraTargetAction(a,targets);UpdateProgramRoleCardUse(use with{TargetSeats=Array.AsReadOnly(targets.ToArray()),Action=action,TargetsAdjusted=true,Enhancements=branch=="add"?use.Enhancements|CurrentCardEnhancement.ExtraTarget:use.Enhancements},action);
  AdvanceEventRulesAndQueueFact(new NeifaTargetsResolvedEvent(f.Id,use.Id,a.ActionId,r.Source,r.GameplayHash,branch){OriginalTargets=r.OriginalTargets,ResultTargets=targets});if(branch=="add")RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!,[choice.Targets[0]]);FinishNeifa(GetActiveProgramFrame(f.Id));
 }
 private bool HasNeifaTrickTargetCancellation(CardUseFrame use)=>use.TargetSeats.Count==0&&use.Action is{}a&&IsOrdinaryTrick(use.CardKind)&&CompleteProgramEventHistory().OfType<NeifaTargetsResolvedEvent>().Any(e=>e.UseId==use.Id&&e.ActionId==a.ActionId&&e.Branch=="remove"&&e.ResultTargets.Count==0&&e.OriginalTargets.Count==(use.CardKind==CardKind.BorrowedSword?2:1)&&IsNeifaTargetsResolvedFact(use,e,e.OriginalTargets));
 private bool IsNeifaTargetsResolvedFact(CardUseFrame use,NeifaTargetsResolvedEvent fact,IReadOnlyList<int> original)
 {
  if(use.Action is not{}a||fact.UseId!=use.Id||fact.ActionId!=a.ActionId||fact.Source.OwnerSeat!=a.ActorSeat||!IsOrdinaryTrick(use.CardKind)||!fact.OriginalTargets.SequenceEqual(original)||_contentRegistry.GetSkill(fact.Source.SkillId).Program is not{}p||p.GameplayHash!=fact.GameplayHash||p.Triggers.SingleOrDefault(t=>t.Id==fact.Source.BindingId)?.Effects is not[{Op:SkillProgramEffectOp.AdjustNeifaOrdinaryTrickTargets}]||CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==fact.FrameId&&e.OwnerSeat==fact.Source.OwnerSeat&&e.SkillId==fact.Source.SkillId&&e.BindingId==fact.Source.BindingId&&e.SkillInstanceId==fact.Source.SkillInstanceId&&e.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized)!=1||CompleteProgramEventHistory().OfType<NeifaTargetsResolvedEvent>().Count(e=>e.FrameId==fact.FrameId&&e.UseId==use.Id)!=1)return false;
  var pair=use.CardKind==CardKind.BorrowedSword?2:1;var result=fact.ResultTargets;if(original.Count%pair!=0||result.Count%pair!=0||result.Any(s=>!IsValidPlayerSeat(s))||result.Where((_,i)=>i%pair==0).Distinct().Count()!=result.Count/pair)return false;
  return fact.Branch switch{"decline"=>result.SequenceEqual(original),"add"=>result.Count==original.Count+pair&&result.Take(original.Count).SequenceEqual(original)&&!original.Where((_,i)=>i%pair==0).Contains(result[^pair])&&(pair==1||result[^2]!=result[^1]),"remove"=>result.Count==original.Count-pair&&Enumerable.Range(0,original.Count/pair).Any(i=>original.Take(i*pair).Concat(original.Skip((i+1)*pair)).SequenceEqual(result)),_=>false};
 }
}
