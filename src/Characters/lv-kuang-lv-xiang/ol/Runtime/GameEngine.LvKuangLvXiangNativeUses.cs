namespace CardGame.Core;
public sealed partial class GameEngine
{
 private static bool SameQigongNativeReceipt(QigongNativeReceipt? left,QigongNativeReceipt? right)
 {
  if(left is null||right is null)return left is null&&right is null;
  return SameOverflowAction(left.OriginalAction,right.OriginalAction)&&
   (left.OriginalAction.DesignatedTargetSeats is null)==(right.OriginalAction.DesignatedTargetSeats is null)&&
   (left.OriginalAction.DesignatedTargetSeats is null||left.OriginalAction.DesignatedTargetSeats.SequenceEqual(right.OriginalAction.DesignatedTargetSeats!))&&
   (left with{OriginalAction=right.OriginalAction})==right;
 }
 private IReadOnlyList<PromptChoice> QigongSlashChoices(ProgramSkillFrame f)
 {
  var r=f.LvKuangLvXiang!;if(!IsValidPlayerSeat(r.Actor)||!_players[r.Actor].IsAlive||!_players[r.Target].IsAlive)return [];var actor=_players[r.Actor];var target=_players[r.Target];var result=new List<PromptChoice>();
  bool Legal(Card c,CardKind k,CardConversionSource?source=null,IReadOnlyList<int>?ids=null)=>CanUseSlashTarget(actor,target,c,source,k,ignoreDistance:true,noEffectiveRank:ids?.Count>1,physicalCardIds:ids)&&!IsTurnPhysicalUseForbidden(actor.Seat,ids??[c.Id]);
  void Add(string key,IReadOnlyList<int>cards,CardKind kind,CardConversionSource?source=null,string?equipment=null,bool cardless=false)
  {var args=new Dictionary<string,string>{{"effective-kind",kind.ToString()}};if(source is not null)AddConversionParameters(args,source);if(equipment is not null)args["equipment"]=equipment;if(cardless)args["round-once-cardless"]="true";result.Add(LvChoice(f,"slash",key,"对"+target.Name+"使用杀",cards,[target.Seat],args));}
  foreach(var card in GetSlashUseCards(actor))foreach(var kind in GetSlashUseKinds(actor,IsSlashCard(card.Kind)?card.Kind:CardKind.Slash))
  {
   if(!Legal(card,kind))continue;foreach(var choice in CreateConversionChoiceVariants(actor,card,kind,false,$"qigong.{f.Id}.{card.Id}.{kind}","使用杀",[card.Id],[r.Target],new Dictionary<string,string>{{"effective-kind",kind.ToString()}}))
    if(!TryReadConversionSource(choice.Parameters,out var source)||source is null||ViewAsRule(source)?.DeclarationValidation is null)result.Add(LvChoice(f,"slash",choice.Id.Value,choice.Description,choice.Cards,choice.Targets,choice.Parameters));
  }
  foreach(var card in GetPlayableCards(actor).Concat(GetEquipment(actor)).DistinctBy(c=>c.Id))foreach(var kind in SlashKinds.Where(k=>k!=CardKind.Slash))foreach(var source in GetProgramViewAsConversions(actor,card,kind,false))if(ViewAsRule(source)?.DeclarationValidation is null&&!IsTurnHandCardRestricted(actor,card)&&Legal(card,kind,source))Add($"typed-{card.Id}-{kind}-{source.SkillInstanceId}-{source.BindingId}",[card.Id],kind,source);
  foreach(var pair in GetZhangbaSlashPairs(actor)){var ids=pair.Select(c=>c.Id).ToArray();if(Legal(pair[0],CardKind.Slash,ids:ids))Add("zhangba-"+string.Join('-',ids),ids,CardKind.Slash,equipment:CardKind.ZhangbaSerpentSpear.ToString());}
  foreach(var kind in SlashKinds)foreach(var selection in GetProgramMultiCardViewAsSelections(actor,kind,false)){var ids=selection.Cards.Select(c=>c.Id).ToArray();if(ViewAsRule(selection.Source)?.DeclarationValidation is null&&Legal(selection.Cards[0],kind,selection.Source,ids))Add($"multi-{kind}-{selection.Source.SkillInstanceId}-{selection.Source.BindingId}-"+string.Join('-',ids),ids,kind,selection.Source);}
  if(_contentRegistry.ProgramDependencies.UsesRoundOnceCardlessBasics)foreach(var(source,_)in RoundOnceCardlessRules(actor,CardKind.Slash))if(CanUseSlashTarget(actor,target,new(0,CardKind.Slash,Suit.None,0),source,CardKind.Slash,ignoreDistance:true,noEffectiveRank:true,physicalCardIds:[]))Add("cardless-"+source.SkillInstanceId+"-"+source.BindingId,[],CardKind.Slash,source,cardless:true);
  AddQigongNativeAdapterChoices(f,result);result.Add(LvChoice(f,"decline","decline","不使用杀"));return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
 }
 private void IssueQigongSlash(ProgramSkillFrame f,PromptChoice choice)
 {
  if(TryBeginQigongNativeAdapter(f,choice))return;var r=f.LvKuangLvXiang!;var actor=_players[r.Actor];var target=_players[r.Target];var kind=Enum.Parse<CardKind>(choice.Parameters["effective-kind"]);
  if(choice.Cards.Count==0)
  {
   if(choice.Parameters.GetValueOrDefault("round-once-cardless")!="true"||!TryReadConversionSource(choice.Parameters,out var source)||source is null)throw new InvalidOperationException("Qigong cardless use requires its independent true paid native source.");var paid=ConsumeRoundOnceCardlessBasic(source,false);ReplaceRuntimeTop(f=f with{LvKuangLvXiang=r with{Stage="issuing",IssuingIds=[],IssuingKind=CardKind.Slash,RoundOncePayment=paid}});IssueQigongCardless(f,source,CardKind.Slash);return;
  }
  var selection=ReadAssistedSlashPayment(actor,choice);ReplaceRuntimeTop(f=f with{LvKuangLvXiang=r with{Stage="issuing",IssuingIds=selection.Cards.Select(c=>c.Id).ToArray(),IssuingKind=selection.Kind}});
  ResolveSlashCore(actor,target,selection.Cards[0],selection.Kind,actor.Seat,physicalCards:selection.Cards,countsTowardSlashLimit:false,usesZhuqueFan:selection.Kind==CardKind.FireSlash&&selection.Cards[0].Kind==CardKind.Slash&&HasZhuqueFan(actor),conversionSource:selection.Conversion,programSkillCardUseFrameId:f.Id);
 }
 private void IssueQigongCardless(ProgramSkillFrame f,CardConversionSource source,CardKind kind)
 {
  var r=f.LvKuangLvXiang!;var actor=_players[r.Actor];var target=_players[r.Target];var logical=new Card(0,kind,Suit.None,0);var id=BeginCardUse(logical,actor.Seat,[target.Seat],kind,ignoresArmor:HasCardArmorBypass(actor,target,kind),physicalCardIds:[],conversionSource:source);if(r.RoundOncePayment is not null)IssueRoundOnceCardlessNativeUse(id,source);
  var attack=new CardAttackHandle(this,id,actor.Seat,target.Seat,card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:kind,ignoresArmor:HasCardArmorBypass(actor,target,kind),programSkillCardUseFrameId:f.Id);ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,kind);CompleteQigongNativePayment(id);AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,kind,actor.Seat,target.Seat,IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,target);if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,[target.Seat],ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
 }
 private void BindQigongNativeUse(long id)
 {
  var index=_resolutionStack.FindIndex(x=>x.Id==id);if(index<1||_resolutionStack[index]is not CardUseFrame{Action:{}a}use||_resolutionStack[index-1]is not ProgramSkillFrame{LvKuangLvXiang:{Stage:"issuing"}r}f)return;
  if(r.Native is not null||a.Type!=CardActionType.Use||a.ActorSeat!=r.Actor||a.ProviderSeat!=(r.IssuingProvider??r.Actor)||a.EffectiveKind!=r.IssuingKind||!IsSlashCard(a.EffectiveKind)||!a.TargetSeats.SequenceEqual([r.Target])||!a.PhysicalCards.Select(c=>c.CardId).SequenceEqual(r.IssuingIds)||a.PhysicalCards.Any(c=>_cardZones.GetLocation(c.CardId)!=c.From&&(use.AcceptedDeclarationPayment is not{}dp||dp.Cost!=c||dp.ProviderSeat!=a.ProviderSeat))||r.OriginalUseId is not{}original||r.OriginalActionId is not{}action)throw new InvalidOperationException("Qigong native issuance lost its exact actor, target, provider or real materials.");
  var receipt=new QigongNativeReceipt(f.Id,r.Source,r.GameplayHash,r.Instruction,r.Turn,original,action,r.Actor,r.Target,id,a.ActionId,a,r.Declaration?.Before??_movementSequence,r.Declaration?.Before??_movementSequence){RoundOncePayment=r.RoundOncePayment,DrawFundedPayment=r.DrawFundedPayment,ChainedPayment=r.ChainedPayment,DeclarationPayment=use.AcceptedDeclarationPayment};ReplaceRuntimeFrame(f.Id,f with{LvKuangLvXiang=r with{Stage="native",Native=receipt}});UpdateLifecycleCardUse(id,u=>u with{Qigong=receipt});AdvanceEventRulesAndQueueFact(new QigongNativeIssuedEvent(receipt));
 }
 private void CompleteQigongNativePayment(long id)
 {
  if(LifecycleCardUse(id)is not{Qigong:{}r}use||r.Paid)return;var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==r.ProgramId);if(!SameQigongNativeReceipt(parent.LvKuangLvXiang?.Native,r))throw new InvalidOperationException("Qigong payment lost its owning native issuer.");
  var paid=r with{After=_movementSequence,Paid=true};if(!QigongMaterialPaymentMatches(use,paid))throw new InvalidOperationException("Qigong must pay exactly its chosen original native entities or independent state cost.");ReplaceRuntimeFrame(parent.Id,parent with{LvKuangLvXiang=parent.LvKuangLvXiang!with{Native=paid}});UpdateLifecycleCardUse(id,u=>u with{Qigong=paid});AdvanceEventRulesAndQueueFact(new QigongNativePaidEvent(paid));
 }
 private bool QigongMaterialPaymentMatches(CardUseFrame use,QigongNativeReceipt r)
 {
  var action=r.OriginalAction;if(action.PhysicalCards.Count==0)return r.RoundOncePayment is{}round&&round.Source.OwnerSeat==r.Actor&&CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e=>e.Receipt==round)==1||r.DrawFundedPayment is{}draw&&ValidDrawFundedDistinctBasicPayment(draw)||r.ChainedPayment is{}chain&&ValidChainedStateBasicPayment(chain);
  return action.PhysicalCards.All(c=>_cardMovements.Count(m=>m.CardId==c.CardId&&m.From==c.From&&m.Sequence>r.Before&&m.Sequence<=r.After&&(m.Reason==CardMoveReasons.Use&&m.To==CaoChunNativeCostDestination(action,c)||r.DeclarationPayment is{}d&&d.Cost==c&&d.ProviderSeat==action.ProviderSeat&&d.DeclaredKind==action.EffectiveKind&&action.ConversionChain.Contains(d.Source)&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay"))==1);
 }
 private bool ValidQigongNativeUse(CardUseFrame use)
 {
  if(use.Qigong is not{}r||r.UseId!=use.Id||r.ActionId!=use.Action?.ActionId||r.Turn!=_turnNumber||r.OriginalAction.Type!=CardActionType.Use||r.OriginalAction.ActorSeat!=r.Actor||!r.OriginalAction.TargetSeats.SequenceEqual([r.Target])||!use.Action.PhysicalCards.SequenceEqual(r.OriginalAction.PhysicalCards)||!use.Action.ConversionChain.SequenceEqual(r.OriginalAction.ConversionChain)||use.PhysicalCardIds is null||!use.PhysicalCardIds.SequenceEqual(r.OriginalAction.PhysicalCards.Select(c=>c.CardId))||!IsSlashCard(use.CardKind)||!ShownEntityUseActorMatches(use,r.Actor,r.OriginalAction.ProviderSeat)||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==r.ProgramId)is not{LvKuangLvXiang:{}parent}f||!SameQigongNativeReceipt(parent.Native,r)||r.OriginalAction.ProviderSeat!=(parent.IssuingProvider??r.Actor)||r.Source!=LvKuangLvXiangSource(f)||r.GameplayHash!=f.GameplayHash||r.Instruction!=f.InstructionIndex||r.OriginalUseId!=parent.OriginalUseId||r.OriginalActionId!=parent.OriginalActionId)return false;
  if(CompleteProgramEventHistory().OfType<QigongNativeIssuedEvent>().Count(e=>SameQigongNativeReceipt(e.Receipt,r with{After=r.Before,Paid=false}))!=1)return false;
  return r.Paid?CompleteProgramEventHistory().OfType<QigongNativePaidEvent>().Count(e=>SameQigongNativeReceipt(e.Receipt,r))==1&&QigongMaterialPaymentMatches(use,r):r.Before==r.After&&!CompleteProgramEventHistory().OfType<QigongNativePaidEvent>().Any(e=>e.Receipt.UseId==use.Id);
 }
 private bool IsQigongIssuedDistance(long? id,int actor)=>id is{}useId&&LifecycleCardUse(useId)is{Qigong:{Paid:true},Action:{}action}use&&action.ActorSeat==actor&&ValidQigongNativeUse(use);
 private bool IsQigongUnrespondable(long id)=>LifecycleCardUse(id)is{Qigong:{Paid:true}}use&&ValidQigongNativeUse(use);
 private bool QigongCardUseRidesOn(CardUseFrame use,ProgramSkillFrame parent)=>use.Qigong?.ProgramId==parent.Id&&ValidQigongNativeUse(use);
 [NativeUseValidation(80)]
 private void ValidateQigongNativeUseReturn(ref CardUseFrame use)
 {
     AssertQigongNativeUse(use);
     if (use.Qigong is not null) use = LifecycleCardUse(use.Id)!;
 }
 private void AssertQigongNativeUse(CardUseFrame use){if(use.Qigong is null)return;CompleteQigongNativePayment(use.Id);if(!ValidQigongNativeUse(LifecycleCardUse(use.Id)!))throw new InvalidOperationException("Qigong lost its exact paid native child before retirement.");}
 [NativeUseReturn(20)]
 private bool CompleteQigongUseReturn(CardUseFrame use)
 {
     if (use.Qigong is null) return false;
     ReturnDrawFundedDistinctBasicUse(use);
     ReturnChainedStateBasicUse(use);
     return true;
 }
 [NativeUsePreparation(30)]
 private bool ReturnQigongNativeUse(CardUseFrame use)
 {
  if(use.Qigong is not{}r)return false;if(!ValidQigongNativeUse(use)||!r.Paid||CompleteProgramEventHistory().OfType<QigongNativeReturnedEvent>().Any(e=>e.UseId==use.Id))throw new InvalidOperationException("Qigong native child cannot return another or twice-paid invoice.");var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==r.ProgramId);ReplaceRuntimeFrame(parent.Id,parent with{LvKuangLvXiang=parent.LvKuangLvXiang!with{Returned=true}});AdvanceEventRulesAndQueueFact(new QigongNativeReturnedEvent(parent.Id,use.Id,r.ActionId));return true;
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 130)]
 private bool ReturnRegisteredCompleteQigongSlash(AttackCompletionReceipt completion)
 {
     if (completion.Qigong is null) return false;
     CompleteQigongSlash(completion);
     return true;
 }
 private void CompleteQigongSlash(AttackCompletionReceipt completion)
 {
  if(completion.Qigong is not{}r||!r.Paid||completion.ResolutionId!=r.UseId||completion.ProgramFrameId!=r.ProgramId||_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LvKuangLvXiang:{Returned:true,Native:{}paid}}parent||parent.Id!=r.ProgramId||!SameQigongNativeReceipt(paid,r)||_resolutionStack.Any(f=>f.Id==r.UseId)||CompleteProgramEventHistory().OfType<QigongNativeReturnedEvent>().Count(e=>e.ProgramId==parent.Id&&e.UseId==r.UseId&&e.ActionId==r.ActionId)!=1||CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==r.UseId)!=1)throw new InvalidOperationException("Qigong attack completion lost its once-consumed original native return.");RestoreQigongParentAttack(parent);AdvanceRuntimeProgram(parent.Id);
 }
 private void RestoreQigongParentAttack(ProgramSkillFrame f)
 {
  if(f.LvKuangLvXiang is not{OriginalUseId:{}id,OriginalActionId:{}actionId}r)return;
  if(LifecycleCardUse(id)is not{Action:{}action,CardAttack:{}attack}original||action.ActionId!=actionId||original.TargetSeats is not[var target]||target!=r.Target||attack.SuccessfulDodgeResponses!=attack.RequiredDodgeResponses||attack.RequiredDodgeResponses<1)throw new InvalidOperationException("Qigong must retain the actual original fully-dodged attack until its own child returns.");ActiveCardAttack=new(this,id);
 }
}
