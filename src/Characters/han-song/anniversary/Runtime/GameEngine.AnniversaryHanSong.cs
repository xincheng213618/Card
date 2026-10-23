using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string HanSongDiscardReason="anniversary.shuaiyan.discard",HanSongDrawReason="anniversary.shuaiyan.draw";
 [ProgramCapability] private sealed class AnniversaryHanSongCapability(GameEngine engine):IAnniversaryHanSongHost
 {public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame)=>engine.BeginAnniversaryHanSong(frame);}
 private static CardConversionSource HanSongSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private SkillProgramStepOutcome BeginAnniversaryHanSong(ProgramSkillFrame supplied)
 {
  var f=GetActiveProgramFrame(supplied.Id);var t=GetProgramTrigger(f);
  if(f.AnniversaryHanSong is not null||f.InstructionIndex!=1||f.WindowContext is not{} c||!ExactHanSongParent(f)||
   !CanOfferAnniversaryHanSong(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0),t,c))throw new InvalidOperationException("Shuaiyan lost its native phase subject or equal-hand opportunity.");
  var subject=c.SourceSeat!.Value;var count=subject==f.OwnerSeat?_players.Count(p=>p.IsAlive&&GetHand(p).Count==GetHand(_players[f.OwnerSeat]).Count):1;
  var r=new AnniversaryHanSongReceipt(HanSongSource(f),f.GameplayHash,c.ParentFrameId,_turnNumber,subject,count,subject==f.OwnerSeat?AnniversaryHanSongStage.Drawing:AnniversaryHanSongStage.BenefitChoice);
  ReplaceRuntimeTop(f=f with{AnniversaryHanSong=r});
  AdvanceEventRulesAndQueueFact(new AnniversaryHanSongStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.ActualTurn,subject,count));
  if(subject==f.OwnerSeat)IssueHanSongDraw(f);else PublishHanSong(f);
  return subject==f.OwnerSeat?SkillProgramStepOutcome.AwaitChild:SkillProgramStepOutcome.AwaitChoice;
 }
 private IEnumerable<(Card Card,CardLocation From,int Slot,bool Hidden)> HanSongDiscardOptions(ProgramSkillFrame f)
 {
  var subject=f.AnniversaryHanSong!.SubjectSeat;
  if(!_players[subject].IsAlive)yield break;
  foreach(var zone in new[]{CardZoneKind.Hand,CardZoneKind.Equipment})
  foreach(var item in _cardZones.CardsAt(new(zone,subject)).Select((card,index)=>(Card:card,From:new CardLocation(zone,subject),Slot:index,Hidden:zone==CardZoneKind.Hand)))
   if(!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,item.Card,item.From,OwnedCardMoveIntent.Discard)&&!IsSelfHandCategoryDiscardForbidden(f.OwnerSeat,item.Card,item.From,OwnedCardMoveIntent.Discard))yield return item;
 }
 private PromptChoice HanSongChoice(ProgramSkillFrame f,string verb,string suffix,string label,IReadOnlyList<int>? cards=null,IReadOnlyDictionary<string,string>? extra=null)
 {
  var p=new Dictionary<string,string>{{"program-action","anniversary-han-song"},{"verb",verb},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}};
  if(extra is not null)foreach(var pair in extra)p[pair.Key]=pair.Value;
  return new(new($"han-song.{f.Id}.{verb}.{suffix}"),label,cards??[],[],p);
 }
 private IReadOnlyList<PromptChoice> HanSongChoices(ProgramSkillFrame f)
 {
  if(f.AnniversaryHanSong is not{} r)return [];
  if(r.Stage==AnniversaryHanSongStage.BenefitChoice)
   return HanSongDiscardOptions(f).Any()?[HanSongChoice(f,"draw","one","摸一张牌"),HanSongChoice(f,"discard","one",$"弃置{_players[r.SubjectSeat].Name}的一张牌")]:[HanSongChoice(f,"draw","one","摸一张牌")];
  if(r.Stage!=AnniversaryHanSongStage.CardChoice)return [];
  return Array.AsReadOnly(HanSongDiscardOptions(f).Select(m=>HanSongChoice(f,"card",$"{m.From.Zone}.{m.Slot}",m.Hidden?$"第{m.Slot+1}张手牌":PublicPileCardLabel(m.Card),m.Hidden?[]:[m.Card.Id],
   new Dictionary<string,string>{{"source-zone",m.From.Zone.ToString()},{"slot-index",m.Slot.ToString(CultureInfo.InvariantCulture)},{"card-owner-seat",r.SubjectSeat.ToString(CultureInfo.InvariantCulture)}})).ToArray());
 }
 private void PublishHanSong(ProgramSkillFrame f)
 {
  var choices=HanSongChoices(f);if(choices.Count==0){FinishHanSong(f);return;}
  ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
  _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.AnniversaryHanSong!.Stage==AnniversaryHanSongStage.BenefitChoice?"率言：摸一张牌，或弃置该角色一张牌。":"率言：选择一张可弃置的牌；暗手牌只显示牌位。",Array.AsReadOnly(choices.SelectMany(c=>c.Cards).Distinct().ToArray()),[],f.OwnerSeat)
  {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan)]private PromptChoice HanSongAi(PendingDecision p)=>p.Choices.First();
 [ProgramAnswer("anniversary-han-song")]private void AnswerHanSong(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryHanSong:{} r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!ValidHanSong(f)||!AssistedChoicesEqual([choice],HanSongChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Shuaiyan's chooser, original phase subject or current owned card changed.");
  ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
  if(!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)){FinishProgramSkill(f,false);return;}
  if(r.Stage==AnniversaryHanSongStage.BenefitChoice)
  {
   if(choice.Parameters["verb"]=="draw"){IssueHanSongDraw(f);return;}
   ReplaceRuntimeTop(f=f with{AnniversaryHanSong=r with{Stage=AnniversaryHanSongStage.CardChoice}});PublishHanSong(f);return;
  }
  var selected=HanSongDiscardOptions(f).Single(m=>m.From.Zone.ToString()==choice.Parameters["source-zone"]&&m.Slot.ToString(CultureInfo.InvariantCulture)==choice.Parameters["slot-index"]);
  var before=_movementSequence;
  ReplaceRuntimeTop(f=f with{AnniversaryHanSong=r with{Stage=AnniversaryHanSongStage.DiscardChildren,DiscardId=selected.Card.Id,DiscardFrom=selected.From,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  AdvanceEventRulesAndQueueFact(new AnniversaryHanSongBenefitIssuedEvent(f.Id,false,1));
  MoveProgramLogicalDiscard(f,f.OwnerSeat,[selected.Card.Id],new(HanSongDiscardReason),(_,records)=>
  {
   var paid=records.Single();var active=GetActiveProgramFrame(f.Id);
   ReplaceRuntimeTop(active with{AnniversaryHanSong=active.AnniversaryHanSong! with{After=_movementSequence,DiscardSequence=paid.Sequence}});
   RecordBianFuRenForeignDiscardProducer(paid,f.OwnerSeat,f.Id);
   AdvanceEventRulesAndQueueFact(new AnniversaryHanSongDiscardPaidEvent(f.Id,r.SubjectSeat,paid.Sequence));
  });
  ContinueHanSong(f.Id);AdvanceRulesAndPublishState();
 }
 private void IssueHanSongDraw(ProgramSkillFrame f)
 {
  var r=f.AnniversaryHanSong!;
  ReplaceRuntimeTop(f=f with{AnniversaryHanSong=r with{Stage=AnniversaryHanSongStage.Drawing,Before=_movementSequence,After=_movementSequence}});
  AdvanceEventRulesAndQueueFact(new AnniversaryHanSongBenefitIssuedEvent(f.Id,true,r.DrawCount));
  BeginOperationProgramDraw(f,f.OwnerSeat,r.DrawCount,new(HanSongDrawReason));
 }
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan)]private void ReturnHanSongDraw(ProgramSkillFrame supplied,NativeDrawOutcome outcome)
 {
  var f=GetActiveProgramFrame(supplied.Id);var r=f.AnniversaryHanSong;
  if(r is null||r.Stage!=AnniversaryHanSongStage.Drawing||!ValidHanSong(f)||f.NativeDraw is not null||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=r.DrawCount||outcome.SequenceBefore!=r.Before||outcome.SequenceAfter>_movementSequence)throw new InvalidOperationException("Shuaiyan lost its original once-paid native draw return.");
  ReplaceRuntimeTop(f=f with{AnniversaryHanSong=r with{Stage=AnniversaryHanSongStage.DrawChildren,Draw=outcome,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  AdvanceEventRulesAndQueueFact(new AnniversaryHanSongDrawReturnedEvent(f.Id,outcome.ActualDrawIds.Count,outcome.SequenceBefore,outcome.SequenceAfter));ContinueHanSong(f.Id);
 }
 private void ContinueHanSong(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryHanSong:{} r}f||f.Id!=id)return;
  AssertHanSong(f);
  if(r.Stage is AnniversaryHanSongStage.BenefitChoice or AnniversaryHanSongStage.CardChoice){if(_pendingDecision is null)PublishHanSong(f);return;}
  if(r.Stage is not(AnniversaryHanSongStage.DrawChildren or AnniversaryHanSongStage.DiscardChildren))return;
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;
  FinishHanSong(GetActiveProgramFrame(id));
 }
 private void FinishHanSong(ProgramSkillFrame f)
 {
  ClearPendingDecision();ReplaceRuntimeTop(f=f with{AnniversaryHanSong=f.AnniversaryHanSong! with{Stage=AnniversaryHanSongStage.Complete},PendingMovementContinuation=null});
  AdvanceEventRulesAndQueueFact(new AnniversaryHanSongCompletedEvent(f.Id));AdvanceRuntimeProgram(f.Id);
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,45900)]private bool ResumeHanSong(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryHanSong:{Stage:not AnniversaryHanSongStage.Complete}}f||f.Id!=id)return false;ContinueHanSong(id);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,45900)]private bool ReturnHanSongMovement(ProgramSkillFrame f)=>f.AnniversaryHanSong is not null&&ResumeHanSong(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,45900)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,45900)]
 private bool ContinuePaidHanSong(ProgramSkillFrame f)=>f.AnniversaryHanSong is{Stage:AnniversaryHanSongStage.Drawing or AnniversaryHanSongStage.DrawChildren or AnniversaryHanSongStage.DiscardChildren}&&ValidHanSong(f);
}
