using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
 private const string YanXiangAction="anniversary-yan-xiang",YanXiangDiscardReason="skill-program.anniversary:yuzheng.discard",YanXiangDrawReason="skill-program.anniversary:yuzheng.draw";
 private static CardConversionSource YanXiangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 [ProgramCapability]private sealed class AnniversaryYanXiangHost(GameEngine e):IAnniversaryYanXiangHost
 {public SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginYanXiang(f,op);}
 private int[] YanXiangAvailableOptions(int owner,string instance)=>new[]{1,2}.Where(option=>!ProgramEventHistory<AnniversaryYanXiangOptionChosenEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Source.SkillInstanceId==instance&&e.Turn==_turnNumber&&e.Phase==_cardUseDebitPhaseInstanceId&&e.Option==option)).ToArray();
 private Card[] YanXiangDiscardableHand(int actor)=>GetHand(_players[actor]).Where(c=>!IsSelfHandCategoryDiscardForbidden(actor,c,CardLocation.Hand(actor),OwnedCardMoveIntent.Discard)).OrderBy(c=>c.Id).ToArray();
 [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,46800,SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng)]
 private bool CanActivateYanXiang(ProgramActivationLegalityContext c)=>c.Owner.IsAlive&&c.OwnerSeat==_currentSeat&&_phase==TurnPhase.Play&&GetSkillBindingShard(c.Owner).ProgramInstances.Where(i=>i.SkillId==AnniversaryYanXiangComposition.Yuzheng).Any(i=>YanXiangAvailableOptions(c.OwnerSeat,i.SkillInstanceId).Length>0);
 private CardActionContext? YanXiangWindowAction(ProgramSkillWindowContext? context)=>context is null?null:_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)?.Action;
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,46800,SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward,SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi)]
 private bool CanOfferYanXiang(ProgramTriggerLegalityContext c)=>c.SkillId==AnniversaryYanXiangComposition.Yuzheng?
  DueYanXiangRewards(c.OwnerSeat,c.Candidate.SkillInstanceId,YanXiangWindowAction(c.WindowContext)).Length>0:
  c.WindowContext is{Window:SkillProgramTriggerWindow.TurnEnding,SourceSeat:{} actor}&&_players[actor].IsAlive&&GetHand(_players[actor]).Count>=_players[actor].Hp;
 private SkillProgramStepOutcome BeginYanXiang(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.AnniversaryYanXiang is not null||f.InstructionIndex!=1||!YanXiangOriginal(f,operation))throw new InvalidOperationException("Yan Xiang requires his original recipient activation, completed action or actual Ending candidate.");
  var actor=operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng?f.SelectedTargetSeats.Single():operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi?f.WindowContext!.SourceSeat!.Value:YanXiangWindowAction(f.WindowContext)!.ActorSeat;
  var stage=operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng?AnniversaryYanXiangStage.Option:operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi?AnniversaryYanXiangStage.DamageName:AnniversaryYanXiangStage.Drawing;
  var r=new AnniversaryYanXiangReceipt(YanXiangSource(f),f.GameplayHash,operation,f.WindowContext?.ParentFrameId??0,_turnNumber,_roundNumber,_cardUseDebitPhaseInstanceId,actor,stage);
  if(operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward)
  {
   var action=YanXiangWindowAction(f.WindowContext)!;var due=DueYanXiangRewards(f.OwnerSeat,f.SkillInstanceId,action);
   if(due.Length==0)throw new InvalidOperationException("Yuzheng's completed action lost its issued round reward.");
   r=r with{Requested=2,RewardAction=action.ActionId,RewardLeases=due.Select(l=>l.Id).ToArray()};
  }
  ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r});
  AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangStartedEvent(f.Id,r.Source,r.Hash,operation,r.ParentId,r.Turn,r.Round,r.Phase,actor));
  if(operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward)
  {
   foreach(var lease in r.RewardLeases)AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangRewardRequestedEvent(f.Id,lease,r.RewardAction));
   IssueYanXiangDraw(f);return SkillProgramStepOutcome.AwaitChild;
  }
  PublishYanXiang(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private int YanXiangChooser(ProgramSkillFrame f)=>f.AnniversaryYanXiang!.Stage==AnniversaryYanXiangStage.DamageName?f.OwnerSeat:f.AnniversaryYanXiang.Actor;
 private PromptChoice YanXiangChoice(ProgramSkillFrame f,string verb,string suffix,string text,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>
  new(new($"yan-xiang.{f.Id}.{verb}.{suffix}"),text,cards??[],targets??[],new Dictionary<string,string>{{"program-action",YanXiangAction},{"verb",verb},{"value",suffix},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
 private IReadOnlyList<PromptChoice> YanXiangChoices(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;
  if(r.Stage==AnniversaryYanXiangStage.Option)return Array.AsReadOnly(YanXiangAvailableOptions(f.OwnerSeat,f.SkillInstanceId).Select(n=>YanXiangChoice(f,"option",n.ToString(CultureInfo.InvariantCulture),n==1?"谕诤一：弃至全场最少，本轮下X次使用或打出后各摸二":"谕诤二：摸体力上限张牌，本轮增加手限且限制后续用牌数")).ToArray());
  if(r.Stage==AnniversaryYanXiangStage.HandCards)return Array.AsReadOnly(YanXiangDiscardableHand(r.Actor).Where(c=>!r.Cards.Contains(c.Id)).Select(c=>YanXiangChoice(f,"discard",c.Id.ToString(CultureInfo.InvariantCulture),"选择 "+c.DisplayName,[c.Id])).ToArray());
  if(r.Stage==AnniversaryYanXiangStage.DamageName)return Array.AsReadOnly(YanXiangDamageNames().Select(n=>YanXiangChoice(f,"name",n.ToString(),"声明【"+CardCatalog.Get(n).DisplayName+"】")).ToArray());
  if(r.Stage==AnniversaryYanXiangStage.UseChoice)return Array.AsReadOnly(YanXiangUsePlans(f).Select((p,i)=>YanXiangChoice(f,"use",i.ToString(CultureInfo.InvariantCulture),"将【"+GetAdvancedCard(p.CardId).DisplayName+"】当【"+CardCatalog.Get(p.Kind).DisplayName+"】使用",[p.CardId],p.Targets)).Append(YanXiangChoice(f,"decline","done","不以邃识使用牌")).ToArray());
  return [];
 }
 private void PublishYanXiang(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;var chooser=YanXiangChooser(f);var choices=YanXiangChoices(f);
  if(choices.Count==0){FinishYanXiang(f);return;}
  ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
  var text=r.Stage switch{AnniversaryYanXiangStage.Option=>"谕诤：由所选角色选择本阶段尚未使用的选项。",AnniversaryYanXiangStage.HandCards=>$"谕诤：选择{r.Required}张手牌弃置，已选{r.Cards.Count}张。",AnniversaryYanXiangStage.DamageName=>"邃识：声明一种伤害牌牌名。",_=>"邃识：由当前结束阶段角色选择自己的牌和合法目标，或放弃。"};
  _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,text,Array.AsReadOnly(choices.SelectMany(c=>c.Cards).Distinct().ToArray()),Array.AsReadOnly(choices.SelectMany(c=>c.Targets).Distinct().ToArray()),f.OwnerSeat)
   {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng,SkillProgramEffectOp.ResolveAnniversaryYanXiangSuishi)]private PromptChoice YanXiangAi(PendingDecision p)=>p.Choices.First();
 [ProgramAnswer(YanXiangAction)]private void AnswerYanXiang(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryYanXiang:{} r}f||!ValidYanXiang(f)||_pendingDecision?.PlayerSeat!=YanXiangChooser(f)||!AssistedChoicesEqual([choice],YanXiangChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Yan Xiang rejects another payer, phase option, private material or current native plan.");
  ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
  if(!_players[r.Actor].IsAlive){FinishYanXiang(f);return;}
  switch(choice.Parameters["verb"])
  {
   case "option":
    var option=int.Parse(choice.Parameters["value"],CultureInfo.InvariantCulture);
    AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangOptionChosenEvent(f.Id,r.Source,r.Hash,r.Turn,r.Phase,r.Actor,option));
    if(option==2)
    {ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Option=2,Requested=Math.Min(5,Math.Max(0,_players[r.Actor].MaxHp)),Stage=AnniversaryYanXiangStage.Drawing}});IssueYanXiangDraw(f);return;}
    var minimum=_players.Where(p=>p.IsAlive).Min(p=>GetHand(p).Count);var required=Math.Min(YanXiangDiscardableHand(r.Actor).Length,Math.Max(0,GetHand(_players[r.Actor]).Count-minimum));
    ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Option=1,Required=required,Stage=AnniversaryYanXiangStage.HandCards}});
    if(required==0){IssueYanXiangLease(f,0);FinishYanXiang(f);}else PublishYanXiang(f);return;
   case "discard":
    var ids=r.Cards.Append(choice.Cards.Single()).ToArray();ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r=r with{Cards=ids}});
    if(ids.Length<r.Required){PublishYanXiang(f);return;}
    PayYanXiangHand(f);return;
   case "name":
    var name=Enum.Parse<CardKind>(choice.Parameters["value"]);
    ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Name=name,Stage=AnniversaryYanXiangStage.UseChoice}});
    AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangNameDeclaredEvent(f.Id,r.Actor,name));PublishYanXiang(f);return;
   case "decline":FinishYanXiang(f);return;
   case "use":
    var plan=YanXiangUsePlans(f)[int.Parse(choice.Parameters["value"],CultureInfo.InvariantCulture)];
    ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Plan=plan,Stage=AnniversaryYanXiangStage.IssuingUse,Before=_movementSequence,After=_movementSequence}});IssueYanXiangUse(f);return;
  }
  throw new InvalidOperationException("Unknown Yan Xiang native choice.");
 }
 private void PayYanXiangHand(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;
  if(r.Cards.Count!=r.Required||!r.Cards.All(id=>YanXiangDiscardableHand(r.Actor).Any(c=>c.Id==id)))throw new InvalidOperationException("Yuzheng lost its dynamically payable actual hand adjustment.");
  var before=_movementSequence;ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Stage=AnniversaryYanXiangStage.MovementChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  MoveProgramLogicalDiscard(f,r.Actor,r.Cards,new(YanXiangDiscardReason),(_,moves)=>{var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{AnniversaryYanXiang=current.AnniversaryYanXiang! with{Moves=moves,After=_movementSequence}});AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangPaidEvent(f.Id,r.Actor,moves.Count,before,_movementSequence));});
  ContinueYanXiang(f.Id);AdvanceRulesAndPublishState();
 }
 private void IssueYanXiangDraw(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;
  if(r.Requested==0){IssueYanXiangLease(f,0);FinishYanXiang(f);return;}
  ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Stage=AnniversaryYanXiangStage.Drawing,Draw=null,Before=_movementSequence,After=_movementSequence}});
  BeginOperationProgramDraw(f,r.Actor,r.Requested,new(YanXiangDrawReason));
 }
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng,SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward)]
 private void ReturnYanXiangDraw(ProgramSkillFrame supplied,NativeDrawOutcome draw)
 {
  var f=GetActiveProgramFrame(supplied.Id);var r=f.AnniversaryYanXiang;
  if(r is not{Stage:AnniversaryYanXiangStage.Drawing}||!ValidYanXiang(f)||f.NativeDraw is not null||draw.RecipientSeat!=r.Actor||draw.RequestedCount!=r.Requested||draw.SequenceBefore!=r.Before)throw new InvalidOperationException("Yuzheng lost its original once-issued native draw.");
  ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Stage=AnniversaryYanXiangStage.MovementChildren,Draw=draw,After=draw.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangDrawReturnedEvent(f.Id,r.Cursor,r.Actor,r.Requested,draw.ActualDrawIds.Count,draw.SequenceBefore,draw.SequenceAfter));ContinueYanXiang(f.Id);
 }
 private void IssueYanXiangLease(ProgramSkillFrame f,int amount)
 {
  var r=f.AnniversaryYanXiang!;
  if(ProgramEventHistory<AnniversaryYanXiangLeaseEvent>().Any(e=>e.Id==f.Id))throw new InvalidOperationException("Yuzheng cannot issue the same paid round effect twice.");
  AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangLeaseEvent(f.Id,r.Source,r.Hash,r.Round,r.Actor,r.Option,amount,ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Count(),_cardActionSequence));
 }
 private void ContinueYanXiang(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryYanXiang:{} r}f||f.Id!=id)return;
  if(!ValidYanXiang(f))throw new InvalidOperationException("Yan Xiang lost his owning phase, payment or native child return.");
  if(r.Stage is AnniversaryYanXiangStage.Option or AnniversaryYanXiangStage.HandCards or AnniversaryYanXiangStage.DamageName or AnniversaryYanXiangStage.UseChoice){if(_pendingDecision is null)PublishYanXiang(f);return;}
  if(r.Stage is not(AnniversaryYanXiangStage.MovementChildren or AnniversaryYanXiangStage.NativeTail))return;
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(id)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id))return;
  f=GetActiveProgramFrame(id);r=f.AnniversaryYanXiang!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
  if(r.Operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng)
  {
   var actual=r.Option==2?r.Requested:ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Count(m=>m.From==CardLocation.Hand(r.Actor)&&ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(i=>i.InvocationId==m.InvocationId&&i.ParentFrameId==f.Id&&i.Reason.Value==YanXiangDiscardReason&&i.SequenceBefore>=r.Before&&i.SequenceAfter<=r.After));
   IssueYanXiangLease(f,actual);FinishYanXiang(f);return;
  }
  if(r.Operation==SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzhengReward&&r.Cursor+1<r.RewardLeases.Count)
  {ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Cursor=r.Cursor+1}});IssueYanXiangDraw(f);return;}
  FinishYanXiang(f);
 }
 private void FinishYanXiang(ProgramSkillFrame f)
 {
  ClearPendingDecision();ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=f.AnniversaryYanXiang! with{Stage=AnniversaryYanXiangStage.Completed},PendingMovementContinuation=null});
  AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangCompletedEvent(f.Id));AdvanceRuntimeProgram(f.Id);
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,46800)]private bool ResumeYanXiang(long id)
 {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryYanXiang:{Stage:not AnniversaryYanXiangStage.Completed}}f||f.Id!=id)return false;ContinueYanXiang(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,46800)]private bool ReturnYanXiangMovement(ProgramSkillFrame f)=>f.AnniversaryYanXiang is not null&&ResumeYanXiang(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,46800)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,46800)]
 private bool KeepYanXiangIssued(ProgramSkillFrame f)=>f.AnniversaryYanXiang is{} r?ValidYanXiang(f)&&r.Stage is not(AnniversaryYanXiangStage.Option or AnniversaryYanXiangStage.DamageName):
  f.SkillId==AnniversaryYanXiangComposition.Yuzheng&&f.TriggerId is"issued-use-reward" or"issued-response-reward"&&DueYanXiangRewards(f.OwnerSeat,f.SkillInstanceId,YanXiangWindowAction(f.WindowContext)).Length>0;
}
