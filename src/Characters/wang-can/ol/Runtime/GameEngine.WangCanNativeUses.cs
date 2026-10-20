namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<LegalAction> BuildDenglouNativeBasicUseOptions(ProgramSkillFrame f)
 {
  if(f.WangCan is not{Lease:{}lease}r||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None)return [];
  var actor=_players[f.OwnerSeat];var result=new List<LegalAction>();
  foreach(var id in r.Remaining.Where(id=>_cardZones.GetLocation(id)==lease))
  {
   var card=GetAdvancedCard(id);if(GetProgramCardCategory(card.Kind)!=SkillProgramCardCategory.Basic||IsTurnPhysicalUseForbidden(actor.Seat,[id]))continue;
   var variants=new List<(CardKind Kind,CardConversionSource? Source)>{(card.Kind,null)};
   foreach(var kind in new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash,CardKind.Peach,CardKind.Alcohol})
    foreach(var conversion in GetProgramViewAsConversions(actor,card,kind,forResponse:false))
     if(ViewAsRule(conversion)is{DeclarationValidation:null}rule&&rule.InputCount==1&&rule.SourceZones.Contains(lease.Zone))variants.Add((kind,conversion));
   foreach(var(kind,conversion)in variants.Distinct())
   {
    var appearance=WuJingLiubingUseCard(actor,card,kind,conversion,[id]);var suit=GetProgramEffectiveSuit(actor,appearance);var color=SuitColor(suit);if(IsCardUseForbidden(actor.Seat,kind,CardActionType.Use))continue;
    if(IsSlashCard(kind))
    {
     var targets=_players.Where(t=>CanUseVirtualSlashTarget(actor,t,kind,suit,color,card.Rank,[id],conversionSource:conversion,actualUnconvertedPhysical:conversion is null)&&!IsDirectedCardTargetProhibited(actor.Seat,t.Seat,kind)&&!IsCardTargetProhibited(t,kind,suit,color)&&!HasBeneficiarySuitShield(actor.Seat,t.Seat,suit)).ToArray();
     foreach(var target in targets)result.Add(new(LegalActionKind.Slash,id,target.Seat,$"登楼：将【{card.DisplayName}】对{target.Name}使用",kind,TargetSeats:[target.Seat]){ConversionSource=conversion});
     var multiple=new List<LegalAction>();AddProgramTargetCountSlashActions(multiple,actor,card,targets,CardCatalog.Get(kind).DisplayName,kind);result.AddRange(multiple.Select(a=>a with{ConversionSource=conversion}));
    }
    else if(actor.HasHpValue&&!HasSelfCardTargetProhibition(actor.Seat)&&!IsDirectedCardTargetProhibited(actor.Seat,actor.Seat,kind)&&!IsCardTargetProhibited(actor,kind,suit,color)&&!HasBeneficiarySuitShield(actor.Seat,actor.Seat,suit))
    {
     if(kind==CardKind.Peach&&IsRuleWounded(actor))result.Add(new(LegalActionKind.Peach,id,actor.Seat,$"登楼：使用【桃】",kind,TargetSeats:[actor.Seat]){ConversionSource=conversion});
     if(kind==CardKind.Alcohol&&!actor.HasAlcoholEffect&&(HasWuLuKangDixianMaterialPrivilege(actor,kind,[id],conversion)||CanUseJianjiOrdinaryAlcohol(actor,null)||HasTargetCardQuotaAllowance(actor.Seat,actor.Seat)||HasNextUnlimitedCard(actor)||HasMouWujingAllowance(actor)||HasFriendQihuiAllowance(actor)||HasCardPolicy(actor,SkillProgramCardPolicyKind.UnlimitedAlcoholUse,CardKind.Alcohol)||HasRoundGainedBasicBonus(actor,CardKind.Alcohol,[id])))result.Add(new(LegalActionKind.Alcohol,id,actor.Seat,"登楼：使用【酒】",kind,TargetSeats:[actor.Seat]){ConversionSource=conversion});
    }
   }
  }
  return Array.AsReadOnly(result.Select(a=>a with{TargetSeats=Array.AsReadOnly(a.TargetSeats.ToArray())}).ToArray());
 }
 private void IssueDenglouNativeUse(ProgramSkillFrame f,LegalAction action)
 {
  RequireIssuedWangCan(f);var r=f.WangCan!;var actor=_players[f.OwnerSeat];var id=action.CardId!.Value;
  if(r.Stage!=WangCanStage.DenglouChoose||!r.Remaining.Contains(id)||_cardZones.GetLocation(id)!=r.Lease||!BuildDenglouNativeBasicUseOptions(f).Any(a=>a.Kind==action.Kind&&a.CardId==id&&a.PlayedCardKind==action.PlayedCardKind&&a.ConversionSource==action.ConversionSource&&a.TargetSeats.SequenceEqual(action.TargetSeats)))throw new InvalidOperationException("Denglou cannot replace the exact private basic entity or its actual legal use.");
  var card=GetAdvancedCard(id);var kind=action.PlayedCardKind??card.Kind;var useId=BeginCardUse(card,f.OwnerSeat,action.TargetSeats,kind,physicalCardIds:[id],conversionSource:action.ConversionSource??r.Source);var use=LifecycleCardUse(useId)!;
  var receipt=new WangCanNativeReceipt(f.Id,r.Source,r.GameplayHash,r.Instruction,r.ActualTurn,r.Phase,r.WindowId,id,r.Lease!.Value,kind,action.ConversionSource,useId,use.Action!.ActionId,use.Action.ParentActionId){Targets=action.TargetSeats};
  var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(p=>p.Id==f.Id);if(parent.WangCan!=r)throw new InvalidOperationException("Denglou original private lease changed during native issuance.");ReplaceRuntimeFrame(f.Id,parent with{WangCan=r with{Stage=WangCanStage.DenglouNative,Native=receipt,Remaining=r.Remaining.Where(entity=>entity!=id).ToArray()}});
  UpdateLifecycleCardUse(useId,u=>u with{DenglouNative=receipt,DenglouCostBefore=_movementSequence,DenglouCostAfter=_movementSequence,DenglouCostDrained=false});MoveCard(card,receipt.From,CardLocation.Processing,CardMoveReasons.Use);UpdateLifecycleCardUse(useId,u=>u with{DenglouCostAfter=_movementSequence});AdvanceEventRulesAndQueueFact(new WangCanNativeIssuedEvent(f.Id,r.Source,r.GameplayHash,useId,receipt.ActionId,f.OwnerSeat,kind));ContinueDenglouNativePayment(useId);
 }
 private bool IsDenglouNativeUse(CardUseFrame use)=>use.DenglouNative is not null;
 private bool DenglouNativeRidesOn(ProgramSkillFrame parent,CardUseFrame use)=>parent.WangCan is{Stage:WangCanStage.DenglouNative,Native:{}n}r&&use.DenglouNative==n&&n.ProgramFrameId==parent.Id&&n.Source==WangCanSource(parent)&&n.GameplayHash==parent.GameplayHash&&n.From==r.Lease&&r.Entities.Contains(n.CardId)&&!r.Remaining.Contains(n.CardId);
 [NativeUseValidation(90)]
 private void ValidateDenglouNativeUseReturn(CardUseFrame use)
 {
     if (IsDenglouNativeUse(use)) AssertDenglouNativeUse(use);
 }
 private void AssertDenglouNativeUse(CardUseFrame use)
 {
  if(use.DenglouNative is not{}n||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p=>p.Id==n.ProgramFrameId)is not{}parent||!DenglouNativeRidesOn(parent,use)||use.Id!=n.UseId||use.Action is not{Type:CardActionType.Use}a||!ShownEntityUseActorMatches(use,parent.OwnerSeat,parent.OwnerSeat)||a.ActionId!=n.ActionId||a.ParentActionId!=n.ParentActionId||a.EffectiveKind!=n.EffectiveKind||use.CardId!=n.CardId||use.CardKind!=n.EffectiveKind||!a.PhysicalCards.Select(c=>c.CardId).SequenceEqual([n.CardId])||a.PhysicalCards.Single().From!=n.From||!a.ConversionChain.SequenceEqual([n.Conversion??n.Source])||!a.TargetSeats.SequenceEqual(use.TargetSeats)||use.DenglouCostBefore is not{}before||use.DenglouCostAfter is not{}after||_cardMovements.Count(m=>m.Sequence>before&&m.Sequence<=after&&m.CardId==n.CardId&&m.From==n.From&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)!=1||CompleteProgramEventHistory().OfType<WangCanNativeIssuedEvent>().Count(e=>e.FrameId==parent.Id&&e.Source==n.Source&&e.GameplayHash==n.GameplayHash&&e.UseId==use.Id&&e.ActionId==n.ActionId&&e.ActorSeat==parent.OwnerSeat&&e.EffectiveKind==n.EffectiveKind)!=1)throw new InvalidOperationException("Denglou native use lost its exact private entity, source, original actor, native action or once-only payment.");
  RequireIssuedWangCan(parent);
 }
 [NativeFrameRoute(typeof(CardUseFrame), 150, typeof(NativeFrameRouteConditions), nameof(NativeFrameRouteConditions.DenglouPayment))]
 private void ContinueDenglouNativePayment(long id)
 {
  var use=LifecycleCardUse(id)!;AssertDenglouNativeUse(use);if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.DenglouCardUse)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.DenglouCardUse)||TryBeginCardsMovedProgramWindow(id))return;
  UpdateLifecycleCardUse(id,u=>u with{DenglouCostDrained=true});var n=use.DenglouNative!;var card=GetAdvancedCard(n.CardId);var actor=_players[use.SourceSeat];
  if(_winner!=Winner.None||!actor.IsAlive){FinishCardUse(id,card,n.EffectiveKind);return;}
  if(n.EffectiveKind==CardKind.Peach){BeginSimpleCardUse(id,new(card.Id,SimpleCardUseEffect.Recovery,1));return;}
  if(n.EffectiveKind==CardKind.Alcohol){BeginSimpleCardUse(id,new(card.Id,SimpleCardUseEffect.Alcohol));return;}
  var targets=LifecycleCardUse(id)!.TargetSeats;var nuzhan=GetNuzhanModifiers(id,actor);var amount=1+actor.AlcoholDamageBonus+nuzhan.DamageBonus;CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;
  if(targets.Count>1)
  {var pending=new FangtianHalberdHandle(this,id,actor.Seat,card,n.EffectiveKind,false,amount,targets.ToArray(),false,false,n.Conversion??n.Source);ActiveFangtianHalberd=pending;var rule=EvaluateCardTargetCount(actor,n.EffectiveKind);AdvanceEventRulesAndQueueFact(new ProgramCardTargetCountAppliedEvent(id,actor.Seat,n.EffectiveKind,targets,Array.AsReadOnly(rule.Value.Contributions.Select(c=>c.SourceId).ToArray())));AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id,n.EffectiveKind,actor.Seat,targets[0]));foreach(var target in targets)NotifyAiOfSlash(actor,_players[target]);BeginNextFangtianHalberdTarget(pending);return;}
  var attack=new CardAttackHandle(this,id,actor.Seat,targets.Single(),card,damageAmount:amount,playedCardKind:n.EffectiveKind,physicalCards:[card],ignoresArmor:HasCardArmorBypass(actor,_players[targets.Single()],n.EffectiveKind),programSkillCardUseFrameId:n.ProgramFrameId);ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,n.EffectiveKind);AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id,n.EffectiveKind,actor.Seat,targets.Single()));TryMarkProgramUseCommitted(id);if(!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,targets,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
 }
 [NativeUseReturn(10)]
 private bool CompleteDenglouUseReturn(CardUseFrame use)
 {
     if (!IsDenglouNativeUse(use)) return false;
     ReturnDenglouNativeUse(use);
     return true;
 }
 private void ReturnDenglouNativeUse(CardUseFrame use)
 {
  if(use.DenglouNative is not{}n||use.CardAttack is{Active:true})return;FinishDenglouNativeReturn(n);
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 120)]
 private bool ReturnRegisteredCompleteDenglouNativeAttack(AttackCompletionReceipt completion)
 {
     if (completion.DenglouNative is null) return false;
     CompleteDenglouNativeAttack(completion);
     return true;
 }
 private void CompleteDenglouNativeAttack(AttackCompletionReceipt completion)=>FinishDenglouNativeReturn(completion.DenglouNative!);
 private void FinishDenglouNativeReturn(WangCanNativeReceipt n)
 {
  var parent=GetActiveProgramFrame(n.ProgramFrameId);if(parent.WangCan is not{Stage:WangCanStage.DenglouNative,Native:{}issued}r||issued!=n||LifecycleCardUse(n.UseId)is not null||CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==n.UseId)!=1||CompleteProgramEventHistory().OfType<WangCanNativeReturnedEvent>().Any(e=>e.FrameId==parent.Id&&e.UseId==n.UseId))throw new InvalidOperationException("Denglou must return once after its entire native basic-card use.");
  AdvanceEventRulesAndQueueFact(new WangCanNativeReturnedEvent(parent.Id,n.UseId,n.ActionId));ReplaceRuntimeTop(parent with{WangCan=r with{Stage=WangCanStage.Completed}});
 }
 private bool DenglouNativePaymentEdge(int index)
 {
  if(index<1||_resolutionStack[index-1]is not CardUseFrame{DenglouNative:{}n,DenglouCostDrained:false}use)return false;AssertDenglouNativeUse(use);
  var child=_resolutionStack[index];if(child is CardsMovedTriggerWindowFrame moved)return moved.ResumeDenglouUseFrameId==use.Id&&moved.Batch.ParentFrameId==use.Id&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>use.DenglouCostBefore&&m.Sequence<=use.DenglouCostAfter&&m.CardId==n.CardId&&m.From==n.From&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use);
  if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==use.Id&&hp.Change.ParentFrameId==use.Id&&hp.Continuation==PostEventContinuation.DenglouCardUse;
  return child is RecoveryReplacementFrame recovery&&recovery.ParentFrameId==use.Id&&recovery.Return.ResumeFrameId==use.Id&&recovery.Return.Continuation==PostEventContinuation.DenglouCardUse;
 }
}
