using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 [ProgramCapability]private sealed class WeijieCapability(GameEngine e):IWeijieHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f)=>e.ResolveWeijie(f);}
 [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount,27462,SkillProgramEffectOp.ResolveWeijieRequiredBasicRequest)]
 private bool IsWeijieOrdinaryActivation(ProgramActivationLegalityContext context)=>false;
 private static string WeijieDecisionHash(PendingDecision p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p))));
 private static bool SameWeijieNeed(WeijieNeed a,WeijieNeed b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
 private bool IsWeijieAnswer(ChoiceId id)=>_pendingDecision?.Choices.Any(c=>c.Id==id&&(c.Parameters.ContainsKey(WeijieOpen)||c.Parameters.ContainsKey(WeijieChoice)))==true;
 private CommandResult SubmitWeijieAnswer(int actor,PromptId prompt,ChoiceId id)
 {
  if(!_started)return Reject(CommandErrorCode.NotStarted,"游戏尚未开始。");
  if(_pendingDecision is not{}p||p.PlayerSeat!=actor||p.PromptId!=prompt)return Reject(CommandErrorCode.InvalidPrompt,"诿解必须回答当前真实请求。");
  var c=p.Choices.SingleOrDefault(c=>c.Id==id&&(c.Parameters.ContainsKey(WeijieOpen)||c.Parameters.ContainsKey(WeijieChoice)));if(c is null)return Reject(CommandErrorCode.InvalidChoice,"诿解选择未公布。");
  return Accept(()=>{AnswerWeijie(c);AdvanceRulesAndPublishState();if(_options.AdvanceAfterHumanCommands)AdvanceToHumanBoundary();});
 }
 private void OpenWeijie(PromptChoice c)
 {
  var p=_pendingDecision??throw new InvalidOperationException("Weijie has no real native request.");var n=CaptureWeijieNeed(p)??throw new InvalidOperationException("Weijie lost its exact native requester.");var source=RequireConversionSource(c);
  if(!p.Choices.Any(x=>AssistedChoicesEqual([x],[c]))||!WeijieSources(_players[p.PlayerSeat]).Any(s=>s.Source==source)||!WeijieVictims(p.PlayerSeat).Any()||!WeijieOutputs(n).Any())throw new InvalidOperationException("Weijie cannot replace an unavailable native request or source.");
  var hash=_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash;
  var f=new ProgramSkillFrame(++_resolutionSequence,p.PlayerSeat,source.SkillId,source.BindingId,hash,0,[],[]){SkillInstanceId=source.SkillInstanceId,Weijie=new(source,hash,_turnNumber,_turnProgression.OwnerSeat,n,p,WeijieStage.Declare)};
  ClearPendingDecision();PushRuntimeFrame(f);AdvanceEventRulesAndQueueFact(new WeijieStartedEvent(f.Id,source,hash,_turnNumber,_turnProgression.OwnerSeat,n.Intent,n.OwnerFrameId,n.RequestFrameId,p.PromptId,p.Revision,WeijieDecisionHash(p)));
  AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id,f.OwnerSeat,f.SkillId,f.ActivationId));var host=new ProgramSkillHost(this);new SkillProgramExecutor().Run(f.Id,host,host);
 }
 private SkillProgramStepOutcome ResolveWeijie(ProgramSkillFrame input)
 {var f=GetActiveProgramFrame(input.Id);if(f.Weijie is null||f.InstructionIndex!=1)throw new InvalidOperationException("Weijie must enter its registered native-only request instruction.");AssertWeijie(f);PublishWeijie(f);return SkillProgramStepOutcome.AwaitChoice;}
 private IReadOnlyList<PromptChoice> WeijieChoices(ProgramSkillFrame f)
 {
  var r=f.Weijie!;var result=new List<PromptChoice>();Dictionary<string,string> Args(string branch)=>new(){[WeijieChoice]=branch,["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)};
  PromptChoice C(string branch,string label,IReadOnlyList<int>? targets=null,string suffix="")=>new(new($"weijie.{f.Id}.{r.Stage}.{branch}.{suffix}"),label,[],targets??[],Args(branch));
  if(r.Stage==WeijieStage.Declare)foreach(var(kind,target)in WeijieOutputs(r.Need)){var args=Args("declare");args["kind"]=kind.ToString();args["target"]=target.ToString(CultureInfo.InvariantCulture);result.Add(new(new($"weijie.{f.Id}.declare.{kind}.{target}"),$"需要【{CardCatalog.Get(kind).DisplayName}】"+(WeijieIsPlayed(r.Need)||r.Need.Intent==WeijieIntent.Dodge?"":$"，目标：{_players[target].Name}"),[],[],args));}
  else if(r.Stage==WeijieStage.Victim)foreach(var seat in WeijieVictims(f.OwnerSeat))result.Add(C("victim","弃置"+_players[seat].Name+"的一张手牌",[seat],seat.ToString(CultureInfo.InvariantCulture)));
  else if(r.Stage==WeijieStage.Card&&r.VictimSeat is{}victim)foreach(var old in BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,victim,[CardZoneKind.Hand],OwnedCardMoveIntent.Discard))result.Add(old with{Id=new($"weijie.{f.Id}.card.{old.Id.Value}"),Parameters=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(old.Parameters){[WeijieChoice]="card",["program-action"]="weijie-card"})});
  if(r.Stage!=WeijieStage.CostChildren)result.Add(C("cancel","取消，返回原请求"));return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
 }
 private void PublishWeijie(ProgramSkillFrame f)
 {
  var r=f.Weijie!;if(!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!WeijieNativeNeedCurrent(r)||r.Stage!=WeijieStage.Declare&&!WeijieNativeLegal(r.Need,r.OutputKind!.Value,r.TargetSeat!.Value)){FinishWeijieAttempt(f,false);return;}
  var choices=WeijieChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,r.Stage==WeijieStage.Declare?"诿解：声明本次需要的基本牌。":r.Stage==WeijieStage.Victim?"诿解：选择距离为1的其他角色。":"诿解：选择一张不公开牌名的手牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 private void AnswerWeijie(PromptChoice c)
 {
  if(c.Parameters.ContainsKey(WeijieOpen)){OpenWeijie(c);return;}
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Weijie:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!WeijieChoices(f).Any(x=>AssistedChoicesEqual([x],[c])))throw new InvalidOperationException("Weijie changed its exact private declaration or opaque native slot.");AssertWeijie(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=c.Parameters[WeijieChoice];
  if(branch=="cancel"){FinishWeijieAttempt(f,false);return;}
  if(branch=="declare"){var kind=Enum.Parse<CardKind>(c.Parameters["kind"]);var target=int.Parse(c.Parameters["target"],CultureInfo.InvariantCulture);AdvanceEventRulesAndQueueFact(new WeijieDeclaredEvent(f.Id,kind,target));ReplaceRuntimeTop(f=f with{Weijie=r with{OutputKind=kind,TargetSeat=target,Stage=WeijieStage.Victim}});PublishWeijie(f);return;}
  if(branch=="victim"){var seat=c.Targets.Single();AdvanceEventRulesAndQueueFact(new WeijieVictimSelectedEvent(f.Id,seat));ReplaceRuntimeTop(f=f with{Weijie=r with{VictimSeat=seat,Stage=WeijieStage.Card}});PublishWeijie(f);return;}
  if(branch!="card"||r.VictimSeat is not{}victim||!WeijieVictims(f.OwnerSeat).Contains(victim)||!WeijieNativeNeedCurrent(r)||!WeijieNativeLegal(r.Need,r.OutputKind!.Value,r.TargetSeat!.Value))throw new InvalidOperationException("Weijie lost its unpaid native need or distance-one Hand owner.");
  if(c.Parameters.GetValueOrDefault("source-zone")!=CardZoneKind.Hand.ToString()||!int.TryParse(c.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Weijie lost its genuine opaque Hand slot.");
  var card=_cardZones.CardsAt(CardLocation.Hand(victim))[slot];var name=ProgramBasicCardName(AdvancedEffectiveHandKind(_players[victim],card));var before=_movementSequence;
  ReplaceRuntimeTop(f=f with{Weijie=r with{Stage=WeijieStage.CostChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  MoveProgramLogicalDiscard(f,f.OwnerSeat,[card.Id],new(WeijieDiscardReason),(batch,moves)=>{var m=moves.Single();var current=GetActiveProgramFrame(f.Id);var paid=new WeijiePaidOrigin(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.ActualTurnOwner,r.Need,r.OutputKind!.Value,r.TargetSeat!.Value,victim,card.Id,card.Kind,name,CardLocation.Hand(victim),m.Sequence,batch);ReplaceRuntimeTop(current with{Weijie=current.Weijie! with{Paid=paid,After=m.Sequence}});AdvanceEventRulesAndQueueFact(new WeijieCostPaidEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.ActualTurnOwner,victim,card.Id,card.Kind,name,paid.From,m.Sequence,batch));});
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Weijie=f.Weijie! with{After=_movementSequence}});ContinueWeijie(f.Id);
 }
 private bool DrainWeijie(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
 private void ContinueWeijie(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Weijie:{}r}f||f.Id!=id)return;AssertWeijie(f);if(r.Stage!=WeijieStage.CostChildren){if(_pendingDecision is null)PublishWeijie(f);return;}if(DrainWeijie(f))return;
  var matched=r.Paid is{}p&&p.EffectiveName==ProgramBasicCardName(p.OutputKind);var legal=matched&&WeijieNativeNeedCurrent(r)&&WeijieNativeLegal(r.Need,r.OutputKind!.Value,r.TargetSeat!.Value);AdvanceEventRulesAndQueueFact(new WeijieAttemptResolvedEvent(f.Id,matched,legal));FinishWeijieAttempt(f,legal);
 }
 private void FinishWeijieAttempt(ProgramSkillFrame f,bool success)
 {var r=f.Weijie!;ClearPendingDecision();ReplaceRuntimeTop(f=f with{Weijie=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);if(success)DispatchWeijie(r.Paid!);else RepublishWeijieNative(r);}
 [ProgramContinuation(ProgramContinuationKind.Resume,27462)]private bool ResumeWeijie(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Weijie:not null}f||f.Id!=id||f.InstructionIndex==0)return false;ContinueWeijie(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,27462)]private bool ReturnWeijieMovement(ProgramSkillFrame f){if(f.Weijie is null)return false;ContinueWeijie(f.Id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27462)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27462)]private bool ContinuePaidWeijie(ProgramSkillFrame f){if(f.Weijie?.Paid is null)return false;AssertWeijie(f);return true;}
 private bool TryAdvanceWeijieAi()
 {
  if(_pendingDecision is not{}p||_players[p.PlayerSeat].IsHuman)return false;
  var choices=p.Choices.Where(c=>c.Parameters.ContainsKey(WeijieOpen)||c.Parameters.ContainsKey(WeijieChoice)).ToArray();if(choices.Length==0)return false;
  // No private victim card is inspected here. An actual opaque slot is selected.
  var selected=choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault(WeijieChoice)!="cancel")??choices[0];AnswerWeijie(selected);return true;
 }
}
