namespace CardGame.Core;

public sealed partial class GameEngine
{
 private IReadOnlyList<CardKind> YanXiangDamageNames()=>Array.AsReadOnly(new[]{CardKind.Slash,CardKind.Duel,CardKind.BarbarianAssault,CardKind.ArrowBarrage,CardKind.FireAttack}
  .Concat(_cardZones.CreateDiagnostics().Select(c=>c.CardKind).Where(k=>k is CardKind.WumiaoFlood or CardKind.QizhengXiangsheng)).Distinct().ToArray());
 private static bool SameYanXiangPlan(AnniversaryYanXiangPlan a,AnniversaryYanXiangPlan b)=>(a.CardId,a.From,a.Kind,a.ActionKind,a.TargetCardId,a.RequiredKind,a.Adjusted)==(b.CardId,b.From,b.Kind,b.ActionKind,b.TargetCardId,b.RequiredKind,b.Adjusted)&&a.Targets.SequenceEqual(b.Targets);
 private IReadOnlyList<AnniversaryYanXiangPlan> YanXiangUsePlans(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;var actor=_players[r.Actor];var result=new List<AnniversaryYanXiangPlan>();
  if(!actor.IsAlive||_winner!=Winner.None||r.Name is not{} name)return [];
  foreach(var card in GetHand(actor).Concat(GetEquipment(actor)).OrderBy(c=>c.Id).Where(c=>!IsTurnHandCardRestricted(actor,c)&&!IsTurnPhysicalUseForbidden(actor.Seat,[c.Id])))
  {
   var from=_cardZones.GetLocation(card.Id);var suit=GetProgramEffectiveSuit(actor,card);var color=CapturePhysicalCardColor(actor.Seat,card);
   if(name==CardKind.Slash)
   {
    var actions=new List<LegalAction>();
    foreach(var kind in SlashKinds)
    {
     var targets=_players.Where(t=>CanUseVirtualSlashTarget(actor,t,kind,suit,color,card.Rank,[card.Id],conversionSource:r.Source)&&
      !IsDirectedCardTargetProhibited(actor.Seat,t.Seat,kind)&&!IsCardTargetProhibited(t,kind,suit,color)&&!HasBeneficiarySuitShield(actor.Seat,t.Seat,suit)).ToArray();
     foreach(var target in targets)actions.Add(new(LegalActionKind.Slash,card.Id,target.Seat,"邃识",kind,TargetSeats:[target.Seat]));
     AddFangtianHalberdSlashActions(actions,actor,card,targets,"邃识",kind,r.Source);
     AddProgramTargetCountSlashActions(actions,actor,card,targets,"邃识",kind,r.Source);
    }
    AddNextActualUseAdjustmentActions(actions,actor);
    result.AddRange(actions.Select(a=>new AnniversaryYanXiangPlan(card.Id,from,a.PlayedCardKind!.Value,a.Kind,Adjusted:a.ProgramActivationId==NextActualUseAdjustmentBinding){Targets=a.TargetSeats}));
   }
   else
    result.AddRange(BuildProgramOrdinaryTrickUseOptions(actor,name,suit,enforceUsePermission:true,beneficiaryShieldSuit:suit,actualEffectiveColor:color,hasActualColor:true,physicalCardIds:[card.Id],includeNextActualUseAdjustment:true,conversionSource:r.Source)
     .Select(o=>new AnniversaryYanXiangPlan(card.Id,from,o.EffectiveCardKind,o.ActionKind,o.TargetCardId,o.RequiredCardKind,o.NextActualUseAdjusted){Targets=GetImplicitSelfCardUseTargets(o.EffectiveCardKind,actor.Seat,o.TargetSeats)}));
  }
  return Array.AsReadOnly(result.ToArray());
 }
 private void IssueYanXiangUse(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;var p=r.Plan!;var actor=_players[r.Actor];var card=GetAdvancedCard(p.CardId);
  if(r.Stage!=AnniversaryYanXiangStage.IssuingUse||!ValidYanXiang(f)||!YanXiangUsePlans(f).Any(x=>SameYanXiangPlan(x,p)))throw new InvalidOperationException("Suishi lost the recipient's own currently legal native material and targets.");
  if(p.Adjusted)_selectedNextCardTargetSeats=p.Targets;
  var id=BeginCardUse(card,r.Actor,p.Targets,p.Kind,physicalCardIds:[card.Id],conversionSource:r.Source);
  if(LifecycleCardUse(id)?.AnniversaryYanXiangUse is null)throw new InvalidOperationException("Suishi must bind its original actual native Use before paying its material.");
  if(IsSlashCard(p.Kind))
  {CompleteNativeMaterialSlash(id,actor,[card],p.Kind,LifecycleCardUse(id)!.TargetSeats,r.Source,HasCardArmorBypass(actor,_players[p.Targets[0]],p.Kind));return;}
  MoveCard(card,p.From,CardLocation.Processing,CardMoveReasons.Use,disclosure:CardMovementDisclosure.FaceUp);
  BeginJizhiOrNullificationWindow(id,card,r.Actor,LifecycleCardUse(id)!.TargetSeats,p.ActionKind,p.TargetCardId,p.RequiredKind,p.Kind);
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,46801)]private void BindYanXiangUse(CardUseDeclaredEvent fact)
 {
  if(LifecycleCardUse(fact.ResolutionId) is not{Action:{Type:CardActionType.Use}a}use)return;
  var index=_resolutionStack.FindIndex(x=>x.Id==use.Id);
  if(index<1||_resolutionStack[index-1] is not ProgramSkillFrame{AnniversaryYanXiang:{Stage:AnniversaryYanXiangStage.IssuingUse,Plan:{}p}r}f)return;
  if(!ValidYanXiang(f)||use.CardId!=p.CardId||use.SourceSeat!=r.Actor||use.CardKind!=p.Kind||a.ActorSeat!=r.Actor||a.ProviderSeat!=r.Actor||
   a.RequesterSeat is not null||a.PhysicalCards is not[{CardId:var material,From:var from}]||material!=p.CardId||from!=p.From||!_cardZones.Contains(from,material)||
   !a.ConversionChain.SequenceEqual([r.Source])||!use.TargetSeats.SequenceEqual(p.Targets))throw new InvalidOperationException("Suishi changed its original recipient, HE material, declared damage name or native targets.");
  var origin=new AnniversaryYanXiangUseOrigin(f.Id,r.Source,r.Hash,r.Actor,p.CardId,p.From,p.Kind,use.Id,a.ActionId);
  ReplaceRuntimeFrame(f.Id,f with{AnniversaryYanXiang=r with{Stage=AnniversaryYanXiangStage.NativeUse,UseId=use.Id,ActionId=a.ActionId}});
  UpdateLifecycleCardUse(use.Id,u=>u with{AnniversaryYanXiangUse=origin});
  AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangUseIssuedEvent(f.Id,use.Id,a.ActionId,r.Actor,p.CardId,p.Kind));
 }
 private bool IsYanXiangNativeUse(CardUseFrame use)=>use.AnniversaryYanXiangUse is{}o&&use.Id==o.UseId&&use.CardId==o.CardId&&use.CardKind==o.Kind&&
  use.Action is{Type:CardActionType.Use}a&&a.ActionId==o.ActionId&&a.ProviderSeat==o.Actor&&ShownEntityUseActorMatches(use,o.Actor,o.Actor)&&a.EffectiveKind==o.Kind&&
  a.PhysicalCards is[{CardId:var id,From:var from}]&&id==o.CardId&&from==o.From&&use.PhysicalCardIds is[var physical]&&physical==id&&a.ConversionChain.SequenceEqual([o.Source])&&
  _resolutionStack.SingleOrDefault(x=>x.Id==o.FrameId) is ProgramSkillFrame{AnniversaryYanXiang:{Plan:{}p}r}f&&ValidYanXiang(f)&&r.Source==o.Source&&r.Hash==o.Hash&&r.Actor==o.Actor&&
  r.UseId==use.Id&&r.ActionId==a.ActionId&&p.CardId==o.CardId&&p.From==o.From&&p.Kind==o.Kind&&
  ProgramEventHistory<AnniversaryYanXiangUseIssuedEvent>().Count(e=>e==new AnniversaryYanXiangUseIssuedEvent(f.Id,use.Id,a.ActionId,o.Actor,o.CardId,o.Kind))==1;
 [NativeUseValidation(46800)]private void ValidateYanXiangNativeReturn(CardUseFrame use)
 {if(use.AnniversaryYanXiangUse is not null&&(use.Step!=ResolutionFrameStep.Completed||!IsYanXiangNativeUse(use)))throw new InvalidOperationException("Suishi must return its own completed original native Use.");}
 [NativeUseReturn(46800)]private void ReturnYanXiangUse(CardUseFrame use)
 {
  if(use.AnniversaryYanXiangUse is not{}o)return;
  var f=GetActiveProgramFrame(o.FrameId);var r=f.AnniversaryYanXiang!;
  if(!IsYanXiangNativeUse(use)||r.Returned||LifecycleCardUse(use.Id) is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id&&e.CardId==o.CardId)!=1||
   _cardMovements.Count(m=>m.CardId==o.CardId&&m.Sequence>r.Before&&m.From==o.From&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)!=1)throw new InvalidOperationException("Suishi cannot pay or retire another use, or complete the same use twice.");
  ReplaceRuntimeFrame(f.Id,f with{AnniversaryYanXiang=r with{Returned=true}});AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangUseReturnedEvent(f.Id,use.Id,o.ActionId));
  if(use.CardAttack is not{Active:true})SettleYanXiangUse(GetActiveProgramFrame(f.Id));
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,46800)]private bool ReturnYanXiangAttack()
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{AnniversaryYanXiang:{Stage:AnniversaryYanXiangStage.NativeUse,Returned:true}}f)return false;
  SettleYanXiangUse(f);return true;
 }
 private void SettleYanXiangUse(ProgramSkillFrame f)
 {
  var r=f.AnniversaryYanXiang!;
  if(!r.Returned||LifecycleCardUse(r.UseId) is not null)throw new InvalidOperationException("Suishi must await all native targets and its complete original use tail.");
  var batches=_pendingCardsMovedBatches.Where(b=>b.ParentFrameId==r.UseId&&b.AwaitingProgramFrameId is null&&b.OriginOwnerSeat==f.OwnerSeat&&b.OriginSkillId==f.SkillId&&b.OriginSkillInstanceId==f.SkillInstanceId).ToArray();
  foreach(var b in batches)AdvanceEventRulesAndQueueFact(new AnniversaryYanXiangNativeBatchEvent(f.Id,r.UseId,b.Id));
  ReplaceRuntimeTop(f=f with{AnniversaryYanXiang=r with{Stage=AnniversaryYanXiangStage.NativeTail,After=_movementSequence,NativeBatches=batches.Select(b=>b.Id).ToArray()},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  ContinueYanXiang(f.Id);
 }
 private bool IsAnniversaryYanXiangSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext b)=>f.AnniversaryYanXiang is{Stage:AnniversaryYanXiangStage.NativeTail,Returned:true}r&&ValidYanXiang(f)&&r.NativeBatches.Contains(b.Id)&&
  b.ParentFrameId==r.UseId&&b.AwaitingProgramFrameId is null&&b.OriginOwnerSeat==f.OwnerSeat&&b.OriginSkillId==f.SkillId&&b.OriginSkillInstanceId==f.SkillInstanceId&&b.Movements.Count>0&&
  b.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After)&&ProgramEventHistory<AnniversaryYanXiangNativeBatchEvent>().Count(e=>e==new AnniversaryYanXiangNativeBatchEvent(f.Id,r.UseId,b.Id))==1;
}
