namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouDongChengRuntime MouDongCheng=>GetRuntimeModule<MouDongChengRuntime>();
 private bool HasMouDongCheng=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouDongChengBaojia);
 [ProgramCapability]private sealed class MouDongChengFacts(GameEngine e):IMouDongChengFacts
 {
  public int Turn=>e._turnNumber;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;public bool Alive(int seat)=>e.IsValidPlayerSeat(seat)&&e._players[seat].IsAlive;public bool Human(int seat)=>e._players[seat].IsHuman;public string Name(int seat)=>e._players[seat].Name;public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
  public double TargetScore(int owner,int target)=>e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner),target,new(0,0,1,0,0,0,false,false));public int KeepValue(int owner,int card)=>GetKeepValue(e.GetAdvancedCard(card),e._players[owner]);
  public IReadOnlyList<int> Living=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());public IReadOnlyList<EquipmentSlot> Slots(int owner,bool abolished)=>Array.AsReadOnly(Enum.GetValues<EquipmentSlot>().Where(s=>(e._players[owner].EquipmentSlotCapacity(s)==0)==abolished).ToArray());
  public bool Parent(ProgramSkillFrame f)=>e.MouDcParent(f);public MouDcOccurrence? Before(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>e.MouDcBefore(c,w);public long ClaimUse(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>e.MouDcClaimUse(c,w)?.Id??0;
  public bool MayActivate(int owner)=>e._winner==Winner.None&&e._players[owner].IsAlive&&e._phase==TurnPhase.Play&&e._currentSeat==owner&&e._turnProgression.OwnerSeat==owner&&e._players[owner].MouDongChengDisabledTurn!=e._turnNumber&&Costs(owner).Count>0;
  public IReadOnlyList<MouDcEntity> Costs(int owner)=>Array.AsReadOnly(e.GetHand(e._players[owner]).Concat(e.GetEquipment(owner)).Where(c=>MouDcDamageKind(e._cardZones.GetLocation(c.Id).Zone==CardZoneKind.Hand?e.AdvancedEffectiveHandKind(e._players[owner],c):c.Kind)&&!e.IsSelfHandCategoryDiscardForbidden(owner,c,e._cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Discard)).Select(c=>new MouDcEntity(c.Id,e._cardZones.GetLocation(c.Id).Zone==CardZoneKind.Hand?e.AdvancedEffectiveHandKind(e._players[owner],c):c.Kind,e._cardZones.GetLocation(c.Id),c.DisplayName)).ToArray());
  public IReadOnlyList<int> Targets(ProgramSkillFrame f,CardKind kind)=>e.MouDcTargets(f,kind);
  public (long Frame,long Use,long Damage)? DyingCause(PlayerDyingEvent fact)
  {
   if(e._resolutionStack.LastOrDefault()is not DyingFrame{Continuation:DyingContinuationKind.Damage}dying||dying.Id!=fact.ResolutionId||e._resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==dying.ParentFrameId)is not{Amount:>0}damage||e.CurrentDamageAttempt is not{DamageWasApplied:true}attack||attack.ResolutionId!=damage.ParentFrameId||attack.TargetSeat!=fact.VictimSeat||e.LifecycleCardUse(attack.ResolutionId)is not{MouDongChengDouwei:{}n}u||e._resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is not{}parent||!e.MouDcUseRidesOn(parent,u))return null;return(n.FrameId,u.Id,damage.Id);
  }
 }
 private static bool MouDcDamageKind(CardKind kind)=>IsDamageCardKind(kind)||IsCompletedUndamagedDamageUse(kind)||kind==CardKind.Lightning;
 [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,37400,SkillProgramEffectOp.ResolveMouDongChengDouwei)]private bool MouDcCanActivate(CharacterState owner)=>new MouDongChengFacts(this).MayActivate(owner.Seat);
 private bool MouDcParent(ProgramSkillFrame f)
 {
  if(f.MouDongChengSettlement is not null)return new MouDcLightningFacts(this).SettlementParent(f);
  if(f.TriggerId is null)return f.SkillId==MouDongChengRuntime.Douwei&&f.ActivationId=="discard-damage-use"&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&_phase==TurnPhase.Play&&_currentSeat==f.OwnerSeat&&_turnProgression.OwnerSeat==f.OwnerSeat;
  if(f.WindowContext is not{}w||w.OwnerSeat!=f.OwnerSeat||f.ActivationId!=f.TriggerId)return false;var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(i<1||_resolutionStack[i-1].Id!=w.ParentFrameId)return false;
  if(f.TriggerId=="opening-link")return w.Window==SkillProgramTriggerWindow.GameStarting&&_resolutionStack[i-1]is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.GameStarting}life&&life.CandidateIndex>=0&&life.CandidateIndex<life.Candidates.Count&&MountObserverCandidateMatches(f,life.Candidates[life.CandidateIndex]);
  var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0);
  return f.TriggerId=="first-card-damage"?MouDcBefore(c,w)is not null||f.MouDongCheng is{Occurrence:{}o}&&_resolutionStack[i-1]is BeforeDamageProgramWindowFrame{MouDongChengOccurrence:{}original}before&&original==o&&original.Target==w.TargetSeat&&before.CandidateIndex>=0&&before.CandidateIndex<before.Candidates.Count&&MountObserverCandidateMatches(f,before.Candidates[before.CandidateIndex].Candidate)&&MouDcHasPaidClaim(f,o,before.Id):f.TriggerId=="claim-finished"&&MouDcClaimUse(c,w,settled:f.MouDongCheng is not null)is not null;
 }
 private MouDcOccurrence? ObserveMouDcCardDamage()
 {
  if(!HasMouDongCheng||CurrentDamageAttempt is not CardAttackHandle{IsProgramSkillDamage:false,IsProgramJudgmentDamage:false}a||a.EffectiveCardKind is null)return null;
  var u=LifecycleCardUse(a.ResolutionId);if(u?.Action is not{Type:CardActionType.Use}&& !a.IsDelayedJudgmentDamage)return null;
  return MouDongCheng.ObserveDamage(++_resolutionSequence,a.ResolutionId,u?.Id??0,a.Card?.Id??0,a.TargetSeat);
 }
 private MouDcOccurrence? MouDcBefore(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
 {
  if(c.SkillId!=MouDongChengRuntime.Baojia||c.BindingId!="first-card-damage"||w.Window!=SkillProgramTriggerWindow.BeforeDamageApplied||w.OwnerSeat!=c.OwnerSeat||_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(x=>x.Id==w.ParentFrameId)is not{Prevented:false,Amount:>0,MouDongChengOccurrence:{First:true}o}b||b.TargetSeat!=o.Target||w.TargetSeat!=o.Target||o.Turn!=_turnNumber||!MouDcDamageParent(o)||!_players[c.OwnerSeat].IsAlive||_players[c.OwnerSeat].MouDongChengLinks?.Any(l=>l.Source.SkillId==c.SkillId&&l.Source.SkillInstanceId==c.SkillInstanceId&&(o.Target==c.OwnerSeat||o.Target==l.Other))!=true||!Enum.GetValues<EquipmentSlot>().Any(s=>_players[c.OwnerSeat].EquipmentSlotCapacity(s)>0)||b.CandidateIndex<0||b.CandidateIndex>=b.Candidates.Count)return null;
  var actual=b.Candidates[b.CandidateIndex].Candidate;return actual.OwnerSeat==c.OwnerSeat&&actual.SkillId==c.SkillId&&actual.BindingId==c.BindingId&&actual.SkillInstanceId==c.SkillInstanceId&&actual.GameplayHash==c.GameplayHash?o:null;
 }
 private CardUseFrame? MouDcClaimUse(ProgramTriggerCandidate c,ProgramSkillWindowContext w,bool settled=false)
 {
  if(c.SkillId!=MouDongChengRuntime.Baojia||c.BindingId!="claim-finished"||w.Window!=SkillProgramTriggerWindow.CardUseCompleted||w.OwnerSeat!=c.OwnerSeat||w.CardUse is not{}info||LifecycleCardUse(info.ParentCardUseFrameId)is not{Step:ResolutionFrameStep.Completed,Action:{}a,MouDongChengClaims:{}claims}u||a.ActionId!=info.CardActionId||!claims.Any(p=>p.Source.OwnerSeat==c.OwnerSeat&&p.Source.SkillInstanceId==c.SkillInstanceId&&p.GameplayHash==c.GameplayHash&&(settled||!p.Settled))||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(x=>x.Id==w.ParentFrameId)is not{}window||window.ParentFrameId!=u.Id||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count)return null;
  var active=window.Candidates[window.CandidateIndex];return active.OwnerSeat==c.OwnerSeat&&active.SkillId==c.SkillId&&active.TriggerId==c.BindingId&&active.SkillInstanceId==c.SkillInstanceId&&active.GameplayHash==c.GameplayHash&&CreateCardActionProgramContext(window,active)==w&&ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==u.Id)==1?u:null;
 }
 [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate,37400)]private bool MouDcIssuedClaim(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>MouDcClaimUse(c,w)is not null;
 [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger,37400)]private void MouDcClaimCandidates(CardActionContext a,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
 {
  if(window!=SkillProgramTriggerWindow.CardUseCompleted||_resolutionStack.LastOrDefault()is not CardUseFrame{MouDongChengClaims:{}claims}u||u.Action?.ActionId!=a.ActionId)return;
  foreach(var p in claims.Where(p=>!p.Settled&&_players[p.Source.OwnerSeat].IsAlive).DistinctBy(p=>(p.Source.OwnerSeat,p.Source.SkillInstanceId)))
  {if(result.Any(c=>c.OwnerSeat==p.Source.OwnerSeat&&c.SkillId==p.Source.SkillId&&c.SkillInstanceId==p.Source.SkillInstanceId&&c.TriggerId=="claim-finished"))continue;result.Add(new(p.Source.OwnerSeat,p.Target,p.Source.SkillId,"claim-finished",p.GameplayHash,p.Source.SkillInstanceId,0,CreateCardActionProgramContext(a,window,0,p.Source.OwnerSeat,p.Target,CaptureProgramTriggerFacts(_players[p.Source.OwnerSeat],a))));}
 }
 [ProgramCapability]private sealed class MouDongChengNative(GameEngine e):IMouDongChengNative
 {
  public void Finish(ProgramSkillFrame f){e.FinishProgramSkill(f,true);if(f.MouDongChengSettlement is{}s)e.MouDongCheng.DelayedSettlementReturned(s);}public void Publish()=>e.AdvanceRulesAndPublishState();public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
  public void Link(ProgramSkillFrame f,int other)
  {if(!e.MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.Opening}r||other==f.OwnerSeat||!e._players[other].IsAlive)throw new InvalidOperationException("Baojia requires one actual other living opening role.");var owner=e._players[f.OwnerSeat];owner.MouDongChengLinks=Array.AsReadOnly((owner.MouDongChengLinks??[]).Where(l=>l.Source.SkillInstanceId!=f.SkillInstanceId).Append(new(r.Source,other)).ToArray());e.AdvanceEventRulesAndQueueFact(new MouDcLinkedEvent(f.Id,r.Source,other));}
  public void Protect(ProgramSkillFrame f)=>e.MouDcProtect(f);
  public void Claim(ProgramSkillFrame f)=>e.MouDcClaim(f);
  public void Use(ProgramSkillFrame f)=>e.IssueMouDcUse(f);
  public void Discard(ProgramSkillFrame f)
  {
   if(!e.MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.CostChildren}r||!new MouDongChengFacts(e).Costs(f.OwnerSeat).Any(c=>c.Id==r.CardId&&c.Kind==r.Kind))throw new InvalidOperationException("Douwei requires its exact real discardable damage-card cost.");
   var from=e._cardZones.GetLocation(r.CardId);e.MoveProgramLogicalDiscard(f,f.OwnerSeat,[r.CardId],new("skill-program.mou:douwei.cost"),(_,moves)=>{if(moves is not[var m]||m.CardId!=r.CardId||m.From!=from||m.Sequence<=r.Before||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame)throw new InvalidOperationException("Douwei's real logical discard invoice changed.");var own=e.GetActiveProgramFrame(f.Id);e.ReplaceRuntimeFrame(f.Id,own with{MouDongCheng=own.MouDongCheng! with{Actual=1,After=m.Sequence}});e.AdvanceEventRulesAndQueueFact(new MouDcMovementPaidEvent(f.Id,r.CardId,1,r.Before,m.Sequence));});
  }
  public void Recover(ProgramSkillFrame f)
  {if(!e.MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.RecoveryChildren,Returned:true,Dying.Count:1}r||(f.MouDongChengSettlement is{Reward:true}s?!new MouDcLightningFacts(e).SettlementParent(f)||s.DyingId!=r.Dying[0]:r.Native is not{}n||e.ProgramEventHistory<MouDcDyingEvent>().Count(x=>x.FrameId==f.Id&&x.UseId==n.UseId)!=1))throw new InvalidOperationException("Douwei's once-per-whole-use reward needs its actual first causal dying return.");new ProgramSkillHost(e).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);}
  public void Restore(ProgramSkillFrame f)
  {if(!e.MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.RestoreChildren,Slot:{}slot}||!e.RestoreEquipmentSlot(e._players[f.OwnerSeat],slot))throw new InvalidOperationException("Douwei requires one actual abolished equipment slot.");e.AdvanceEventRulesAndQueueFact(new MouDcRestoredEvent(f.Id,slot));}
  public void Disable(ProgramSkillFrame f)
  {if(!e.MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.NativeUse,Dying.Count:1})throw new InvalidOperationException("Only the first real causal dying may disable Douwei.");e._players[f.OwnerSeat].MouDongChengDisabledTurn=e._turnNumber;}
 }
 private void MouDcProtect(ProgramSkillFrame f)
 {
  if(!MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.SlotChildren,Slot:{}slot,Occurrence:{}o}r||_players[f.OwnerSeat].EquipmentSlotCapacity(slot)<=0||_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(x=>x.Id==r.ParentId)is not{Prevented:false}b||b.MouDongChengOccurrence!=o||!MouDcDamageParent(o)||MouDcHasPaidClaim(f,o,b.Id))throw new InvalidOperationException("Baojia must pay a real still-enabled slot in its one exact first card-damage window.");
  var claim=new MouDcClaim(f.Id,r.Source,r.GameplayHash,r.Turn,b.Id,o.AttackId,o.UseId,LifecycleCardUse(o.UseId)?.Action?.ActionId??0,o.Target,slot);
  if(o.UseId>0)UpdateLifecycleCardUse(o.UseId,u=>u with{MouDongChengClaims=(u.MouDongChengClaims??[]).Append(claim).ToArray()});
  else {var judgment=MouDcDelayedDamage(o)!;ReplaceRuntimeFrame(judgment.Id,judgment with{MouDongChengClaims=(judgment.MouDongChengClaims??[]).Append(claim).ToArray()});}
  SetEquipmentSlotCapacity(_players[f.OwnerSeat],slot,0);var own=GetActiveProgramFrame(f.Id);ReplaceRuntimeFrame(f.Id,own with{MouDongCheng=own.MouDongCheng! with{After=_movementSequence}});PreventProgramCurrentDamage(GetActiveProgramFrame(f.Id));AdvanceEventRulesAndQueueFact(new MouDcSlotPaidEvent(f.Id,b.Id,o.Target,slot,o.UseId));
 }
 private void MouDcClaim(ProgramSkillFrame f)
 {
  if(f.MouDongChengSettlement is not null){ClaimMouDcDelayedCard(f);return;}
  if(!MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.ClaimChildren}r||LifecycleCardUse(r.UseId)is not{Step:ResolutionFrameStep.Completed,Action:{}a,MouDongChengClaims:{}claims}use)throw new InvalidOperationException("Baojia may obtain only its original fully resolved card.");
  var pending=claims.Where(p=>p.Source.OwnerSeat==f.OwnerSeat&&p.Source.SkillInstanceId==f.SkillInstanceId&&!p.Settled).ToArray();if(pending.Length==0)throw new InvalidOperationException("Baojia cannot repeat an already settled whole-use claim.");var ids=a.PhysicalCards.Select(c=>c.CardId).Where(id=>_cardZones.GetLocation(id).Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile&&!IsClaimedUseCardEntity(use.Id,id)).ToArray();
  UpdateLifecycleCardUse(use.Id,u=>u with{MouDongChengClaims=claims.Select(p=>pending.Contains(p)?p with{Settled=true,Before=r.Before,After=r.Before,Cards=ids}:p).ToArray()});
  if(ids.Length>0)MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new("skill-program.mou:baojia.claim"),(_,moves)=>{if(moves.Count!=ids.Length||moves.Any(m=>!ids.Contains(m.CardId)||m.Sequence<=r.Before||m.To!=CardLocation.Hand(f.OwnerSeat)&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Baojia's completed original material claim lost its genuine movement.");var after=moves.Max(m=>m.Sequence);var parent=GetActiveProgramFrame(f.Id);ReplaceRuntimeFrame(f.Id,parent with{MouDongCheng=parent.MouDongCheng! with{After=after,Actual=moves.Count(m=>m.To==CardLocation.Hand(f.OwnerSeat))}});UpdateLifecycleCardUse(use.Id,u=>u with{MouDongChengClaims=u.MouDongChengClaims!.Select(p=>pending.Any(old=>old.ProtectionFrameId==p.ProtectionFrameId)?p with{After=after}:p).ToArray()});});
  AdvanceEventRulesAndQueueFact(new MouDcClaimSettledEvent(f.Id,use.Id,ids.Length));
 }
 private bool IsMouDcClaimedEntity(long useId,int card)=>LifecycleCardUse(useId)is{Action:{}a,MouDongChengClaims:{}claims}u&&claims.Any(p=>p.Settled&&p.UseId==u.Id&&p.ActionId==a.ActionId&&p.Cards.Contains(card)&&p.After>p.Before&&_cardMovements.Count(m=>m.CardId==card&&m.Sequence>p.Before&&m.Sequence<=p.After&&m.To==CardLocation.Hand(p.Source.OwnerSeat)&&m.Reason.Value=="skill-program.mou:baojia.claim")==1);
}
