using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string GongjieGiftReason="skill-program.cao-yu.gongjie.obtain",GongjieDrawReason="skill-program.cao-yu.gongjie.draw",XiangxuDrawReason="skill-program.cao-yu.xiangxu.draw",XiangxuDiscardReason="skill-program.cao-yu.xiangxu.discard",XiangzuGiftReason="skill-program.cao-yu.xiangzu.give";
 [ProgramCapability]private sealed class CaoYuCapability(GameEngine e):ICaoYuHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginCaoYu(f,op);}
 private IReadOnlyList<Card> CaoYuGiftCards(int owner,int chooser)=>Array.AsReadOnly(GetHand(_players[owner]).Concat(GetEquipment(owner)).Where(c=>!c.IsGeneralWeapon&&!IsForeignEquipmentDiscardPrevented(chooser,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Transfer)).OrderBy(c=>_cardZones.GetLocation(c.Id).Zone).ThenBy(c=>c.Id).ToArray());
 private IReadOnlyList<Card> CaoYuDiscardCards(int owner)=>Array.AsReadOnly(GetHand(_players[owner]).Where(c=>!IsForeignEquipmentDiscardPrevented(owner,c,CardLocation.Hand(owner),OwnedCardMoveIntent.Discard)&&!IsSelfHandCategoryDiscardForbidden(owner,c,CardLocation.Hand(owner),OwnedCardMoveIntent.Discard)).OrderBy(c=>c.Id).ToArray());
 private SkillProgramStepOutcome BeginCaoYu(ProgramSkillFrame input,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(input.Id);var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,f.WindowContext?.OccurrenceIndex??0);
  if(f.CaoYu is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context||!QualifyCaoYu(c,GetProgramTrigger(f),context))throw new InvalidOperationException("CaoYu requires its exact original round, causal minimum, issued ending or dying candidate.");
  if(op==SkillProgramEffectOp.ResolveXiangxuMinimumTransition)
  {var edge=CaoYuTransition(c,context.CaoYuOpportunityId)!;if(ProgramEventHistory<CaoYuMinimumArmedEvent>().Any(e=>e.OpportunityId==edge.OpportunityId))throw new InvalidOperationException("Xiangxu cannot arm a transition twice.");AdvanceEventRulesAndQueueFact(new CaoYuMinimumArmedEvent(f.Id,edge.OpportunityId,edge.Source,edge.GameplayHash));return SkillProgramStepOutcome.Continue;}
  var dying=0L;if(op==SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery){if(!CaoYuDyingEntry(f,out var d))throw new InvalidOperationException("Xiangzu lost its genuine original dying occurrence.");dying=d.Id;}
  var stage=op switch{SkillProgramEffectOp.ResolveGongjieRoundFirstGifts=>CaoYuStage.GongjieTargets,SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment=>CaoYuStage.XiangxuChoice,_=>CaoYuStage.XiangzuRecipient};
  var r=new CaoYuReceipt(CaoYuSource(f),f.GameplayHash,op,context.ParentFrameId,_turnNumber,_turnProgression.OwnerSeat,_roundNumber,context.CaoYuOpportunityId??0,dying,stage,[],[],[],[]);
  ReplaceRuntimeTop(f=f with{CaoYu=r});AdvanceEventRulesAndQueueFact(new CaoYuStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.WindowId,r.ActualTurn,r.TurnOwner,r.Round,r.OpportunityId,r.DyingId));PublishCaoYu(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private IReadOnlyList<PromptChoice> CaoYuChoices(ProgramSkillFrame f)
 {
  var r=f.CaoYu!;var choices=new List<PromptChoice>();Dictionary<string,string> Args(string branch)=>new(){["program-action"]="cao-yu",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch};
  PromptChoice C(string branch,string label,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null,string suffix="")=>new(new($"cao-yu.{f.Id}.{r.Stage}.{branch}.{suffix}"),label,cards??[],targets??[],Args(branch));
  if(r.Stage==CaoYuStage.GongjieTargets)
  {foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat)){var selected=r.Targets.Contains(p.Seat);choices.Add(C(selected?"unselect-target":"target",(selected?"取消":"选择")+p.Name,targets:[p.Seat],suffix:p.Seat.ToString(CultureInfo.InvariantCulture)));}if(r.Targets.Count>0)choices.Add(C("targets-done","确认受益者"));choices.Add(C("cancel","取消恭节"));}
  else if(r.Stage==CaoYuStage.GongjieTake)
  {
   var target=r.Targets[r.Index];foreach(var old in BuildOwnedCardPaymentChoices(f.Id,target,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,canSelect:(_,card)=>!card.IsGeneralWeapon,destination:SkillProgramCardDestination.SelectedTargetHand,destinationSeat:target))choices.Add(old with{Id=new($"cao-yu.{f.Id}.take.{old.Id.Value}"),Parameters=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(old.Parameters){["program-action"]="cao-yu",["branch"]="take"})});
  }
  else if(r.Stage==CaoYuStage.XiangxuChoice)
  {var own=GetHand(_players[f.OwnerSeat]).Count;var other=GetHand(_players[r.TurnOwner]).Count;if(other>=own||CaoYuDiscardCards(f.OwnerSeat).Count>=own-other)choices.Add(C("adjust","按当前回合角色的手牌调整"));choices.Add(C("decline","放弃这一次相胥"));}
  else if(r.Stage==CaoYuStage.XiangxuDiscardChoice)
  {foreach(var card in CaoYuDiscardCards(f.OwnerSeat)){var selected=r.Selected.Contains(card.Id);if(!selected&&r.Selected.Count>=r.Requested)continue;choices.Add(C(selected?"unpick":"pick",(selected?"取消":"选择")+card.DisplayName,[card.Id],suffix:card.Id.ToString(CultureInfo.InvariantCulture)));}if(r.Selected.Count==r.Requested)choices.Add(C("discard","确认整次弃牌"));}
  else if(r.Stage==CaoYuStage.XiangzuRecipient)
  {foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))choices.Add(C("recipient","交给"+p.Name,targets:[p.Seat],suffix:p.Seat.ToString(CultureInfo.InvariantCulture)));choices.Add(C("cancel","取消襄胙"));}
  else if(r.Stage==CaoYuStage.XiangzuCards)
  {foreach(var card in CaoYuGiftCards(f.OwnerSeat,f.OwnerSeat)){var selected=r.Selected.Contains(card.Id);choices.Add(C(selected?"unpick":"pick",(selected?"取消":"选择")+card.DisplayName,[card.Id],suffix:card.Id.ToString(CultureInfo.InvariantCulture)));}if(r.Selected.Count>0)choices.Add(C("give","确认交牌"));choices.Add(C("cancel","取消襄胙"));}
  return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
 }
 private void PublishCaoYu(ProgramSkillFrame f)
 {
  var r=f.CaoYu!;if(r.Stage==CaoYuStage.GongjieTake&&(!_players[f.OwnerSeat].IsAlive||!r.Paid&&!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||r.Index>=r.Targets.Count)){AfterGongjieGifts(f);return;}
  if(r.Stage==CaoYuStage.GongjieTake&&(!_players[r.Targets[r.Index]].IsAlive||CaoYuGiftCards(f.OwnerSeat,r.Targets[r.Index]).Count==0)){AdvanceGongjieDonor(f);return;}
  var chooser=r.Stage==CaoYuStage.GongjieTake?r.Targets[r.Index]:f.OwnerSeat;var choices=CaoYuChoices(f);if(choices.Count==0){FinishCaoYu(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,skill.Name,choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=r.Stage!=CaoYuStage.GongjieTargets&&r.Stage!=CaoYuStage.XiangzuRecipient,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("cao-yu")]
 private void AnswerCaoYu(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoYu:{}r}f||_pendingDecision?.PlayerSeat!=(r.Stage==CaoYuStage.GongjieTake?r.Targets[r.Index]:f.OwnerSeat)||!CaoYuChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("CaoYu requires its actual chooser and exact current opaque slot or private material set.");AssertCaoYu(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=choice.Parameters["branch"];
  if(branch=="cancel"){FinishCaoYu(f);return;}
  if(branch is"target"or"unselect-target")
  {var seat=choice.Targets.Single();AdvanceEventRulesAndQueueFact(new CaoYuTargetChoiceEvent(f.Id,ProgramEventHistory<CaoYuTargetChoiceEvent>().Count(e=>e.FrameId==f.Id),seat,branch=="target"));ReplaceRuntimeTop(f=f with{CaoYu=r with{Targets=branch=="target"?r.Targets.Append(seat).ToArray():r.Targets.Where(s=>s!=seat).ToArray()}});PublishCaoYu(f);return;}
  if(branch=="targets-done")
  {var targets=r.Targets.OrderBy(s=>RelativeTableOrder(r.TurnOwner, s)).ToArray();for(var i=0;i<targets.Length;i++)AdvanceEventRulesAndQueueFact(new CaoYuTargetFrozenEvent(f.Id,i,targets[i]));ReplaceRuntimeTop(f=f with{CaoYu=r with{Targets=targets,Stage=CaoYuStage.GongjieTake}});PublishCaoYu(f);return;}
  if(branch=="take")
  {if(!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Gongjie lost the original source slot.");var card=_cardZones.CardsAt(new(zone,f.OwnerSeat))[slot];PayCaoYuCards(f,[card.Id],r.Targets[r.Index],CaoYuStage.GongjieGiftChildren,GongjieGiftReason);ContinueCaoYu(f.Id);return;}
  if(branch=="recipient"){ReplaceRuntimeTop(f=f with{CaoYu=r with{Target=choice.Targets.Single(),Stage=CaoYuStage.XiangzuCards}});PublishCaoYu(f);return;}
  if(branch is"pick"or"unpick")
  {var id=choice.Cards.Single();ReplaceRuntimeTop(f=f with{CaoYu=r with{Selected=branch=="pick"?r.Selected.Append(id).ToArray():r.Selected.Where(c=>c!=id).ToArray()}});PublishCaoYu(f);return;}
  if(branch=="decline"){AdvanceEventRulesAndQueueFact(new CaoYuOpportunityConsumedEvent(f.Id,r.OpportunityId,r.Source,false));FinishCaoYu(f);return;}
  if(branch=="adjust")
  {var own=GetHand(_players[f.OwnerSeat]).Count;var other=GetHand(_players[r.TurnOwner]).Count;var discard=own>other;var requested=discard?own-other:Math.Max(0,Math.Min(5,other)-own);AdvanceEventRulesAndQueueFact(new CaoYuOpportunityConsumedEvent(f.Id,r.OpportunityId,r.Source,true));AdvanceEventRulesAndQueueFact(new CaoYuXiangxuComparisonEvent(f.Id,r.OpportunityId,r.TurnOwner,own,other,discard?"discard":"draw",requested));AdvanceEventRulesAndQueueFact(new CaoYuRecipientHistoryEvent(f.Id,f.OwnerSeat,"ol:xiangxu",r.TurnOwner,r.ActualTurn));ReplaceRuntimeTop(f=f with{CaoYu=r with{Paid=true,Requested=requested,Selected=[],Stage=discard?CaoYuStage.XiangxuDiscardChoice:CaoYuStage.XiangxuDrawChildren}});if(discard)PublishCaoYu(f);else DrawCaoYu(f,requested,CaoYuStage.XiangxuDrawChildren,XiangxuDrawReason);return;}
  if(branch=="discard"){PayCaoYuCards(f,r.Selected,null,CaoYuStage.XiangxuDiscardChildren,XiangxuDiscardReason);ContinueCaoYu(f.Id);return;}
  if(branch=="give")
  {if(XiangzuSpent(f.OwnerSeat)||r.Target is not{}target||r.Selected.Count==0)throw new InvalidOperationException("Xiangzu cannot spend its permanent limited right twice or give zero entities.");var history=CaoYuHistory(f.OwnerSeat,"ol:gongjie",target)&&CaoYuHistory(f.OwnerSeat,"ol:xiangxu",target);AdvanceEventRulesAndQueueFact(new CaoYuXiangzuSpentEvent(f.Id,r.Source,r.GameplayHash,r.DyingId,target,r.Selected.Count,history));ReplaceRuntimeTop(f=f with{CaoYu=r with{Paid=true,HistoryQualified=history}});PayCaoYuCards(f,r.Selected,target,CaoYuStage.XiangzuGiftChildren,XiangzuGiftReason);ContinueCaoYu(f.Id);return;}
  throw new InvalidOperationException("Unknown CaoYu choice.");
 }
 private void PayCaoYuCards(ProgramSkillFrame f,IReadOnlyList<int> ids,int? target,CaoYuStage stage,string reason)
 {
  var r=f.CaoYu!;var materials=ids.Select(id=>{var card=GetAdvancedCard(id);return new CaoYuMaterial(id,card.Kind,_cardZones.GetLocation(id),EffectiveSuit(_players[f.OwnerSeat],card));}).ToArray();var grains=materials.Any(c=>c.Kind==CardKind.WoodenOx&&c.From==CardLocation.Equipment(f.OwnerSeat))?GetWoodenOxGrain(_players[f.OwnerSeat]).Select(c=>c.Id).ToArray():[];var before=_movementSequence;
  ReplaceRuntimeTop(f=f with{CaoYu=r with{Stage=stage,Materials=materials,GrainIds=grains,Before=before,After=before,Count=0,Target=target??r.Target,Paid=true},PendingMovementContinuation=new(f.OwnerSeat,0,null)});for(var i=0;i<materials.Length;i++){var c=materials[i];AdvanceEventRulesAndQueueFact(new CaoYuMaterialFrozenEvent(f.Id,stage,r.Index,i,c.CardId,c.Kind,c.From,c.Suit));}for(var i=0;i<grains.Length;i++)AdvanceEventRulesAndQueueFact(new CaoYuOxGrainFrozenEvent(f.Id,stage,r.Index,i,grains[i]));
  void Paid(long batch,IReadOnlyList<CardMovementRecord> moves){foreach(var m in moves){var c=materials.Single(c=>c.CardId==m.CardId);AdvanceEventRulesAndQueueFact(new CaoYuCardPaidEvent(f.Id,stage,r.Index,c.CardId,c.Kind,c.From,m.To,c.Suit,m.Sequence,batch));}if(stage==CaoYuStage.GongjieGiftChildren&&moves.Count==1)AdvanceEventRulesAndQueueFact(new CaoYuRecipientHistoryEvent(f.Id,f.OwnerSeat,"ol:gongjie",target!.Value,r.ActualTurn));}
  if(target is{}seat)MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(seat), new(reason), (stage==CaoYuStage.XiangzuGiftChildren ? NativeGiveCompletionRecords(f.Id,f.OwnerSeat,seat,ids,_movementSequence,Paid) : Paid));else MoveProgramLogicalDiscard(f,f.OwnerSeat,ids,new(reason),Paid);RecordCaoYuMovement(f.Id);
 }
 private void RecordCaoYuMovement(long id)
 {
  var f=GetActiveProgramFrame(id);var r=f.CaoYu!;var count=r.Stage is CaoYuStage.GongjieDrawChildren or CaoYuStage.XiangxuDrawChildren?_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=_movementSequence&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==(r.Stage==CaoYuStage.GongjieDrawChildren?GongjieDrawReason:XiangxuDrawReason)):ProgramEventHistory<CaoYuCardPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.Index==r.Index&&e.Sequence>r.Before&&e.Sequence<=_movementSequence);
  ReplaceRuntimeTop(f=f with{CaoYu=r with{After=_movementSequence,Count=count}});AdvanceEventRulesAndQueueFact(new CaoYuMovementPaidEvent(id,r.Stage,r.Index,f.OwnerSeat,r.Before,_movementSequence,count));
 }
 private void DrawCaoYu(ProgramSkillFrame f,int count,CaoYuStage stage,string reason)
 {var r=f.CaoYu!;ReplaceRuntimeTop(f=f with{CaoYu=r with{Stage=stage,Before=_movementSequence,After=_movementSequence,Count=0,Materials=[],GrainIds=[]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(_players[f.OwnerSeat].IsAlive&&count>0)DrawProgramCards(f.Id,f.OwnerSeat,count,null,null,SkillProgramCardSetVisibility.Private,new(reason));RecordCaoYuMovement(f.Id);ContinueCaoYu(f.Id);}
 private void RecoverCaoYu(ProgramSkillFrame f,int count,CaoYuStage stage)
 {var r=f.CaoYu!;ReplaceRuntimeTop(f=f with{CaoYu=r with{Stage=stage,Before=_movementSequence,After=_movementSequence,Count=0,Recovery=count,Materials=[],GrainIds=[]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new CaoYuRecoveryIssuedEvent(f.Id,stage,count));if(count>0&&_players[f.OwnerSeat].IsAlive)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,count,null,null);RecordCaoYuMovement(f.Id);ContinueCaoYu(f.Id);}
 private bool DrainCaoYu(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
 private void ContinueCaoYu(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoYu:{}r}f||f.Id!=id)return;AssertCaoYu(f);
  if(r.Stage is CaoYuStage.GongjieTargets or CaoYuStage.GongjieTake or CaoYuStage.XiangxuChoice or CaoYuStage.XiangxuDiscardChoice or CaoYuStage.XiangzuRecipient or CaoYuStage.XiangzuCards){if(_pendingDecision is null)PublishCaoYu(f);return;}
  if(DrainCaoYu(f))return;ReplaceRuntimeTop(f=GetActiveProgramFrame(id) with{PendingMovementContinuation=null});if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishCaoYu(f);return;}
  if(r.Stage==CaoYuStage.GongjieGiftChildren){AdvanceGongjieDonor(f);return;}
  if(r.Stage==CaoYuStage.XiangxuDiscardChildren&&r.Count>=2){RecoverCaoYu(f,1,CaoYuStage.XiangxuRecoveryChildren);return;}
  if(r.Stage==CaoYuStage.XiangzuGiftChildren&&r.HistoryQualified){RecoverCaoYu(f,r.Count,CaoYuStage.XiangzuRecoveryChildren);return;}
  FinishCaoYu(f);
 }
 private void FinishCaoYu(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new CaoYuCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{CaoYu=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,27440)]private bool ResumeCaoYu(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoYu:not null}f||f.Id!=id)return false;ContinueCaoYu(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,27440)]private bool ReturnCaoYu(ProgramSkillFrame f){if(f.CaoYu is null)return false;ContinueCaoYu(f.Id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27440)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27440)]private bool ContinuePaidCaoYu(ProgramSkillFrame f){if(f.CaoYu is not{}r||!r.Paid&&!(r.Op==SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment&&_players[f.OwnerSeat].IsAlive))return false;AssertCaoYu(f);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27441)]private bool ContinueIssuedCaoYu(ProgramSkillFrame f)=>f.CaoYu is null&&f.InstructionIndex is 0 or 1&&f.WindowContext is{}c&&(IsXiangxuMinimumCandidate(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex),c)||IsIssuedXiangxuEndingCandidate(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex),c));
 [ProgramAiChoice(SkillProgramEffectOp.ResolveGongjieRoundFirstGifts,SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment,SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery)]
 private PromptChoice AiCaoYu(PendingDecision p,ProgramSkillFrame f)
 {
  var r=f.CaoYu!;if(r.Stage==CaoYuStage.GongjieTargets)return p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="target"&&AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets[0]]))??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="targets-done")??p.Choices.Last();
  if(r.Stage==CaoYuStage.XiangzuRecipient)return p.Choices.FirstOrDefault(c=>c.Targets.Count>0&&CaoYuHistory(f.OwnerSeat,"ol:gongjie",c.Targets[0])&&CaoYuHistory(f.OwnerSeat,"ol:xiangxu",c.Targets[0]))??p.Choices.First();
  if(r.Stage==CaoYuStage.XiangzuCards)return p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="pick")??p.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="give");
  if(r.Stage==CaoYuStage.XiangxuDiscardChoice)return p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="discard")??p.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="pick");return p.Choices.First();
 }
}
