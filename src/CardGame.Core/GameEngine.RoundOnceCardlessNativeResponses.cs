namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool IsRoundOnceCardlessResponseCardUse(CharacterState actor,CardKind kind)
 {
  if(!actor.IsAlive||_winner!=Winner.None||kind!=CardKind.Dodge||HasIssuedPlayPhaseUseBan(actor.Seat)||IsCardUseForbidden(actor.Seat,kind,CardActionType.Use))return false;
  return ActiveFactionDefense is null&&ActiveGroupCard is null&&IsProgramResponseCardUse(actor,kind)&&ActiveCardAttack is {} slash&&slash.TargetSeat==actor.Seat&&slash.EffectiveCardKind is {} incoming&&IsSlashCard(incoming);

 }
 private IReadOnlyList<PromptChoice> RoundOnceCardlessResponseChoices(CharacterState actor,CardKind kind)
 {
  if(!IsRoundOnceCardlessResponseCardUse(actor,kind))return [];
  var originalId=ActiveCardAttack!.ResolutionId;
  return Array.AsReadOnly(RoundOnceCardlessRules(actor,kind,true).Select(entry=>
  {
   var source=entry.Source;var parameters=new Dictionary<string,string>{["response"]="round-once-cardless-basic",["output-kind"]=kind.ToString(),["original-frame-id"]=originalId.ToString(System.Globalization.CultureInfo.InvariantCulture)};AddConversionParameters(parameters,source);
   return new PromptChoice(new ChoiceId($"round-cardless.{originalId}.{kind}.{source.SkillId}.{source.BindingId}.{source.SkillInstanceId}"),$"发动【{ProgramConversionName(source)}】，视为使用【{CardCatalog.Get(kind).DisplayName}】。",[],[],parameters);
  }).ToArray());
 }
 private bool TryResolveRoundOnceCardlessResponse(PromptChoice choice,bool advance)
 {
  if(choice.Parameters.GetValueOrDefault("response")!="round-once-cardless-basic")return false;
  if(_pendingDecision is not {} prompt||!prompt.Choices.Any(c=>AssistedChoicesEqual([c],[choice]))||choice.Cards.Count!=0||choice.Targets.Count!=0||!Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"),out var kind)||kind!=CardKind.Dodge||prompt.Kind!=DecisionKind.RespondDodge)throw new InvalidOperationException("Cardless response changed its current native published producer or zero-material tuple.");
  var actor=_players[prompt.PlayerSeat];var source=RequireConversionSource(choice);
  if(!RoundOnceCardlessResponseChoices(actor,kind).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Cardless response lost its exact current native use qualification or unpaid shared round allowance.");
  ConsumeRoundOnceCardlessBasic(source,true);
  ResolveRoundOnceCardlessDodge(actor,source);
  AdvanceRulesAndPublishState();if(advance)AdvanceToHumanBoundary();return true;
 }
 private ResolutionFrame RoundOnceCardlessResponseOwner(long id)=>_resolutionStack.SingleOrDefault(f=>f.Id==id&&(f is CardUseFrame or ProgramSkillFrame))??throw new InvalidOperationException("Cardless response has no actual typed incoming Slash owner.");
 private RoundOnceCardlessBasicUseReceipt StoreRoundOnceCardlessResponse(ResolutionFrame owner,CardActionContext action,CardConversionSource source)
 {
  if(RoundOnceCardlessResponseReceipt(owner)is not null)throw new InvalidOperationException("A native response owner already holds an unresolved cardless basic receipt.");
  var receipt=IssueRoundOnceCardlessBasicUse(owner,action,source);ReplaceRuntimeFrame(owner.Id,owner switch{CardUseFrame use=>use with{RoundOnceCardlessBasicResponse=receipt},ProgramSkillFrame program=>program with{RoundOnceCardlessBasicResponse=receipt},_=>throw new InvalidOperationException("Unexpected cardless response owner.")});return receipt;
 }
 private void ResolveRoundOnceCardlessDodge(CharacterState actor,CardConversionSource source)
 {
  var attack=ActiveCardAttack??throw new InvalidOperationException("Cardless Dodge lost its original native Slash.");var owner=RoundOnceCardlessResponseOwner(attack.ResolutionId);
  PopResponseWindow(attack.ResolutionId);ClearPendingDecision();SetResponseParentStep(attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);
  var action=CaptureFactionAction(new CardActionContext(++_cardActionSequence,(owner as CardUseFrame)?.Action?.ActionId,CardActionType.Response,actor.Seat,actor.Seat,null,actor.Seat,attack.SourceSeat,CardKind.Dodge,[],[],[source],effectiveSuit:Suit.None,effectiveRank:0));
  StoreRoundOnceCardlessResponse(RoundOnceCardlessResponseOwner(owner.Id),action,source);RecordProgramUsedBasicCard(actor.Seat,CardKind.Dodge);RecordActualPlayPhaseUse(action);
  AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0,actor.Seat,attack.SourceSeat,CardKind.Dodge));AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));CaptureFriendMinimumHandUse(action);CaptureOffTurnResponseUseOrigin(attack.ResolutionId,action);
  if(TryBeginCommittedResponseUsePrograms(attack,action,ProgramCardContinuation.Dodge))return;
  if(TryBeginProgramCardWindow(attack,action,SkillProgramTriggerWindow.CardResponseAccepted,[attack.SourceSeat],ProgramCardContinuation.Dodge))return;
  if(HasResponseUseCompletionObserver(action,ProgramCardContinuation.Dodge)||HasCardResponseCompletedObserver(action,ProgramCardContinuation.Dodge))ContinueAcceptedCardResponse(attack,action,ProgramCardContinuation.Dodge);else CompleteSuccessfulDodgeResponse(attack);
 }
 private static RoundOnceCardlessBasicUseReceipt? RoundOnceCardlessResponseReceipt(ResolutionFrame owner)=>owner switch{CardUseFrame use=>use.RoundOnceCardlessBasicResponse,ProgramSkillFrame program=>program.RoundOnceCardlessBasicResponse,_=>null};
 private bool IsIssuedRoundOnceCardlessResponse(ResolutionFrame owner,CardActionContext action)
 {
  if(!IsIssuedRoundOnceCardlessBasicUse(RoundOnceCardlessResponseReceipt(owner),owner,action)||action.Type!=CardActionType.Response||action.TargetSeats.Count!=0||action.ActorSeat!=action.ResponderSeat)return false;
  var parentAction=(owner as CardUseFrame)?.Action?.ActionId;if(action.ParentActionId!=parentAction)return false;
  var attack=owner switch{CardUseFrame use=>use.CardAttack,ProgramSkillFrame program=>program.CardAttack,_=>null};if(attack is null)return false;
  return action.EffectiveKind==CardKind.Dodge&&attack.EffectiveCardKind is {} incoming&&IsSlashCard(incoming)&&attack.TargetSeat==action.ActorSeat&&attack.SourceSeat==action.OpponentSeat;
 }
 private CardActionContext RequireRoundOnceCardlessResponseAction(ResolutionFrame owner)
 {
  var receipt=RoundOnceCardlessResponseReceipt(owner)??throw new InvalidOperationException("Cardless response lost its actual paid receipt.");var accepted=CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Where(e=>e.Action.ActionId==receipt.CardActionId).ToArray();
  if(accepted is not [var fact]||!IsIssuedRoundOnceCardlessResponse(owner,fact.Action))throw new InvalidOperationException("Cardless response lost its once accepted native action, original typed producer or shared quota payment.");return fact.Action;
 }
 private bool IsRoundOnceCardlessResponseUse(CardActionContext action,ProgramCardContinuation continuation)
 {
  if(continuation!=ProgramCardContinuation.Dodge||action.Type!=CardActionType.Response||action.PhysicalCards.Count!=0||action.ConversionChain is not [var source]||ViewAsRule(source)?.RoundOnceCardlessBasic is null)return false;
  var owner=_resolutionStack.FirstOrDefault(f=>RoundOnceCardlessResponseReceipt(f)?.CardActionId==action.ActionId);return owner is not null&&IsIssuedRoundOnceCardlessResponse(owner,action)&&action.EffectiveKind==CardKind.Dodge;
 }
 private void AssertRoundOnceCardlessNativeResponses()
 {
  foreach(var owner in _resolutionStack.Where(f=>RoundOnceCardlessResponseReceipt(f)is not null))RequireRoundOnceCardlessResponseAction(owner);
  foreach(var window in _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Where(w=>w.Action.Type==CardActionType.Response&&w.Action.ConversionChain.Any(s=>ViewAsRule(s)?.RoundOnceCardlessBasic is not null)))
  {var owner=_resolutionStack.SingleOrDefault(f=>f.Id==window.ParentFrameId);if(owner is null||!IsIssuedRoundOnceCardlessResponse(owner,window.Action))throw new InvalidOperationException("Cardless response child lost its exact owning native frame receipt.");}
 }
 private void ClearRoundOnceCardlessResponse(ResolutionFrame owner)
 {
  if(RoundOnceCardlessResponseReceipt(owner)is null)return;var action=RequireRoundOnceCardlessResponseAction(owner);
  if(_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w=>w.Action.ActionId==action.ActionId))throw new InvalidOperationException("Cardless response cannot clear before its accepted native children return.");
  ReplaceRuntimeFrame(owner.Id,owner switch{CardUseFrame use=>use with{RoundOnceCardlessBasicResponse=null},ProgramSkillFrame program=>program with{RoundOnceCardlessBasicResponse=null},_=>throw new InvalidOperationException("Unexpected cardless response owner.")});
 }
 private void ClearRoundOnceCardlessDodgeResponse(CardAttackHandle attack)
 {if(_resolutionStack.SingleOrDefault(f=>f.Id==attack.ResolutionId)is {} owner&&RoundOnceCardlessResponseReceipt(owner)is not null){var action=RequireRoundOnceCardlessResponseAction(owner);if(action.EffectiveKind!=CardKind.Dodge||action.ActorSeat!=attack.TargetSeat||action.OpponentSeat!=attack.SourceSeat)throw new InvalidOperationException("Cardless Dodge changed its original successful native attack.");ClearRoundOnceCardlessResponse(owner);}}
}
