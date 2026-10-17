namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void StoreZonghuResponse(ZonghuPaidOrigin p)
 {var owner=_resolutionStack.Single(f=>f.Id==p.Need.OwnerFrameId);ReplaceRuntimeFrame(owner.Id,owner switch{CardUseFrame u=>u with{ZonghuResponseOrigin=p},ProgramSkillFrame f=>f with{ZonghuResponseOrigin=p},_=>throw new InvalidOperationException("Zonghu response lost its typed incoming native owner.")});}
 private static ZonghuPaidOrigin? ZonghuResponseOrigin(ResolutionFrame f)=>f switch{CardUseFrame u=>u.ZonghuResponseOrigin,ProgramSkillFrame p=>p.ZonghuResponseOrigin,_=>null};
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27512)]
 private void ObserveZonghuResponse(CardActionAcceptedEvent accepted)
 {
  var a=accepted.Action;if(!HasZonghuCapability||a.Type!=CardActionType.Response||a.PhysicalCards.Count!=0||a.ConversionChain is not[var source]||source.SkillId!="ol:zonghu")return;
  var owner=_resolutionStack.LastOrDefault(f=>ZonghuResponseOrigin(f)is{}p&&p.Source==source&&p.Need.ParentActionId==a.ParentActionId);if(owner is null)return;var p=ZonghuResponseOrigin(owner)!;
  if(!ValidZonghuPayment(p)||p.OutputKind!=a.EffectiveKind||p.Need.ActorSeat!=a.ActorSeat||p.Need.ProviderSeat!=a.ProviderSeat||p.Need.RequesterSeat!=a.RequesterSeat||a.ResponderSeat!=a.ActorSeat||a.TargetSeats.Count!=0||a.OpponentSeat!=p.Need.OpponentSeat||a.EffectiveIsRed is not null||ProgramEventHistory<ZonghuResponseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId)||ProgramEventHistory<ZonghuUseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId))throw new InvalidOperationException("Zonghu zero response changed its original native participants or already-issued cost.");
  AdvanceEventRulesAndQueueFact(new ZonghuResponseIssuedEvent(p.AttemptId,owner.Id,a.ActionId,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,p.OutputKind,ZonghuResponseContinuation(p)));
 }
 private ProgramCardContinuation ZonghuResponseContinuation(ZonghuPaidOrigin p)=>p.Need.Intent switch{ZonghuIntent.Dodge=>ProgramCardContinuation.Dodge,ZonghuIntent.Duel=>ProgramCardContinuation.DuelSlash,ZonghuIntent.FactionDodge=>ProgramCardContinuation.FactionDefenseDodge,ZonghuIntent.FactionSlashPlayed=>ActiveFactionCardRequest?.Purpose==FactionCardRequestPurpose.DuelResponse||ActiveDuel?.ResolutionId==p.Need.OwnerFrameId?ProgramCardContinuation.FactionSlashDuelSlash:ProgramCardContinuation.FactionSlashGroupResponse,_=>ProgramCardContinuation.GroupResponse};
 private void DispatchZonghuResponse(ZonghuPaidOrigin p)
 {
  var n=p.Need;StoreZonghuResponse(p);
  if(n.Intent==ZonghuIntent.FactionDodge){CompleteFactionDefenseResponse(ActiveFactionDefense!,_players[n.ProviderSeat],null,false,p.Source);return;}
  var attack=n.Intent==ZonghuIntent.Duel?ActiveDuel!.Attack:n.Intent==ZonghuIntent.Group?ActiveGroupCard!.CurrentAttack!:n.Intent==ZonghuIntent.FactionSlashPlayed?ActiveFactionCardRequest!.ResponseAttack!:ActiveCardAttack!;
  var continuation=ZonghuResponseContinuation(p);var faction=ActiveFactionCardRequest;FactionProviderRewardReceipt? reward=null;
  if(n.Intent==ZonghuIntent.FactionSlashPlayed){reward=CaptureFactionProviderReward(faction!);ActiveFactionCardRequest=null;AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(faction!.ResolutionId,n.ActorSeat,true,n.ProviderSeat,0,p.OutputKind,false));}
  PopResponseWindow(n.OwnerFrameId);ClearPendingDecision();SetResponseParentStep(n.OwnerFrameId,ResolutionFrameStep.ResolvingEffect);
  AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0,n.ActorSeat,n.OpponentSeat!.Value,p.OutputKind));
  if(n.Intent==ZonghuIntent.Duel||continuation==ProgramCardContinuation.FactionSlashDuelSlash)AdvanceEventRulesAndQueueFact(new DuelResponseEvent(n.OwnerFrameId,n.ActorSeat,true,0,p.OutputKind));
  else if(n.Intent==ZonghuIntent.Group||continuation==ProgramCardContinuation.FactionSlashGroupResponse){var g=ActiveGroupCard!;AdvanceEventRulesAndQueueFact(new GroupResponseEvent(g.ResolutionId,g.Card.Kind,g.RequiredCardKind!.Value,n.ActorSeat,true,0,p.OutputKind));}
  if(IsSlashCard(p.OutputKind))MarkSlashUsedOrPlayedDuringCurrentPlayPhase(n.ActorSeat,p.OutputKind);if(reward is not null)RewardFactionRequestProvider(reward,n.ProviderSeat);
  if(TryBeginCardResponsePrograms(attack,_players[n.ActorSeat],_players[n.ProviderSeat],n.RequesterSeat,n.OpponentSeat!.Value,p.OutputKind,[],continuation,p.Source))return;
  if(continuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge)CompleteSuccessfulDodgeResponse(attack);else if(continuation is ProgramCardContinuation.DuelSlash or ProgramCardContinuation.FactionSlashDuelSlash)ContinueDuelAfterSuccessfulSlash(ActiveDuel!,n.ActorSeat);else CompleteAttack(attack);
 }
 private bool IsZonghuZeroResponse(CardActionContext a,ProgramCardContinuation c)
 {
  if(a.Type!=CardActionType.Response||a.PhysicalCards.Count!=0||a.ConversionChain is not[var source]||source.SkillId!="ol:zonghu")return false;
  return _resolutionStack.Any(f=>ZonghuResponseOrigin(f)is{}p&&ValidZonghuPayment(p)&&p.Source==source&&p.Need.ActorSeat==a.ActorSeat&&p.Need.ProviderSeat==a.ProviderSeat&&p.Need.RequesterSeat==a.RequesterSeat&&p.Need.ParentActionId==a.ParentActionId&&p.OutputKind==a.EffectiveKind&&ProgramEventHistory<ZonghuResponseIssuedEvent>().Count(e=>e.AttemptId==p.AttemptId&&e.OwnerFrameId==f.Id&&e.ActionId==a.ActionId&&e.Continuation==c)==1);
 }
 private void DispatchZonghuFactionUse(ZonghuPaidOrigin p)
 {
  var n=p.Need;var f=ActiveFactionCardRequest??throw new InvalidOperationException("Zonghu lost its actual faction provider request.");var reward=CaptureFactionProviderReward(f);var borrowed=f.BorrowedSword;long? parent=f.ProgramSkillFrameId;
  if(f.IsBorrowedSwordUse){PopResponseWindow(borrowed!.ResolutionId);borrowed.AwaitingSlashChoice=false;borrowed.SlashCardId=0;borrowed.EffectiveSlashKind=p.OutputKind;SetCardUseStep(borrowed.ResolutionId,ResolutionFrameStep.ResolvingEffect);}
  else if(f.IsQinglongCrescentBladeUse){var old=ActiveQinglongCrescentBlade!.Attack;ActiveQinglongCrescentBlade=null;SetCardUseStep(old.ResolutionId,ResolutionFrameStep.ResolvingEffect);AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId,n.ActorSeat,p.TargetSeat,true,[],p.OutputKind));SuspendContinuationForQinglongFollowup(old.ResolutionId);}
  else if(parent is{}id){var program=GetActiveProgramFrame(id);ReplaceRuntimeTop(program=program with{ZonghuReturn=new(p,0,0),Step=ResolutionFrameStep.ResolvingEffect});if(f.IsAssistedProgramUse){if(IsChanshuangSlashRequest(program)||IsQigongRequest(program)||IsWuJingHejiRequest(program)||IsCaoChunInitialRequest(program))PrepareZonghuProgram(program,p);else CommitProgramChoiceResult(program.Id,f.AssistedResultBind!,"used-slash",n.ActorSeat,"宗护供给真实杀。");}}
  f.AwaitingProviders=false;AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(f.ResolutionId,n.ActorSeat,true,n.ProviderSeat,0,p.OutputKind,true,p.TargetSeat));BeginZonghuSlash(p,borrowed,!f.IsAssistedProgramUse&&!f.IsQinglongCrescentBladeUse,parent,f);RewardFactionRequestProvider(reward,n.ProviderSeat);
 }
 private bool IsZonghuZeroSupply(CardUseFrame use,FactionCardRequestHandle request)=>use.ZonghuOrigin is{}p&&p.Need.Intent==ZonghuIntent.FactionSlashUse&&IsIssuedZonghuUse(use)&&p.Need.OwnerFrameId==request.OwnerFrameId&&p.Need.ActorSeat==request.OwnerSeat&&p.Need.ProviderSeat==request.CurrentCandidateSeat&&p.Need.RequesterSeat==request.OwnerSeat&&p.Need.Cursor==request.CandidateIndex&&request.TargetSeat==p.TargetSeat&&request.ActiveAttack?.ResolutionId==use.Id;
 private void RepublishZonghuNative(ZonghuReceipt r)
 {
  var n=r.Need;var actor=_players[n.ProviderSeat];if(!_resolutionStack.Any(f=>f.Id==n.RequestFrameId)||_winner!=Winner.None)return;
  var keepCurrentRequest=actor.IsAlive&&r.Paid is{}requestPayment&&ValidZonghuPayment(requestPayment)&&SameZonghuNeed(requestPayment.Need,n)&&ZonghuNativeNeedCurrent(r);
  if(!actor.IsAlive&&n.Intent is ZonghuIntent.Dodge or ZonghuIntent.Group or ZonghuIntent.Duel)
  {if(_resolutionStack.LastOrDefault()is ResponseWindowFrame response&&response.ParentFrameId==n.OwnerFrameId)PopResponseWindow(n.OwnerFrameId);SetResponseParentStep(n.OwnerFrameId,ResolutionFrameStep.ResolvingEffect);var attack=n.Intent==ZonghuIntent.Group?ActiveGroupCard?.CurrentAttack:n.Intent==ZonghuIntent.Duel?ActiveDuel?.Attack:ActiveCardAttack;if(attack is not null&&attack.ResolutionId==n.OwnerFrameId)CompleteAttack(attack);return;}
  if(!actor.IsAlive&&n.Intent==ZonghuIntent.Dying){RunOneDyingStep();return;}
  if(n.Intent==ZonghuIntent.Dying){RequestHumanDyingResponse(actor,GetDyingPeaches(actor),GetDyingAlcohols(actor,ActiveDying!.VictimSeat));return;}
  if(n.Intent==ZonghuIntent.FactionDodge){AdvanceFactionDefenseCandidate(keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent is ZonghuIntent.FactionSlashPlayed or ZonghuIntent.FactionSlashUse){AdvanceFactionSlashCandidate(keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent==ZonghuIntent.Qinglong)
  {
   if(!actor.IsAlive)
   {
    if(r.Paid is not{}paid||!ValidZonghuPayment(paid)||!ZonghuNativeNeedCurrent(r)||ActiveQinglongCrescentBlade is not{}blade||blade.Attack.ResolutionId!=n.OwnerFrameId||blade.Attack.SourceSeat!=n.ActorSeat||!SameAttackOwner(ActiveCardAttack,blade.Attack))throw new InvalidOperationException("A dead Zonghu payer lost its exact still-unconsumed Qinglong parent.");
    // This is the native no-follow-up return: no card or dead-player menu is issued.
    var attack=blade.Attack;ActiveQinglongCrescentBlade=null;ClearPendingDecision();SetCardUseStep(attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);
    AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(attack.ResolutionId,n.ActorSeat,attack.TargetSeat,false,[],null));RestoreFactionRequestAfterQinglongChoice(attack.ResolutionId);CompleteAttack(attack);return;
   }
   PublishQinglongCrescentBladeChoice(ActiveQinglongCrescentBlade!,CanRequestQinglongCrescentBladeFactionSlash(ActiveQinglongCrescentBlade!));return;
  }
  if(n.Intent==ZonghuIntent.BorrowedSword){var b=ActiveBorrowedSword!;PopResponseWindow(b.ResolutionId);BeginBorrowedSwordSlashChoice(b,CanRequestBorrowedSwordFactionSlash(b),keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent==ZonghuIntent.Duel){var d=ActiveDuel!;PopResponseWindow(d.ResolutionId);BeginDuelResponse(d,keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent is ZonghuIntent.Dodge or ZonghuIntent.Group)
  {var attack=n.Intent==ZonghuIntent.Group?ActiveGroupCard!.CurrentAttack!:ActiveCardAttack!;var required=n.Intent==ZonghuIntent.Dodge?CardKind.Dodge:ActiveGroupCard!.RequiredCardKind!.Value;var cards=GetResponseCards(actor,required);var choices=CreateResponseChoices(cards,required,attack.EffectiveCardKind,card=>GetEffectiveResponseKind(actor,card,required),includeBagua:required==CardKind.Dodge&&!attack.IgnoresArmor&&HasBagua(actor),includeFactionDefense:required==CardKind.Dodge&&CanRequestFactionDefense(actor,attack),includeFactionSlash:required==CardKind.Slash&&CanRequestFactionSlashResponse(actor,attack),responder:actor);_pendingDecision=ZonghuNativeDecision(r.OriginalDecision) with{PromptId=CreatePromptId(),ValidCardIds=choices.SelectMany(c=>c.Cards).Distinct().ToArray(),Choices=choices};_status=actor.IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return;}
  var f=GetActiveProgramFrame(n.OwnerFrameId);var effect=GetPausedPrivateOfferEffect(f)!;
  if(!actor.IsAlive&&n.Intent==ZonghuIntent.NearestLegalSlash)
  {
   if(r.Paid is not{}paid||!ValidZonghuPayment(paid)||!ZonghuNativeNeedCurrent(r)||effect.Op!=SkillProgramEffectOp.RequestLegalSlashByNearest||n.RequestOp!=effect.Op||f.InstructionIndex!=n.InstructionIndex||n.ActorSeat!=n.ProviderSeat||!f.ReexecuteParticipantInstruction||f.NearestLegalSlashRequest is not{AwaitingFaction:false}draft||draft.ActorSeat!=n.ActorSeat||!draft.NearestSeats.SequenceEqual(n.Targets)||f.NumberBindings.SingleOrDefault(b=>b.Name=="participant-cursor-"+(f.InstructionIndex-1))is not{}cursor||cursor.Value<1||cursor.Value>=_playerCount||n.ActorSeat!=(f.OwnerSeat+cursor.Value)%_playerCount)throw new InvalidOperationException("A dead Zonghu payer lost its exact committed nearest-participant cursor.");
   ClearPendingDecision();ReplaceRuntimeTop(f with{NearestLegalSlashRequest=null});AdvanceRuntimeProgram(f.Id);return;
  }
   if(IsSayingSlashRequest(f)){if(!actor.IsAlive)FinishSayingSuppliedSlashFailure(f);else PublishSaying(f);return;}if(IsChanshuangSlashRequest(f)){if(!actor.IsAlive)FinishChanshuangSuppliedSlashFailure(f);else PublishChanshuang(f);return;}if(IsQigongRequest(f)){PublishLvKuangLvXiang(f);return;}if(IsWuJingHejiRequest(f)){PublishWuJing(f,keepCurrentRequest:keepCurrentRequest);return;}if(IsCaoChunInitialRequest(f)){PublishCaoChun(f);return;}if(n.Intent==ZonghuIntent.GenericBasic){PublishKeBiNeng(f);return;}
  if(n.Intent==ZonghuIntent.ProgramSlash){RequestProgramSlashByTarget(f,n.ActorSeat,effect.ResultBind!,keepCurrentRequest:keepCurrentRequest);return;}if(n.Intent==ZonghuIntent.ProgramNearestSlash){RequestProgramSlashByNearest(f,n.ActorSeat,effect.Amount,keepCurrentRequest:keepCurrentRequest);return;}if(n.Intent==ZonghuIntent.AssistedSlash){PublishAssistedPhysicalSlashPrompt(f);return;}if(n.Intent==ZonghuIntent.FixedSlash){PublishFixedSlashAndDeclaredDeckPrompt(f,FixedTargetSlashChoices(f),"选择杀响应原请求。",n.Targets);return;}
  if(n.Intent==ZonghuIntent.NearestLegalSlash){var choices=NearestLegalSlashChoices(f);_pendingDecision=ZonghuNativeDecision(r.OriginalDecision) with{PromptId=CreatePromptId(),ValidCardIds=choices.SelectMany(c=>c.Cards).Distinct().ToArray(),Choices=choices};_status=actor.IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return;}
  throw new InvalidOperationException("Zonghu has no native failed-request return.");
 }
}
