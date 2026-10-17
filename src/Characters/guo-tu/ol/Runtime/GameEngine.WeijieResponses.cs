namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void StoreWeijieResponse(WeijiePaidOrigin p)
 {var owner=_resolutionStack.Single(f=>f.Id==p.Need.OwnerFrameId);ReplaceRuntimeFrame(owner.Id,owner switch{CardUseFrame u=>u with{WeijieResponseOrigin=p},ProgramSkillFrame f=>f with{WeijieResponseOrigin=p},_=>throw new InvalidOperationException("Weijie response lost its typed incoming native owner.")});}
 private static WeijiePaidOrigin? WeijieResponseOrigin(ResolutionFrame f)=>f switch{CardUseFrame u=>u.WeijieResponseOrigin,ProgramSkillFrame p=>p.WeijieResponseOrigin,_=>null};
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27462)]
 private void ObserveWeijieResponse(CardActionAcceptedEvent accepted)
 {
  var a=accepted.Action;if(!HasWeijieCapability||a.Type!=CardActionType.Response||a.PhysicalCards.Count!=0||a.ConversionChain is not[var source]||source.SkillId!="ol:weijie")return;
  var owner=_resolutionStack.LastOrDefault(f=>WeijieResponseOrigin(f)is{}p&&p.Source==source&&p.Need.ParentActionId==a.ParentActionId);if(owner is null)return;var p=WeijieResponseOrigin(owner)!;
  if(!ValidWeijiePayment(p)||p.OutputKind!=a.EffectiveKind||p.Need.ActorSeat!=a.ActorSeat||p.Need.ProviderSeat!=a.ProviderSeat||p.Need.RequesterSeat!=a.RequesterSeat||a.ResponderSeat!=a.ActorSeat||a.TargetSeats.Count!=0||a.OpponentSeat!=p.Need.OpponentSeat||a.EffectiveIsRed is not null||ProgramEventHistory<WeijieResponseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId)||ProgramEventHistory<WeijieUseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId))throw new InvalidOperationException("Weijie zero response changed its original native participants or already-issued cost.");
  AdvanceEventRulesAndQueueFact(new WeijieResponseIssuedEvent(p.AttemptId,owner.Id,a.ActionId,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,p.OutputKind,WeijieResponseContinuation(p)));
 }
 private ProgramCardContinuation WeijieResponseContinuation(WeijiePaidOrigin p)=>p.Need.Intent switch{WeijieIntent.Dodge=>ProgramCardContinuation.Dodge,WeijieIntent.Duel=>ProgramCardContinuation.DuelSlash,WeijieIntent.FactionDodge=>ProgramCardContinuation.FactionDefenseDodge,WeijieIntent.FactionSlashPlayed=>ActiveFactionCardRequest?.Purpose==FactionCardRequestPurpose.DuelResponse||ActiveDuel?.ResolutionId==p.Need.OwnerFrameId?ProgramCardContinuation.FactionSlashDuelSlash:ProgramCardContinuation.FactionSlashGroupResponse,_=>ProgramCardContinuation.GroupResponse};
 private void DispatchWeijieResponse(WeijiePaidOrigin p)
 {
  var n=p.Need;StoreWeijieResponse(p);
  if(n.Intent==WeijieIntent.FactionDodge){CompleteFactionDefenseResponse(ActiveFactionDefense!,_players[n.ProviderSeat],null,false,p.Source);return;}
  var attack=n.Intent==WeijieIntent.Duel?ActiveDuel!.Attack:n.Intent==WeijieIntent.Group?ActiveGroupCard!.CurrentAttack!:n.Intent==WeijieIntent.FactionSlashPlayed?ActiveFactionCardRequest!.ResponseAttack!:ActiveCardAttack!;
  var continuation=WeijieResponseContinuation(p);var faction=ActiveFactionCardRequest;FactionProviderRewardReceipt? reward=null;
  if(n.Intent==WeijieIntent.FactionSlashPlayed){reward=CaptureFactionProviderReward(faction!);ActiveFactionCardRequest=null;AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(faction!.ResolutionId,n.ActorSeat,true,n.ProviderSeat,0,p.OutputKind,false));}
  PopResponseWindow(n.OwnerFrameId);ClearPendingDecision();SetResponseParentStep(n.OwnerFrameId,ResolutionFrameStep.ResolvingEffect);
  AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0,n.ActorSeat,n.OpponentSeat!.Value,p.OutputKind));
  if(n.Intent==WeijieIntent.Duel||continuation==ProgramCardContinuation.FactionSlashDuelSlash)AdvanceEventRulesAndQueueFact(new DuelResponseEvent(n.OwnerFrameId,n.ActorSeat,true,0,p.OutputKind));
  else if(n.Intent==WeijieIntent.Group||continuation==ProgramCardContinuation.FactionSlashGroupResponse){var g=ActiveGroupCard!;AdvanceEventRulesAndQueueFact(new GroupResponseEvent(g.ResolutionId,g.Card.Kind,g.RequiredCardKind!.Value,n.ActorSeat,true,0,p.OutputKind));}
  if(IsSlashCard(p.OutputKind))MarkSlashUsedOrPlayedDuringCurrentPlayPhase(n.ActorSeat,p.OutputKind);if(reward is not null)RewardFactionRequestProvider(reward,n.ProviderSeat);
  if(TryBeginCardResponsePrograms(attack,_players[n.ActorSeat],_players[n.ProviderSeat],n.RequesterSeat,n.OpponentSeat!.Value,p.OutputKind,[],continuation,p.Source))return;
  if(continuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge)CompleteSuccessfulDodgeResponse(attack);else if(continuation is ProgramCardContinuation.DuelSlash or ProgramCardContinuation.FactionSlashDuelSlash)ContinueDuelAfterSuccessfulSlash(ActiveDuel!,n.ActorSeat);else CompleteAttack(attack);
 }
 private bool IsWeijieZeroResponse(CardActionContext a,ProgramCardContinuation c)
 {
  if(a.Type!=CardActionType.Response||a.PhysicalCards.Count!=0||a.ConversionChain is not[var source]||source.SkillId!="ol:weijie")return false;
  return _resolutionStack.Any(f=>WeijieResponseOrigin(f)is{}p&&ValidWeijiePayment(p)&&p.Source==source&&p.Need.ActorSeat==a.ActorSeat&&p.Need.ProviderSeat==a.ProviderSeat&&p.Need.RequesterSeat==a.RequesterSeat&&p.Need.ParentActionId==a.ParentActionId&&p.OutputKind==a.EffectiveKind&&ProgramEventHistory<WeijieResponseIssuedEvent>().Count(e=>e.AttemptId==p.AttemptId&&e.OwnerFrameId==f.Id&&e.ActionId==a.ActionId&&e.Continuation==c)==1);
 }
 private void DispatchWeijieFactionUse(WeijiePaidOrigin p)
 {
  var n=p.Need;var f=ActiveFactionCardRequest??throw new InvalidOperationException("Weijie lost its actual faction provider request.");var reward=CaptureFactionProviderReward(f);var borrowed=f.BorrowedSword;long? parent=f.ProgramSkillFrameId;
  if(f.IsBorrowedSwordUse){PopResponseWindow(borrowed!.ResolutionId);borrowed.AwaitingSlashChoice=false;borrowed.SlashCardId=0;borrowed.EffectiveSlashKind=p.OutputKind;SetCardUseStep(borrowed.ResolutionId,ResolutionFrameStep.ResolvingEffect);}
  else if(f.IsQinglongCrescentBladeUse){var old=ActiveQinglongCrescentBlade!.Attack;ActiveQinglongCrescentBlade=null;SetCardUseStep(old.ResolutionId,ResolutionFrameStep.ResolvingEffect);AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId,n.ActorSeat,p.TargetSeat,true,[],p.OutputKind));SuspendContinuationForQinglongFollowup(old.ResolutionId);}
  else if(parent is{}id){var program=GetActiveProgramFrame(id);ReplaceRuntimeTop(program=program with{WeijieReturn=new(p,0,0),Step=ResolutionFrameStep.ResolvingEffect});if(f.IsAssistedProgramUse){if(IsSayingSlashRequest(program)||IsChanshuangSlashRequest(program)||IsQigongRequest(program)||IsWuJingHejiRequest(program)||IsCaoChunInitialRequest(program))PrepareWeijieProgram(program,p);else CommitProgramChoiceResult(program.Id,f.AssistedResultBind!,"used-slash",n.ActorSeat,"诿解供给真实杀。");}}
  f.AwaitingProviders=false;AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(f.ResolutionId,n.ActorSeat,true,n.ProviderSeat,0,p.OutputKind,true,p.TargetSeat));BeginWeijieSlash(p,borrowed,!f.IsAssistedProgramUse&&!f.IsQinglongCrescentBladeUse,parent,f);RewardFactionRequestProvider(reward,n.ProviderSeat);
 }
 private bool IsWeijieZeroSupply(CardUseFrame use,FactionCardRequestHandle request)=>use.WeijieOrigin is{}p&&p.Need.Intent==WeijieIntent.FactionSlashUse&&IsIssuedWeijieUse(use)&&p.Need.OwnerFrameId==request.OwnerFrameId&&p.Need.ActorSeat==request.OwnerSeat&&p.Need.ProviderSeat==request.CurrentCandidateSeat&&p.Need.RequesterSeat==request.OwnerSeat&&p.Need.Cursor==request.CandidateIndex&&request.TargetSeat==p.TargetSeat&&request.ActiveAttack?.ResolutionId==use.Id;
 private void RepublishWeijieNative(WeijieReceipt r)
 {
  var n=r.Need;var actor=_players[n.ProviderSeat];if(!_resolutionStack.Any(f=>f.Id==n.RequestFrameId)||_winner!=Winner.None)return;
  var keepCurrentRequest=actor.IsAlive&&r.Paid is{}requestPayment&&ValidWeijiePayment(requestPayment)&&SameWeijieNeed(requestPayment.Need,n)&&WeijieNativeNeedCurrent(r);
  if(!actor.IsAlive&&n.Intent is WeijieIntent.Dodge or WeijieIntent.Group or WeijieIntent.Duel)
  {if(_resolutionStack.LastOrDefault()is ResponseWindowFrame response&&response.ParentFrameId==n.OwnerFrameId)PopResponseWindow(n.OwnerFrameId);SetResponseParentStep(n.OwnerFrameId,ResolutionFrameStep.ResolvingEffect);var attack=n.Intent==WeijieIntent.Group?ActiveGroupCard?.CurrentAttack:n.Intent==WeijieIntent.Duel?ActiveDuel?.Attack:ActiveCardAttack;if(attack is not null&&attack.ResolutionId==n.OwnerFrameId)CompleteAttack(attack);return;}
  if(!actor.IsAlive&&n.Intent==WeijieIntent.Dying){RunOneDyingStep();return;}
  if(n.Intent==WeijieIntent.Dying){RequestHumanDyingResponse(actor,GetDyingPeaches(actor),GetDyingAlcohols(actor,ActiveDying!.VictimSeat));return;}
  if(n.Intent==WeijieIntent.FactionDodge){AdvanceFactionDefenseCandidate(keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent is WeijieIntent.FactionSlashPlayed or WeijieIntent.FactionSlashUse){AdvanceFactionSlashCandidate(keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent==WeijieIntent.Qinglong)
  {
   if(!actor.IsAlive)
   {
    if(r.Paid is not{}paid||!ValidWeijiePayment(paid)||!WeijieNativeNeedCurrent(r)||ActiveQinglongCrescentBlade is not{}blade||blade.Attack.ResolutionId!=n.OwnerFrameId||blade.Attack.SourceSeat!=n.ActorSeat||!SameAttackOwner(ActiveCardAttack,blade.Attack))throw new InvalidOperationException("A dead Weijie payer lost its exact still-unconsumed Qinglong parent.");
    // This is the native no-follow-up return: no card or dead-player menu is issued.
    var attack=blade.Attack;ActiveQinglongCrescentBlade=null;ClearPendingDecision();SetCardUseStep(attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);
    AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(attack.ResolutionId,n.ActorSeat,attack.TargetSeat,false,[],null));RestoreFactionRequestAfterQinglongChoice(attack.ResolutionId);CompleteAttack(attack);return;
   }
   PublishQinglongCrescentBladeChoice(ActiveQinglongCrescentBlade!,CanRequestQinglongCrescentBladeFactionSlash(ActiveQinglongCrescentBlade!));return;
  }
  if(n.Intent==WeijieIntent.BorrowedSword){var b=ActiveBorrowedSword!;PopResponseWindow(b.ResolutionId);BeginBorrowedSwordSlashChoice(b,CanRequestBorrowedSwordFactionSlash(b),keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent==WeijieIntent.Duel){var d=ActiveDuel!;PopResponseWindow(d.ResolutionId);BeginDuelResponse(d,keepCurrentRequest:keepCurrentRequest);return;}
  if(n.Intent is WeijieIntent.Dodge or WeijieIntent.Group)
  {var attack=n.Intent==WeijieIntent.Group?ActiveGroupCard!.CurrentAttack!:ActiveCardAttack!;var required=n.Intent==WeijieIntent.Dodge?CardKind.Dodge:ActiveGroupCard!.RequiredCardKind!.Value;var cards=GetResponseCards(actor,required);var choices=CreateResponseChoices(cards,required,attack.EffectiveCardKind,card=>GetEffectiveResponseKind(actor,card,required),includeBagua:required==CardKind.Dodge&&!attack.IgnoresArmor&&HasBagua(actor),includeFactionDefense:required==CardKind.Dodge&&CanRequestFactionDefense(actor,attack),includeFactionSlash:required==CardKind.Slash&&CanRequestFactionSlashResponse(actor,attack),responder:actor);_pendingDecision=WeijieNativeDecision(r.OriginalDecision) with{PromptId=CreatePromptId(),ValidCardIds=choices.SelectMany(c=>c.Cards).Distinct().ToArray(),Choices=choices};_status=actor.IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return;}
  var f=GetActiveProgramFrame(n.OwnerFrameId);var effect=GetPausedPrivateOfferEffect(f)!;
  if(!actor.IsAlive&&n.Intent==WeijieIntent.NearestLegalSlash)
  {
   if(r.Paid is not{}paid||!ValidWeijiePayment(paid)||!WeijieNativeNeedCurrent(r)||effect.Op!=SkillProgramEffectOp.RequestLegalSlashByNearest||n.RequestOp!=effect.Op||f.InstructionIndex!=n.InstructionIndex||n.ActorSeat!=n.ProviderSeat||!f.ReexecuteParticipantInstruction||f.NearestLegalSlashRequest is not{AwaitingFaction:false}draft||draft.ActorSeat!=n.ActorSeat||!draft.NearestSeats.SequenceEqual(n.Targets)||f.NumberBindings.SingleOrDefault(b=>b.Name=="participant-cursor-"+(f.InstructionIndex-1))is not{}cursor||cursor.Value<1||cursor.Value>=_playerCount||n.ActorSeat!=(f.OwnerSeat+cursor.Value)%_playerCount)throw new InvalidOperationException("A dead Weijie payer lost its exact committed nearest-participant cursor.");
   ClearPendingDecision();ReplaceRuntimeTop(f with{NearestLegalSlashRequest=null});AdvanceRuntimeProgram(f.Id);return;
  }
  if(IsSayingSlashRequest(f)){if(!actor.IsAlive)FinishSayingSuppliedSlashFailure(f);else PublishSaying(f,keepCurrentRequest:keepCurrentRequest);return;}
   if(IsChanshuangSlashRequest(f)){if(!actor.IsAlive)FinishChanshuangSuppliedSlashFailure(f);else PublishChanshuang(f);return;}if(IsQigongRequest(f)){PublishLvKuangLvXiang(f);return;}if(IsWuJingHejiRequest(f)){PublishWuJing(f,keepCurrentRequest:keepCurrentRequest);return;}if(IsCaoChunInitialRequest(f)){PublishCaoChun(f);return;}if(n.Intent==WeijieIntent.GenericBasic){PublishKeBiNeng(f);return;}
  if(n.Intent==WeijieIntent.ProgramSlash){RequestProgramSlashByTarget(f,n.ActorSeat,effect.ResultBind!,keepCurrentRequest:keepCurrentRequest);return;}if(n.Intent==WeijieIntent.ProgramNearestSlash){RequestProgramSlashByNearest(f,n.ActorSeat,effect.Amount,keepCurrentRequest:keepCurrentRequest);return;}if(n.Intent==WeijieIntent.AssistedSlash){PublishAssistedPhysicalSlashPrompt(f);return;}if(n.Intent==WeijieIntent.FixedSlash){PublishFixedSlashAndDeclaredDeckPrompt(f,FixedTargetSlashChoices(f),"选择杀响应原请求。",n.Targets);return;}
  if(n.Intent==WeijieIntent.NearestLegalSlash){var choices=NearestLegalSlashChoices(f);_pendingDecision=WeijieNativeDecision(r.OriginalDecision) with{PromptId=CreatePromptId(),ValidCardIds=choices.SelectMany(c=>c.Cards).Distinct().ToArray(),Choices=choices};_status=actor.IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;return;}
  throw new InvalidOperationException("Weijie has no native failed-request return.");
 }
}
