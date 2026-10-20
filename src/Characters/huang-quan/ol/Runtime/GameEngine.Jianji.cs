using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private SkillProgramStepOutcome DrawAndOfferJianjiExactCard(ProgramSkillFrame supplied,int target)
 {var f=GetActiveProgramFrame(supplied.Id);if(f.SelectedTargetSeats is not[var selected]||target!=selected||target==f.OwnerSeat||!_players[target].IsAlive||_phase!=TurnPhase.Play||f.OwnerSeat!=_currentSeat||f.TriggerId is not null||f.SkillId=="dragonboat:jianji"&&!DragonBoatAllies(f.OwnerSeat,target))throw new InvalidOperationException("Jianji requires its accepted actual own-Play other-living target.");return IssueHuangQuanDraw(f,target,"jianji",null);}
 private bool IsJianjiExactCardQuery(CharacterState actor,int? selectedId)
 {
  if(IsJianjiDeclarationQuery(actor,selectedId))return true;
  if(selectedId is not{}id||_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HuangQuan:{Kind:"jianji",Stage:"offer" or "issuing"}p}f||p.RecipientSeat!=actor.Seat||!actor.IsAlive||p.CardIds is not[var drawn]||id!=drawn||_cardZones.GetLocation(id)!=CardLocation.Hand(actor.Seat)||!ValidHuangQuanDraw(f))return false;
  return p.Stage=="offer"||p.NativeUse is {UseId:0,ActionId:0}r&&r.ActorSeat==actor.Seat&&r.CardId==id&&r.ProgramFrameId==f.Id;
 }
 private IReadOnlyList<LegalAction> JianjiActions(ProgramSkillFrame f)
 {
  if(f.HuangQuan is not{Kind:"jianji",Stage:"offer",CardIds:[var id]}p||!_players[p.RecipientSeat].IsAlive||_winner!=Winner.None||_cardZones.GetLocation(id)!=CardLocation.Hand(p.RecipientSeat))return [];
  return Array.AsReadOnly(BuildLegalActions(_players[p.RecipientSeat],includeProgramActions:false,selectedPhysicalCardId:id).Where(a=>a.CardId==id&&a.MinCardCount<=1&&a.MaxCardCount<=1&&(a.ConversionSource is null||ViewAsRule(a.ConversionSource) is not{}rule||rule.InputCount==1)&&a.Kind is not(LegalActionKind.EndPlay or LegalActionKind.Recast or LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral)).Select(a=>a with{TargetSeats=Array.AsReadOnly(a.TargetSeats.ToArray()),AdditionalConversionSources=Array.AsReadOnly((a.AdditionalConversionSources??[]).ToArray()),SelectableCardIds=Array.AsReadOnly(a.SelectableCardIds.ToArray()),SelectableTargetSeats=Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),SelectableCardTargets=a.SelectableCardTargets is null?null:Array.AsReadOnly(a.SelectableCardTargets.ToArray()),SelectableTargetSequences=a.SelectableTargetSequences is null?null:Array.AsReadOnly(a.SelectableTargetSequences.Select(t=>(IReadOnlyList<int>)Array.AsReadOnly(t.ToArray())).ToArray())}).ToArray());
 }
 private IReadOnlyList<PromptChoice> JianjiChoices(ProgramSkillFrame f)
 {
  var result=JianjiActions(f).Select((a,i)=>new PromptChoice(new($"jianji.{f.Id}.{i}"),a.Description,[a.CardId!.Value],a.TargetSeats,new Dictionary<string,string>{{"program-action","jianji-use"},{"option",i.ToString(CultureInfo.InvariantCulture)}})).ToList();result.Add(new(new($"jianji.{f.Id}.decline"),"不使用此牌",[],[],new Dictionary<string,string>{{"program-action","jianji-use"},{"option","decline"}}));return Array.AsReadOnly(result.ToArray());
 }
 private void PublishJianjiChoice(ProgramSkillFrame f)
 {
  var p=f.HuangQuan!;if(JianjiActions(f).Count==0){FinishHuangQuan(f);return;}var choices=JianjiChoices(f);var definition=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,p.RecipientSeat,"可以使用谏计刚摸到的这张牌，或不使用。",p.CardIds,choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),TargetSeat=p.RecipientSeat,IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,definition.Name,definition.Name,definition.Description)};_status=_players[p.RecipientSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswerBefore(0)]
 private bool TryResolveJianjiChoice(PromptChoice choice)
 {
  if(choice.Parameters.GetValueOrDefault("program-action")!="jianji-use")return false;
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HuangQuan:{Stage:"offer"}p}f||_pendingDecision is not{}prompt||prompt.PlayerSeat!=p.RecipientSeat||!JianjiChoices(f).Any(c=>AssistedChoicesEqual([c],[choice]))||!ValidHuangQuanDraw(f))throw new InvalidOperationException("Jianji requires its actual recipient's published exact-card use choice.");
  var value=choice.Parameters["option"];if(value=="decline"){ClearPendingDecision();FinishHuangQuan(f);return true;}var action=JianjiActions(f)[int.Parse(value,CultureInfo.InvariantCulture)];var receipt=new JianjiNativeReceipt(f.Id,f.InstructionIndex,p.Source,f.GameplayHash,p.ActualTurn,p.TurnOwner,p.ActualPhase,p.RecipientSeat,p.CardIds.Single(),action,0,0);
  ClearPendingDecision();ReplaceRuntimeTop(f=f with{HuangQuan=p with{Stage="issuing",NativeUse=receipt},PendingMovementContinuation=null});ExecuteAction(_players[p.RecipientSeat],action);return true;
 }
 private bool ValidJianjiReceipt(JianjiNativeReceipt r,ProgramSkillFrame parent,bool bound)
 {
  if(parent.HuangQuan is not{Kind:"jianji"}p||r.ProgramFrameId!=parent.Id||r.Source!=p.Source||r.GameplayHash!=parent.GameplayHash||r.InstructionIndex!=parent.InstructionIndex||r.ActorSeat!=p.RecipientSeat||p.CardIds is not[var id]||r.CardId!=id||r.ActualTurn!=p.ActualTurn||r.TurnOwner!=p.TurnOwner||r.ActualPhase!=p.ActualPhase||r.Action.CardId!=id||!ValidHuangQuanDraw(parent))return false;
  return !bound||r.UseId>0&&r.ActionId>0&&CompleteProgramEventHistory().OfType<JianjiUseIssuedEvent>().Count(e=>SameJianjiReceipt(e.Receipt,r))==1;
 }
 private bool SameJianjiReceipt(JianjiNativeReceipt a,JianjiNativeReceipt b)=>(a with{Action=b.Action})==b&&a.Action.Kind==b.Action.Kind&&a.Action.CardId==b.Action.CardId&&a.Action.PlayedCardKind==b.Action.PlayedCardKind&&a.Action.TargetCardId==b.Action.TargetCardId&&a.Action.ConversionSource==b.Action.ConversionSource&&a.Action.TargetSeats.SequenceEqual(b.Action.TargetSeats)&&(a.Action.AdditionalConversionSources??[]).SequenceEqual(b.Action.AdditionalConversionSources??[]);
 private void BindJianjiNativeUse(CardUseFrame use)
 {
  if(_resolutionStack.Count<2||_resolutionStack[^2]is not ProgramSkillFrame{HuangQuan:{Stage:"issuing",NativeUse:{UseId:0,ActionId:0}r}p}parent)return;
  var action=use.Action??throw new InvalidOperationException("Jianji native use lost its real action.");
  var targets=JianjiExpectedNativeTargets(r);
  if(!ValidJianjiReceipt(r,parent,false)||use.SourceSeat!=r.ActorSeat||use.CardId!=r.CardId||use.CardKind!=(r.Action.PlayedCardKind??GetAttackCard(r.CardId).Kind)||use.PhysicalCardIds is not[var physical]||physical!=r.CardId||action.Type!=CardActionType.Use||action.ActorSeat!=r.ActorSeat||action.ProviderSeat!=r.ActorSeat||action.RequesterSeat is not null||action.PhysicalCards is not[var cost]||cost.CardId!=r.CardId||!action.ConversionChain.SequenceEqual((r.Action.ConversionSource is{}primary?new[]{primary}:Array.Empty<CardConversionSource>()).Concat(r.Action.AdditionalConversionSources??[]))||!use.TargetSeats.SequenceEqual(targets))throw new InvalidOperationException("Jianji changed its actual drawn material, actor/provider, native action or accepted target list.");
  var bound=r with{UseId=use.Id,ActionId=action.ActionId};ReplaceRuntimeFrame(parent.Id,parent with{HuangQuan=p with{Stage="native-use",NativeUse=bound}});UpdateLifecycleCardUse(use.Id,current=>current with{JianjiUse=bound});AdvanceEventRulesAndQueueFact(new JianjiUseIssuedEvent(bound));
 }
 private IReadOnlyList<int> JianjiExpectedNativeTargets(JianjiNativeReceipt r)
 {
  var a=r.Action;var actor=_players[r.ActorSeat];var card=ApplyProgramUseAppearance(actor,GetAttackCard(r.CardId),a.ConversionSource);
  if(a.ConversionSource is{}source&&ViewAsRule(source)is{SingleCardTrickUse:true}rule)
   return BuildProgramOrdinaryTrickUseOptions(actor,rule.OutputKind,EffectiveSuit(actor,GetAttackCard(r.CardId)),rule.ExcludeOwnerEffects,enforceUsePermission:true,beneficiaryShieldSuit:EffectiveSuit(actor,card),actualEffectiveColor:SuitColor(EffectiveSuit(actor,card)),hasActualColor:true,physicalCardIds:[r.CardId]).Where(o=>o.ActionKind==a.Kind&&o.TargetCardId==a.TargetCardId&&(o.TargetSeats.SequenceEqual(a.TargetSeats)||a.ProgramActivationId=="red-additional-targets"&&o.TargetSeats.SequenceEqual(a.TargetSeats.Take(o.TargetSeats.Count)))).OrderByDescending(o=>o.TargetSeats.Count).Select(o=>o.TargetSeats).First();
  if(GuanduExpectedNativeTargets(actor,card,a)is{}guanduTargets)return guanduTargets;
  if(DragonBoatExpectedNativeTargets(actor,card,a)is{}dragonTargets)return dragonTargets;
  var effectiveKind=a.PlayedCardKind??card.Kind;
  if(a.Kind==LegalActionKind.Peach)return [actor.Seat];
  if(a.Kind is not(LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains))return GetImplicitSelfCardUseTargets(a.PlayedCardKind??card.Kind,actor.Seat,a.TargetSeats);
  var offensive=a.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage;var seats=Enumerable.Range(offensive?1:0,_playerCount-(offensive?1:0)).Select(offset=>_players[(actor.Seat+offset)%_playerCount]).Where(p=>p.IsAlive&&(!offensive||!HasCardPolicy(p,SkillProgramCardPolicyKind.ExcludeGlobalTarget,effectiveKind))&&!IsCardTargetProhibited(p,effectiveKind,card.Suit,SuitColor(EffectiveSuit(actor,card)))&&!HasBeneficiarySuitShield(actor.Seat,p.Seat,EffectiveSuit(actor,card))).Select(p=>p.Seat).ToArray();
  if(!offensive)seats=ApplyTurnCardGroupTargetRestrictions(actor,seats).ToArray();if(a.Kind==LegalActionKind.FiveGrains)seats=seats.Take(_cardZones.Count(CardLocation.DrawPile)+_cardZones.Count(CardLocation.DiscardPile)).ToArray();return Array.AsReadOnly(seats);
 }
 [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 40)]
 private bool IsJianjiOffTurnNativeUse(long id,int actor)=>actor!=_currentSeat&&LifecycleCardUse(id)is{JianjiUse:{}r}use&&r.ActorSeat==actor&&_resolutionStack.SingleOrDefault(f=>f.Id==r.ProgramFrameId)is ProgramSkillFrame parent&&JianjiNativeUseRidesOn(use,parent);
 private bool JianjiNativeUseRidesOn(CardUseFrame use,ProgramSkillFrame parent)=>use.JianjiUse is{}r&&parent.HuangQuan is{NativeUse:{}pending}&&SameJianjiReceipt(r,pending)&&ValidJianjiReceipt(r,parent,true)&&use.Action is{Type:CardActionType.Use}a&&a.ActionId==r.ActionId&&a.ActorSeat==r.ActorSeat&&a.ProviderSeat==r.ActorSeat&&a.PhysicalCards is[var cost]&&cost.CardId==r.CardId;
 [NativeUseValidation(50)]
 private void AssertJianjiNativeUseCompletion(CardUseFrame use)
 {if(use.JianjiUse is not{}r)return;if(use.Step!=ResolutionFrameStep.Completed||_resolutionStack.LastOrDefault()?.Id!=use.Id||_resolutionStack.SingleOrDefault(f=>f.Id==r.ProgramFrameId)is not ProgramSkillFrame parent||!JianjiNativeUseRidesOn(use,parent))throw new InvalidOperationException("Jianji must return through the exact issued native use after its children.");}
 [NativeUseReturn(60)]
 private bool CompleteJianjiUseReturn(CardUseFrame use)
 {
     if (use.JianjiUse is null) return false;
     ReturnJianjiNativeUse(use);
     return true;
 }
 private void ReturnJianjiNativeUse(CardUseFrame use)
 {
  if(use.JianjiUse is not{}r)return;if(_resolutionStack.Any(f=>f.Id==use.Id)||use.Step!=ResolutionFrameStep.Completed||_resolutionStack.SingleOrDefault(f=>f.Id==r.ProgramFrameId)is not ProgramSkillFrame parent||!JianjiNativeUseRidesOn(use,parent)||CompleteProgramEventHistory().OfType<JianjiUseReturnedEvent>().Any(e=>SameJianjiReceipt(e.Receipt,r)))throw new InvalidOperationException("Jianji cannot return or repay another native use.");
  ReplaceRuntimeFrame(parent.Id,parent with{HuangQuan=parent.HuangQuan! with{Stage="returned"}});AdvanceEventRulesAndQueueFact(new JianjiUseReturnedEvent(r));if(use.CardAttack is not{Active:true})AdvanceRuntimeProgram(parent.Id);
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 10)]
 private bool TryResumeJianjiAfterNativeAttack()
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{HuangQuan:{Stage:"returned",NativeUse:{}}}p)return false;AdvanceRuntimeProgram(p.Id);return true;}
}
public sealed partial class GameEngine
{
 private void RecordJianjiOrdinaryAlcoholUse(long id)
 {
  if(!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.DrawAndOfferJianjiExactCard)&&!HasKeBiNengCapability&&!HasWeijieCapability&&!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards)&&!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack))return;
  var use=LifecycleCardUse(id)??throw new InvalidOperationException("Ordinary alcohol requires its native use owner.");
  if(use.DyingResponse is not null||use.Action is not{Type:CardActionType.Use}action||
      !(use.CardKind==CardKind.Alcohol&&action.EffectiveKind==CardKind.Alcohol||IsDragonBoatRealgarWineQuotaUse(use,action)))throw new InvalidOperationException("Jianji ordinary alcohol quota cannot consume a dying recovery or unrelated card.");
  if(CompleteProgramEventHistory().OfType<JianjiOrdinaryAlcoholPaidEvent>().Any(e=>e.NativeUseId==id))return;
  AdvanceEventRulesAndQueueFact(new JianjiOrdinaryAlcoholPaidEvent(id,action.ActionId,_turnNumber,action.ActorSeat,!HasIssuedSiruiUse(id,action.ActorSeat)&&!HasIssuedRoundGainedBasicBonus(id,action.ActorSeat)&&(!HasKeBiNengCapability||!HasIssuedFuRongUnlimitedUse(id,action.ActorSeat)),use.JianjiUse?.ProgramFrameId));
 }
 private bool CanUseJianjiOrdinaryAlcohol(CharacterState actor,int? selectedId)=>(IsJianjiExactCardQuery(actor,selectedId)||IsKeBiNengPingduanBasicQuery(actor)||IsQingyaDiscardQuery(actor,selectedId)||IsHuiyunExactHandQuery(actor,selectedId)||IsWeijieOrdinaryAlcoholQuery(actor))?!CompleteProgramEventHistory().OfType<JianjiOrdinaryAlcoholPaidEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.ActorSeat==actor.Seat&&e.CountsQuota):!actor.UsedPlayPhaseAlcoholThisTurn;
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 560)]
 private void ObserveJianjiAlcoholExpiry(IGameEvent payload)
 {
  if(payload is not TurnEndedEvent ended||!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.DrawAndOfferJianjiExactCard))return;
  foreach(var actor in CompleteProgramEventHistory().OfType<JianjiOrdinaryAlcoholPaidEvent>().Where(e=>e.ActualTurn==ended.TurnNumber&&e.JianjiProgramFrameId is not null&&e.ActorSeat!=ended.ActorSeat).Select(e=>e.ActorSeat).Distinct().ToArray())
   if(_players[actor].HasAlcoholEffect){_players[actor].HasAlcoholEffect=false;AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(actor));}
 }
}
