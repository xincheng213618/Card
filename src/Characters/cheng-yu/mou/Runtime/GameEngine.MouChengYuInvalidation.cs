namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouCyUse? MouCyUse(ProgramSkillWindowContext c)
 {
  if(c.Window!=SkillProgramTriggerWindow.CardUseCommitted||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is not{}w||GetCardActionWindow(w)!=c.Window)return null;var a=w.Action;
  if(a.Type==CardActionType.Response)return w.CompletedResponseReturn is{IsCommitted:true}typed&&IsCompletedResponseUse(a,typed.Kind)?new(w.Id,w.ParentFrameId,a,true):null;
  return a.Type==CardActionType.Use&&LifecycleCardUse(w.ParentFrameId)is{Action:{}actual}u&&MouCySameAction(a,actual)&&u.CardKind==a.EffectiveKind?new(w.Id,w.ParentFrameId,a,false):null;
 }
 private static bool MouCySameAction(CardActionContext a,CardActionContext b)=>(a.ActionId,a.ParentActionId,a.Type,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,a.ResponderSeat,a.OpponentSeat,a.EffectiveKind,a.EffectiveSuit,a.EffectiveRank)==(b.ActionId,b.ParentActionId,b.Type,b.ActorSeat,b.ProviderSeat,b.RequesterSeat,b.ResponderSeat,b.OpponentSeat,b.EffectiveKind,b.EffectiveSuit,b.EffectiveRank)&&a.PhysicalCards.SequenceEqual(b.PhysicalCards)&&a.ConversionChain.SequenceEqual(b.ConversionChain);
 private bool ValidMouCyInvalidation(MouCyUseInvalidatedEvent proof,CardActionContext action,long parent)
 {
  var record=ProgramEventHistory<MouCyNameRecordedEvent>().SingleOrDefault(r=>r.FrameId==proof.RecordFrameId);var started=ProgramEventHistory<MouCyStartedEvent>().SingleOrDefault(s=>s.FrameId==proof.FrameId);
  return record is not null&&started is not null&&proof.Source.SkillId==MouChengYuAnniversaryRuntime.Shizha&&proof.Source.BindingId=="consume-name"&&record.Source.OwnerSeat==proof.Source.OwnerSeat&&record.Source.SkillInstanceId==proof.Source.SkillInstanceId&&record.GameplayHash==proof.GameplayHash&&record.Name==AmbushCardName(proof.Use.Action.EffectiveKind)&&proof.GameplayHash==_contentRegistry.GetSkill(proof.Source.SkillId).Program?.GameplayHash&&started.Source==proof.Source&&started.GameplayHash==proof.GameplayHash&&started.Operation==SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha&&started.ParentId==proof.Use.WindowId&&record.Round==started.Round&&proof.Use.ParentId==parent&&MouCySameAction(proof.Use.Action,action)&&proof.Use.Response==(action.Type==CardActionType.Response)&&ProgramEventHistory<MouCyNameConsumedEvent>().Count(e=>e.FrameId==proof.FrameId&&e.RecordFrameId==record.FrameId&&e.ActionId==action.ActionId)==1&&ProgramEventHistory<MouCyUseInvalidatedEvent>().Count(e=>e.FrameId==proof.FrameId&&e.RecordFrameId==proof.RecordFrameId&&e.Source==proof.Source&&e.GameplayHash==proof.GameplayHash&&e.Use.WindowId==proof.Use.WindowId&&e.Use.ParentId==proof.Use.ParentId&&e.Use.Response==proof.Use.Response&&MouCySameAction(e.Use.Action,proof.Use.Action))==1;
 }
 private void ApplyMouCyInvalidation(ProgramSkillFrame f,MouCyUseInvalidatedEvent proof)
 {
  if(!MouChengYuAnniversary.Valid(f)||f.MouChengYuAnniversary is not{Stage:MouCyStage.Invalidate,Use:{}u}||MouCyUse(f.WindowContext!)is not{}live||live.WindowId!=u.WindowId||live.ParentId!=u.ParentId||!MouCySameAction(live.Action,u.Action)||!ValidMouCyInvalidation(proof,u.Action,u.ParentId))throw new InvalidOperationException("Shizha cannot invalidate a different committed native use.");
  if(!u.Response)UpdateLifecycleCardUse(u.ParentId,p=>p with{MouCyInvalidation=proof});
  else if(u.Action.EffectiveKind==CardKind.Dodge)UpdateLifecycleCardUse(u.ParentId,p=>p with{MouCyResponseInvalidations=(p.MouCyResponseInvalidations??[]).Append(proof).ToArray()});
  else if(u.Action.EffectiveKind==CardKind.Nullification&&_resolutionStack.OfType<NullificationWindowFrame>().SingleOrDefault(p=>p.Id==u.ParentId)is{NamedAmbushResponse:{}r}counter&&r.ActionId==u.Action.ActionId)ReplaceNullificationWindowFrame(counter with{MouCyResponseInvalidation=proof});
  else throw new InvalidOperationException("Shizha response lost its original typed native owner.");
  AddLog("SkillTriggered",$"{_players[f.OwnerSeat].Name} 发动【识诈】，令【{CardCatalog.Get(u.Action.EffectiveKind).DisplayName}】无效。",f.OwnerSeat,u.Action.ActorSeat);
 }
 private bool IsMouCyUseIneffective(CardUseFrame use)
 {if(use.MouCyInvalidation is not{}p)return false;if(use.Action is not{}a||!ValidMouCyInvalidation(p,a,use.Id))throw new InvalidOperationException("Shizha lost its exact consumed-name provenance.");return true;}
 private bool ContinueMouCyIneffectiveDodge(CardAttackHandle attack,CardActionContext action,ProgramCardContinuation continuation)
 {
  if(continuation!=ProgramCardContinuation.Dodge||LifecycleCardUse(attack.ResolutionId)?.MouCyResponseInvalidations is not{}proofs)return false;var matches=proofs.Where(p=>p.Use.Action.ActionId==action.ActionId).ToArray();if(matches.Length==0)return false;
  if(action.ParentActionId!=LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId||attack.TargetSeat!=action.ActorSeat||action.EffectiveKind!=CardKind.Dodge||matches.Any(p=>!ValidMouCyInvalidation(p,action,attack.ResolutionId)))throw new InvalidOperationException("Shizha's ineffective Dodge changed its original Slash or real response identity.");
  ClearRoundOnceCardlessDodgeResponse(attack);ClearTieredRoundDodgeResponse(attack);ClearChainedStateBasicDodgeResponse(attack);ClearDrawFundedDistinctBasicDodgeResponse(attack);SetCardUseStep(attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);if(!ApplyAttackDamage(attack))CompleteAttack(attack);return true;
 }
 private bool HasMouCyMatchingName(int actor,CardKind kind)=>HasMouChengYuAnniversary&&_players.Where(p=>p.IsAlive).Any(owner=>GetSkillBindingShard(owner).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted).Any(b=>b.SkillId==MouChengYuAnniversaryRuntime.Shizha&&b.Trigger.Id=="consume-name"&&MouChengYuAnniversary.Names(owner.Seat,b.SkillInstanceId).Any(n=>n.Name==AmbushCardName(kind))));
 private bool MouCyCounterspellInvalidated(NullificationWindowFrame pending,CardActionContext action)
 {if(pending.MouCyResponseInvalidation is not{}p)return false;if(pending.NamedAmbushResponse is not{}r||r.ActionId!=action.ActionId||!ValidMouCyInvalidation(p,action,pending.Id))throw new InvalidOperationException("Shizha's counterspell lost its typed deferred native response.");return true;}
 private bool IsMouCyZeroCounterspell(NullificationWindowFrame pending,CardActionContext action,NamedAmbushCounterspellResponse response)=>HasMouChengYuAnniversary&&response.CardId==0&&action.PhysicalCards.Count==0&&pending.TieredRoundResponseUse is{}r&&r.OwnerFrameId==pending.Id&&r.CardActionId==action.ActionId&&r.ActorSeat==action.ActorSeat&&r.EffectiveKind==CardKind.Nullification&&ProgramEventHistory<TieredRoundConversionUseIssuedEvent>().Count(e=>e.Receipt==r)==1;
 private bool TryMouCyEquipmentCommitted(long id,Card equipment)=>HasMouCyMatchingName(LifecycleCardUse(id)!.SourceSeat,equipment.Kind)&&TryBeginCommittedCardUse(id,ProgramCardContinuation.CommittedSimpleCard,simple:new(equipment.Id,SimpleCardUseEffect.EquipmentPlacement));
 private bool ContinueMouCyIneffectiveSimple(long id,ProgramSimpleCardContinuation continuation)
 {
  if(continuation.Effect!=SimpleCardUseEffect.EquipmentPlacement||LifecycleCardUse(id)is not{}u||!IsMouCyUseIneffective(u))return false;
  if(_resolutionStack.LastOrDefault()?.Id!=id||u.Action is not{Type:CardActionType.Use})throw new InvalidOperationException("Shizha lost its exact native simple Use return.");
  foreach(var c in GetCardUsePhysicalCards(id).Where(c=>_cardZones.GetLocation(c.Id)==CardLocation.Processing))MoveCard(c,CardLocation.Processing,CardLocation.DiscardPile,new("skill-program.mou:shizha.ineffective"));
  var card=u.CardId==0?new Card(0,u.CardKind,Suit.None,0):GetAdvancedCard(u.CardId);FinishCardUse(id,card,u.CardKind);return true;
 }
}
