using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramCapability]private sealed class LiuZhangCapability(GameEngine e):ILiuZhangHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>op==SkillProgramEffectOp.ResolveZonghuRequiredUseRequest?e.ResolveZonghu(f):e.ResolveFengwei(f,op);}
 [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount,27512,SkillProgramEffectOp.ResolveZonghuRequiredUseRequest)]
 private bool IsZonghuOrdinaryActivation(ProgramActivationLegalityContext c)=>c.Activation.Id=="native-use-request"&&c.OwnerSeat==_currentSeat&&_phase==TurnPhase.Play&&ZonghuSources(c.Owner).Any()&&ZonghuVictims(c.OwnerSeat).Any()&&ZonghuOutputs(ZonghuPlayNeed(c.OwnerSeat)).Any();
 private static string ZonghuDecisionHash(PendingDecision p) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_Zonghu.ZonghuDecisionHash(p);
 private static bool SameZonghuNeed(ZonghuNeed a,ZonghuNeed b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_zhang_ol_Zonghu.SameZonghuNeed(a,b);
 private bool IsZonghuAnswer(ChoiceId id)=>_pendingDecision?.Choices.Any(c=>c.Id==id&&(c.Parameters.ContainsKey(ZonghuOpen)||c.Parameters.ContainsKey(ZonghuChoice)))==true;
 private CommandResult SubmitZonghuAnswer(int actor,PromptId prompt,ChoiceId id)
 {
  if(!_started)return Reject(CommandErrorCode.NotStarted,"游戏尚未开始。");
  if(_pendingDecision is not{}p||p.PlayerSeat!=actor||p.PromptId!=prompt)return Reject(CommandErrorCode.InvalidPrompt,"宗护必须回答当前真实请求。");
  var c=p.Choices.SingleOrDefault(c=>c.Id==id&&(c.Parameters.ContainsKey(ZonghuOpen)||c.Parameters.ContainsKey(ZonghuChoice)));if(c is null)return Reject(CommandErrorCode.InvalidChoice,"宗护选择未公布。");
  return Accept(()=>{AnswerZonghu(c);AdvanceRulesAndPublishState();if(_options.AdvanceAfterHumanCommands)AdvanceToHumanBoundary();});
 }
 private void OpenZonghu(PromptChoice c)
 {
  var p=_pendingDecision??throw new InvalidOperationException("Zonghu has no real native request.");var n=CaptureZonghuNeed(p)??throw new InvalidOperationException("Zonghu lost its exact native requester.");var source=RequireConversionSource(c);
  if(!p.Choices.Any(x=>AssistedChoicesEqual([x],[c]))||!ZonghuSources(_players[p.PlayerSeat]).Any(s=>s.Source==source)||!ZonghuVictims(p.PlayerSeat).Any()||!ZonghuOutputs(n).Any())throw new InvalidOperationException("Zonghu cannot replace an unavailable native request or source.");
  var hash=_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash;
  var f=new ProgramSkillFrame(++_resolutionSequence,p.PlayerSeat,source.SkillId,source.BindingId,hash,0,[],[]){SkillInstanceId=source.SkillInstanceId,Zonghu=new(source,hash,_turnNumber,_turnProgression.OwnerSeat,n,p,ZonghuStage.Declare)};
  ClearPendingDecision();PushRuntimeFrame(f);AdvanceEventRulesAndQueueFact(new ZonghuStartedEvent(f.Id,source,hash,_turnNumber,_turnProgression.OwnerSeat,n.Intent,n.OwnerFrameId,n.RequestFrameId,p.PromptId,p.Revision,ZonghuDecisionHash(p)));
  AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id,f.OwnerSeat,f.SkillId,f.ActivationId));var host=new ProgramSkillHost(this);new SkillProgramExecutor().Run(f.Id,host,host);
 }
 private SkillProgramStepOutcome ResolveZonghu(ProgramSkillFrame input)
 {var f=GetActiveProgramFrame(input.Id);if(f.InstructionIndex!=1)throw new InvalidOperationException("Zonghu must enter its registered instruction.");
 if(f.Zonghu is null){if(f.ActivationId!="native-use-request"||f.TriggerId is not null||f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Play||!ZonghuSources(_players[f.OwnerSeat]).Any())throw new InvalidOperationException("Zonghu requires a real Play activation.");var original=new PendingDecision(DecisionKind.PlayCard,f.OwnerSeat,"宗护真实出牌阶段",[],[]){PromptId=CreatePromptId(),Revision=Revision};var source=ZonghuSource(f);var n=ZonghuPlayNeed(f.OwnerSeat);ReplaceRuntimeTop(f=f with{Zonghu=new(source,f.GameplayHash,_turnNumber,_turnProgression.OwnerSeat,n,original,ZonghuStage.Declare)});AdvanceEventRulesAndQueueFact(new ZonghuStartedEvent(f.Id,source,f.GameplayHash,_turnNumber,_turnProgression.OwnerSeat,n.Intent,0,0,original.PromptId,original.Revision,ZonghuDecisionHash(original)));}
 foreach(var player in _players)_observedSkillGrantRevisions.TryAdd(player.Seat,player.SkillGrants.Revision);AssertZonghu(f);PublishZonghu(f);return SkillProgramStepOutcome.AwaitChoice;}
 private IReadOnlyList<PromptChoice> ZonghuChoices(ProgramSkillFrame f)
 {
  var r=f.Zonghu!;var result=new List<PromptChoice>();Dictionary<string,string> Args(string branch)=>new(){[ZonghuChoice]=branch,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)};
  PromptChoice C(string branch,string label,IReadOnlyList<int>? targets=null,string suffix="")=>new(new($"zonghu.{f.Id}.{r.Stage}.{branch}.{suffix}"),label,[],targets??[],Args(branch));
  if(r.Stage==ZonghuStage.Declare)foreach(var(kind,target)in ZonghuOutputs(r.Need)){var args=Args("declare");args["kind"]=kind.ToString();args["target"]=target.ToString(CultureInfo.InvariantCulture);result.Add(new(new($"zonghu.{f.Id}.declare.{kind}.{target}"),$"需要【{CardCatalog.Get(kind).DisplayName}】"+(ZonghuIsPlayed(r.Need)||r.Need.Intent==ZonghuIntent.Dodge?"":$"，目标：{_players[target].Name}"),[],[],args));}
  else if(r.Stage==ZonghuStage.Victim)foreach(var seat in ZonghuVictims(f.OwnerSeat))result.Add(C("victim","交牌给"+_players[seat].Name,[seat],seat.ToString(CultureInfo.InvariantCulture)));
  else if(r.Stage==ZonghuStage.Card&&r.RecipientSeat is{}recipient){foreach(var card in ZonghuGiftCards(f.OwnerSeat)){var selected=r.Selected.Contains(card.Id);if(!selected&&r.Selected.Count>=ZonghuCost)continue;result.Add(new(new($"zonghu.{f.Id}.gift.{card.Id}.{selected}"),(selected?"取消":"选择")+card.DisplayName,[card.Id],[],Args(selected?"unpick":"pick")));}if(r.Selected.Count==ZonghuCost)result.Add(C("give",$"交给{_players[recipient].Name}{ZonghuCost}张牌"));}
  if(r.Stage==ZonghuStage.Declare)result.Add(C("cancel","取消，返回原请求"));return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
 }
 private void PublishZonghu(ProgramSkillFrame f)
 {
  var r=f.Zonghu!;if(!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!ZonghuNativeNeedCurrent(r)||r.Stage!=ZonghuStage.Declare&&!ZonghuNativeLegal(r.Need,r.OutputKind!.Value,r.TargetSeat!.Value)){FinishZonghuAttempt(f,false);return;}
  var choices=ZonghuChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,r.Stage==ZonghuStage.Declare?"宗护：选择本人使用的杀或闪。":r.Stage==ZonghuStage.Victim?"宗护：选择交牌对象。":$"宗护：选择{ZonghuCost}张手牌或装备牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 private void AnswerZonghu(PromptChoice c)
 {
  if(c.Parameters.ContainsKey(ZonghuOpen)){OpenZonghu(c);return;}
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Zonghu:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!ZonghuChoices(f).Any(x=>AssistedChoicesEqual([x],[c])))throw new InvalidOperationException("Zonghu changed its exact private declaration or opaque native slot.");AssertZonghu(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=c.Parameters[ZonghuChoice];
  if(branch=="cancel"){FinishZonghuAttempt(f,false);return;}
  if(branch=="declare"){var kind=Enum.Parse<CardKind>(c.Parameters["kind"]);var target=int.Parse(c.Parameters["target"],CultureInfo.InvariantCulture);AdvanceEventRulesAndQueueFact(new ZonghuDeclaredEvent(f.Id,kind,target));ReplaceRuntimeTop(f=f with{Zonghu=r with{OutputKind=kind,TargetSeat=target,Stage=ZonghuStage.Victim}});PublishZonghu(f);return;}
  if(branch=="victim"){var seat=c.Targets.Single();AdvanceEventRulesAndQueueFact(new ZonghuRecipientSelectedEvent(f.Id,seat));ReplaceRuntimeTop(f=f with{Zonghu=r with{RecipientSeat=seat,Stage=ZonghuStage.Card}});PublishZonghu(f);return;}
  if(branch is"pick"or"unpick"){var id=c.Cards.Single();ReplaceRuntimeTop(f=f with{Zonghu=r with{Selected=branch=="pick"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray()}});PublishZonghu(f);return;}
  if(branch!="give"||r.RecipientSeat is not{}recipient||!ZonghuVictims(f.OwnerSeat).Contains(recipient)||r.Selected.Count!=ZonghuCost||r.Selected.Any(id=>!ZonghuGiftCards(f.OwnerSeat).Any(card=>card.Id==id))||!ZonghuNativeNeedCurrent(r)||!ZonghuNativeLegal(r.Need,r.OutputKind!.Value,r.TargetSeat!.Value))throw new InvalidOperationException("Zonghu lost its exact gift or original true-use request.");
  var materials=r.Selected.Select(id=>(Card:GetAdvancedCard(id),From:_cardZones.GetLocation(id))).ToArray();var before=_movementSequence;var grain=materials.Any(m=>m.Card.Kind==CardKind.WoodenOx&&m.From==CardLocation.Equipment(f.OwnerSeat))?GetWoodenOxGrain(_players[f.OwnerSeat]).Select(card=>card.Id).ToArray():[];
  ReplaceRuntimeTop(f=f with{Zonghu=r with{Stage=ZonghuStage.CostChildren,Before=before,After=before,GrainIds=grain},PendingMovementContinuation=new(f.OwnerSeat,0,null)});for(var i=0;i<materials.Length;i++)AdvanceEventRulesAndQueueFact(new ZonghuGiftSelectedEvent(f.Id,i,materials[i].Card.Id));
  MoveProgramCardsFromMultipleSources(r.Selected, CardLocation.Hand(recipient), new(ZonghuGiftReason), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,recipient,r.Selected,_movementSequence,(batch,moves)=>{var gifts=moves.Select(m=>new ZonghuGift(m.CardId,materials.Single(x=>x.Card.Id==m.CardId).Card.Kind,m.From,m.Sequence,batch)).OrderBy(g=>Array.IndexOf(r.Selected.ToArray(),g.CardId)).ToArray();var current=GetActiveProgramFrame(f.Id);var paid=new ZonghuPaidOrigin(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.ActualTurnOwner,r.Need,r.OutputKind!.Value,r.TargetSeat!.Value,recipient,gifts,ZonghuCost,before,moves.Max(m=>m.Sequence));ReplaceRuntimeTop(current with{Zonghu=current.Zonghu! with{Paid=paid,After=paid.After}});for(var i=0;i<gifts.Length;i++){var g=gifts[i];AdvanceEventRulesAndQueueFact(new ZonghuGiftPaidEvent(f.Id,i,g.CardId,g.Kind,g.From,recipient,g.Sequence,g.BatchId));}AdvanceEventRulesAndQueueFact(new ZonghuCostPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.ActualTurnOwner,recipient,ZonghuCost,before,paid.After));}));
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Zonghu=f.Zonghu! with{After=_movementSequence}});ContinueZonghu(f.Id);
 }
 private bool DrainZonghu(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
 private void ContinueZonghu(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Zonghu:{}r}f||f.Id!=id)return;AssertZonghu(f);if(r.Stage!=ZonghuStage.CostChildren){if(_pendingDecision is null)PublishZonghu(f);return;}if(DrainZonghu(f))return;
  var matched=r.Paid is{}p&&ValidZonghuPayment(p);var legal=matched&&ZonghuNativeNeedCurrent(r)&&ZonghuNativeLegal(r.Need,r.OutputKind!.Value,r.TargetSeat!.Value);AdvanceEventRulesAndQueueFact(new ZonghuAttemptResolvedEvent(f.Id,matched,legal));FinishZonghuAttempt(f,legal);
 }
 private void FinishZonghuAttempt(ProgramSkillFrame f,bool success)
 {var r=f.Zonghu!;ClearPendingDecision();ReplaceRuntimeTop(f=f with{Zonghu=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);if(success)DispatchZonghu(r.Paid!);else RepublishZonghuNative(r);}
 [ProgramContinuation(ProgramContinuationKind.Resume,27512)]private bool ResumeZonghu(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Zonghu:not null}f||f.Id!=id||f.InstructionIndex==0)return false;ContinueZonghu(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,27512)]private bool ReturnZonghuMovement(ProgramSkillFrame f){if(f.Zonghu is null)return false;ContinueZonghu(f.Id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27512)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27512)]private bool ContinuePaidZonghu(ProgramSkillFrame f){if(f.Zonghu?.Paid is null)return false;AssertZonghu(f);return true;}
 private bool TryAdvanceZonghuAi()
 {
  if(_pendingDecision is not{}p||_players[p.PlayerSeat].IsHuman)return false;
  var choices=p.Choices.Where(c=>c.Parameters.ContainsKey(ZonghuOpen)||c.Parameters.ContainsKey(ZonghuChoice)).ToArray();if(choices.Length==0)return false;
  // No private victim card is inspected here. An actual opaque slot is selected.
  var selected=choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault(ZonghuChoice)!="cancel"&&c.Parameters.GetValueOrDefault(ZonghuChoice)!="unpick")??choices[0];AnswerZonghu(selected);return true;
 }
}
