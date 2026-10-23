namespace CardGame.Core;

public sealed partial class GameEngine
{
 private bool HasYanXiangCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveAnniversaryYanXiangYuzheng);
 private bool YanXiangLeaseProof(AnniversaryYanXiangLeaseEvent lease)=>lease.Source.SkillId==AnniversaryYanXiangComposition.Yuzheng&&lease.Source.BindingId=="admonish"&&lease.Amount>=0&&lease.Option is 1 or 2&&
  IsValidPlayerSeat(lease.Actor)&&_contentRegistry.GetSkill(lease.Source.SkillId).Program?.GameplayHash==lease.Hash&&lease.UseCutoff>=0&&lease.UseCutoff<=ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Count()&&lease.ActionCutoff>=0&&lease.ActionCutoff<=_cardActionSequence&&
  ProgramEventHistory<AnniversaryYanXiangOptionChosenEvent>().Count(e=>e.FrameId==lease.Id&&e.Source==lease.Source&&e.Hash==lease.Hash&&e.Actor==lease.Actor&&e.Option==lease.Option)==1;
 private AnniversaryYanXiangLeaseEvent[] ActiveYanXiangLeases()=>!HasYanXiangCapability?[]:ProgramEventHistory<AnniversaryYanXiangLeaseEvent>().Where(e=>e.Round==_roundNumber&&YanXiangLeaseProof(e)).ToArray();
 private AnniversaryYanXiangLeaseEvent[] DueYanXiangRewards(int owner,string instance,CardActionContext? action)=>action is null||!_players[action.ActorSeat].IsAlive?[]:
  ActiveYanXiangLeases().Where(e=>e.Option==1&&e.Actor==action.ActorSeat&&e.Source.OwnerSeat==owner&&e.Source.SkillInstanceId==instance&&action.ActionId>e.ActionCutoff&&
   ProgramEventHistory<AnniversaryYanXiangRewardRequestedEvent>().Count(r=>r.LeaseId==e.Id)<e.Amount&&!ProgramEventHistory<AnniversaryYanXiangRewardRequestedEvent>().Any(r=>r.LeaseId==e.Id&&r.ActionId==action.ActionId)).OrderBy(e=>e.Id).ToArray();
 [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger,46800)]
 private void CollectYanXiangRewards(CardActionContext action,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
 {
  if(!HasYanXiangCapability||window is not(SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted))return;
  var binding=window==SkillProgramTriggerWindow.CardUseCompleted?"issued-use-reward":"issued-response-reward";
  foreach(var source in ActiveYanXiangLeases().Where(e=>e.Option==1&&e.Actor==action.ActorSeat).Select(e=>e.Source).DistinctBy(s=>(s.OwnerSeat,s.SkillInstanceId)))
  {
   var due=DueYanXiangRewards(source.OwnerSeat,source.SkillInstanceId,action);if(due.Length==0)continue;
   if(!result.Any(c=>c.OwnerSeat==source.OwnerSeat&&c.SkillId==AnniversaryYanXiangComposition.Yuzheng&&c.TriggerId==binding&&c.SkillInstanceId==source.SkillInstanceId))
    result.Add(new(source.OwnerSeat,action.ActorSeat,AnniversaryYanXiangComposition.Yuzheng,binding,due[0].Hash,source.SkillInstanceId));
  }
 }
 [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate,46800)]
 private bool IsIssuedYanXiangReward(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>c.SkillId==AnniversaryYanXiangComposition.Yuzheng&&c.BindingId is"issued-use-reward" or"issued-response-reward"&&
  DueYanXiangRewards(c.OwnerSeat,c.SkillInstanceId,YanXiangWindowAction(w)).Any(e=>e.Hash==c.GameplayHash);
 private IEnumerable<RuleQueryContribution> AnniversaryYanXiangHandLimitContributions(CharacterState player)
 {
  foreach(var lease in ActiveYanXiangLeases().Where(e=>e.Option==2&&e.Actor==player.Seat&&e.Amount>0))
   yield return new FiniteRuleQueryContribution($"issued:{lease.Id}:anniversary:yuzheng:round-hand-limit",SkillRuleOperation.Add,lease.Amount);
 }
 private (CurrentTurnCardUseKindsRecordedEvent Fact,int Actor)[] YanXiangUsesAfter(AnniversaryYanXiangLeaseEvent lease)
 {
  var replacements=ProgramEventHistory<ProgramCardUseActorReplacedEvent>().GroupBy(e=>e.CardUseFrameId).ToDictionary(g=>g.Key,g=>g.ToArray());
  return ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Skip(lease.UseCutoff).Select(f=>
  {
   var actor=f.ActorSeat;
   if(f.RoundActionType==CardActionType.Use&&replacements.TryGetValue(f.OriginFrameId,out var changes))foreach(var change in changes)
   {if(change.PreviousActorSeat!=actor||change.ProviderSeat!=f.RoundProviderSeat)throw new InvalidOperationException("Yuzheng's use quota lost a native actor-replacement chain.");actor=change.ActorSeat;}
   return(Fact:f,Actor:actor);
  }).Where(x=>x.Actor==lease.Actor).ToArray();
 }
 private bool YanXiangQuotaForbidden(int actor,long? existingUse=null)
 {
  foreach(var lease in ActiveYanXiangLeases().Where(e=>e.Option==2&&e.Actor==actor))
  {
   var uses=YanXiangUsesAfter(lease);var current=existingUse is{} id?LifecycleCardUse(id):null;
   var admitted=current is{Action.Type:CardActionType.Use}&&current.SourceSeat==actor&&uses.Any(x=>x.Fact.OriginFrameId==current.Id&&x.Fact.CardActionId==current.Action.ActionId);
   if(admitted?uses.Length>lease.Amount:uses.Length>=lease.Amount)return true;
  }
  return false;
 }
 private bool YanXiangIsTrueResponseUse(int actor,CardKind kind,bool dying=false)=>dying||kind==CardKind.Nullification||IsProgramResponseCardUse(_players[actor],kind);
 [ProgramCardLegality(ProgramCardLegalityStage.BeforeIntrinsicUseBan,46800)]
 private bool YanXiangUseQuota(ProgramCardUseQuery q)=>(q.ActionType==CardActionType.Use||YanXiangIsTrueResponseUse(q.ActorSeat,q.Kind))&&YanXiangQuotaForbidden(q.ActorSeat,q.ExistingUseFrameId);
 private int YanXiangOwnReturns(int actor)=>ProgramEventHistory<ActualTurnCursorReturnedEvent>().Count(e=>e.OwnerSeat==actor);
 private bool YanXiangColorBanned(int actor,bool? color)=>color is{} red&&ProgramEventHistory<AnniversaryYanXiangColorBannedEvent>().Any(e=>e.Victim==actor&&e.IsRed==red&&YanXiangOwnReturns(actor)==e.OwnReturns&&
  ProgramEventHistory<AnniversaryYanXiangUseIssuedEvent>().Any(i=>i.FrameId==e.FrameId&&i.UseId==e.UseId&&i.ActionId==e.ActionId));
 private bool? YanXiangUseColor(int actor,CardKind kind,IEnumerable<int> ids,IReadOnlyList<CardConversionSource>? sources=null)
 {
  var cards=ids.Where(id=>id>0).Distinct().Select(GetAdvancedCard).ToArray();if(cards.Length==0)return null;
  var chain=sources??[];var source=chain.FirstOrDefault();
  if(source is not null&&(IsXieweiSource(source)||IsWushuangJinzhiSource(source)))return null;
  var appearance=ApplyProgramUseAppearance(_players[actor],cards[0],source);var materialIds=cards.Select(c=>c.Id).ToArray();
  var suit=WuJingLiubingUsedSuit(actor,appearance,kind,materialIds,chain,CaptureUsedCardSuit(actor,materialIds,appearance));
  return CaptureActionColor(cards.Select(c=>new CardActionCost(c.Id,c.Kind,_cardZones.GetLocation(c.Id),CapturePhysicalCardColor(actor,c))).ToArray(),suit);
 }
 [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan,46800)]private bool YanXiangNativeResponseBan(ProgramNativeResponseQuery q)=>YanXiangIsTrueResponseUse(q.Provider.Seat,q.Kind,q.DyingUse)&&
  (YanXiangQuotaForbidden(q.Provider.Seat)||YanXiangColorBanned(q.Provider.Seat,YanXiangUseColor(q.Provider.Seat,q.Kind,q.CardIds,q.Source is{}s?[s]:[])));
 [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan,46800)]private bool YanXiangViewAsBan(ProgramViewAsLegalityQuery q)=>(!q.ForResponse||q.RequestedUseTarget is not null||YanXiangIsTrueResponseUse(q.Owner.Seat,q.Kind,q.DyingUse))&&
  (YanXiangQuotaForbidden(q.Owner.Seat)||YanXiangColorBanned(q.Owner.Seat,YanXiangUseColor(q.Owner.Seat,q.Kind,q.CardIds,[q.Source])));
 [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind,46800)]private bool YanXiangProjectedBan(ProgramLegalActionQuery q)=>q.Action.Kind is not(LegalActionKind.Recast or LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral)&&q.EffectiveKind is not null&&
  (YanXiangQuotaForbidden(q.Actor.Seat)||YanXiangColorBanned(q.Actor.Seat,YanXiangUseColor(q.Actor.Seat,q.EffectiveKind.Value,q.Action.CardId is>0 and var id?[id]:[],(q.Action.ConversionSource is{}s?new[]{s}:Array.Empty<CardConversionSource>()).Concat(q.Action.AdditionalConversionSources??[]).ToArray())));
 [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation,46800)]private void ValidateYanXiangSubmittedUse(ProgramSubmittedUseQuery q)
 {if(YanXiangQuotaForbidden(q.ActorSeat)||YanXiangColorBanned(q.ActorSeat,YanXiangUseColor(q.ActorSeat,q.Kind,q.CardIds,q.ConversionChain)))throw new InvalidOperationException("Yuzheng/Suishi prohibits this new true card use.");}
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,46800)]private void ValidateYanXiangNativeUse(IGameEvent fact)
 {
  if(!HasYanXiangCapability||fact is not CardUseDeclaredEvent d||LifecycleCardUse(d.ResolutionId) is not{Action:{} action}use)return;
  if(YanXiangQuotaForbidden(action.ActorSeat,use.Id)||YanXiangColorBanned(action.ActorSeat,action.EffectiveIsRed))throw new InvalidOperationException("Yan Xiang rejects a new native true Use beyond its round quota or effective-color restriction.");
 }
 [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying,46800)]private bool CaptureYanXiangColorDamage(IDamageAttempt attack,long damageId)
 {
  if(attack is not CardAttackHandle a||!a.DamageWasApplied||LifecycleCardUse(a.ResolutionId) is not{AnniversaryYanXiangUse:{} origin,Action:{} action}use||!IsYanXiangNativeUse(use)||
   _resolutionStack.SingleOrDefault(f=>f.Id==damageId) is not DamageFrame{Amount:>0}damage||action.EffectiveIsRed is not{} color)return false;
  if(!ProgramEventHistory<AnniversaryYanXiangColorBannedEvent>().Any(e=>e.UseId==use.Id&&e.DamageId==damageId))AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangColorBannedEvent(origin.FrameId,use.Id,action.ActionId,damageId,damage.TargetSeat,color,YanXiangOwnReturns(damage.TargetSeat)));
  return false;
 }
}
