using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record XuRongActualDamageHpProof(long DamageFrameId,long DamageAttemptResolutionId,int TargetSeat,int HpBefore,int ActualAmount,int UnclampedHpAfter);
public sealed partial record DamageFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
 public XuRongActualDamageHpProof? XuRongActualHp {get;init;}
}
public sealed partial class GameEngine
{
 private void CaptureXuRongActualDamageHp(long damageFrameId,IDamageAttempt attack,int hpBefore,int actualAmount)
 {
  if(!_players.Any(p=>p.IsAlive&&GetSkillBindingShard(p).GetInstanceTriggers(SkillProgramTriggerWindow.DyingEntering).Any(b=>b.Trigger.Effects is [{Op:SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause}])))return;
  if(_resolutionStack.LastOrDefault()is not DamageFrame d||d.Id!=damageFrameId||d.ParentFrameId!=attack.ResolutionId||d.TargetSeat!=attack.TargetSeat||d.Amount!=actualAmount||actualAmount<=0||_players[d.TargetSeat].Hp!=hpBefore||!_players[d.TargetSeat].HasHpValue||d.XuRongActualHp is not null)throw new InvalidOperationException("Xu Rong requires the exact native pre-clamp damage payment.");
  if(d.ArmorPayment is {} armor&&(armor.HpBefore!=hpBefore||!IsValidDamageArmorPayment(d,requireApplied:false)))throw new InvalidOperationException("Xu Rong lost the exact Armor-adjusted native damage payment.");
  ReplaceRuntimeFrame(d.Id,d with{XuRongActualHp=new(d.Id,attack.ResolutionId,d.TargetSeat,hpBefore,actualAmount,checked(hpBefore-(d.ArmorPayment?.HpLossAmount??actualAmount)))});
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 460)]
 private void ObserveXuRongDyingQualification(IGameEvent payload)
 {
  if(payload is not PlayerDyingEvent fact||ActiveDying is not {} dying||dying.Id!=fact.ResolutionId||dying.VictimSeat!=fact.VictimSeat||dying.XuRongProof is not null)return;
  if(!_players.Any(p=>p.IsAlive&&GetSkillBindingShard(p).GetInstanceTriggers(SkillProgramTriggerWindow.DyingEntering).Any(b=>b.Trigger.Effects is [{Op:SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause}])))return;
  var victim=_players[dying.VictimSeat];if(!victim.HasHpValue||victim.Hp>0)return;
  var attack=GetDyingAttack(dying);var originalHp=victim.Hp;long? actionId=null;var ids=Array.Empty<int>();
  if(dying.Continuation==DyingContinuationKind.Damage&&attack is not null&&attack.TargetSeat==victim.Seat&&attack.DamageWasApplied&&_resolutionStack.SingleOrDefault(f=>f.Id==dying.ParentFrameId)is DamageFrame damage&&damage.ParentFrameId==attack.ResolutionId&&damage.TargetSeat==victim.Seat)
  {
   if(damage.XuRongActualHp is not {} paid||paid.DamageFrameId!=damage.Id||paid.DamageAttemptResolutionId!=attack.ResolutionId||paid.TargetSeat!=victim.Seat||paid.ActualAmount!=damage.Amount||paid.ActualAmount!=attack.DamageAmount||paid.UnclampedHpAfter!=paid.HpBefore-(damage.ArmorPayment?.HpLossAmount??paid.ActualAmount)||damage.ArmorPayment is {} armor&&(armor.HpBefore!=paid.HpBefore||!IsIssuedDamageArmorPayment(damage))||(CompleteProgramEventHistory().OfType<DamageAppliedEvent>().LastOrDefault() is not {} applied||applied.TargetSeat!=victim.Seat||applied.Amount!=paid.ActualAmount||applied.RemainingHp!=Math.Max(0,paid.UnclampedHpAfter)||applied.SourceLess!=attack.IsSourceLess||applied.SourceSeat!=attack.SourceSeat||applied.Nature!=damage.Nature||applied.ArmorPayment!=damage.ArmorPayment))throw new InvalidOperationException("Xu Rong dying requirement lost its exact native applied damage proof.");
   originalHp=paid.UnclampedHpAfter;
   var native=LifecycleCardUse(attack.ResolutionId);
   if(native is {Action:{Type:CardActionType.Use}action}&&native.CardKind==attack.EffectiveCardKind&&(native.PhysicalCardIds??(native.CardId>0?new[]{native.CardId}:Array.Empty<int>())) is {} physical&&physical.SequenceEqual(action.PhysicalCards.Select(c=>c.CardId))&&CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e=>e.Action.ActionId==action.ActionId&&e.Action.PhysicalCards.Select(c=>c.CardId).SequenceEqual(physical)))
   {ids=physical.ToArray();actionId=action.ActionId;}
   else if(attack.IsDelayedJudgmentDamage&&_resolutionStack.SingleOrDefault(f=>f.Id==attack.ResolutionId)is JudgmentFrame{Continuation:JudgmentContinuationKind.Lightning,DelayedCardId:{}held,Succeeded:true}judgment&&attack.EffectiveCardKind==CardKind.Lightning&&CompleteProgramEventHistory().OfType<LightningResolvedEvent>().Any(e=>e.ResolutionId==judgment.Id&&e.CardId==held&&e.Hit))ids=[held];
  }
  var entities=ids.Distinct().Where(id=>id>0).Select(id=>new XuRongCausalEntity(id,_cardZones.GetLocation(id))).Where(e=>e.OriginalLocation.Zone is CardZoneKind.Processing or CardZoneKind.Judgment or CardZoneKind.DiscardPile).ToArray();
  var proof=new XuRongDyingProof(dying.Id,dying.ParentFrameId,dying.VictimSeat,originalHp,checked(1-originalHp),attack?.ResolutionId,actionId,attack is null||attack.IsSourceLess||attack.IsDelayedJudgmentDamage?null:attack.SourceSeat,attack is null||attack.IsSourceLess||attack.IsDelayedJudgmentDamage,Array.AsReadOnly(entities));
  UpdateDyingFrame(dying.Id,current=>current with{XuRongProof=proof});
 }
 private ProgramSkillWindowContext CreateXuRongDyingContext(ProgramSkillWindowContext context)=>context.Window==SkillProgramTriggerWindow.DyingEntering&&ActiveDying?.XuRongProof is {} proof?context with{XuRongDying=proof}:context;
 private bool HasXuRongDyingQualification(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)=>GetProgramTrigger(candidate).Effects is [{Op:SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause}]&&context.Window==SkillProgramTriggerWindow.DyingEntering&&context.XuRongDying is {} proof&&proof.VictimSeat!=candidate.OwnerSeat&&context.TargetSeat==proof.VictimSeat&&proof.HpAtEntry<=0&&proof.RescueCardsRequired==1-proof.HpAtEntry&&_resolutionStack.OfType<DyingFrame>().Any(d=>d.Id==proof.DyingFrameId&&d.XuRongProof==proof);
 private bool ExactXuRongParent(ProgramSkillFrame f,XuRongPending r)
 {
  if(f.Id<=0||f.GameplayHash!=r.GameplayHash||f.InstructionIndex!=r.InstructionIndex||f.InstructionIndex!=1||f.WindowContext?.ParentFrameId!=r.ParentFrameId||XuRongSource(f)!=r.Source||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash)return false;
  var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(i<1)return false;
  if(r.DyingFrameId is {} dying)return f.WindowContext?.Window==SkillProgramTriggerWindow.DyingEntering&&f.WindowContext.XuRongDying is {} proof&&proof.DyingFrameId==dying&&proof.VictimSeat==r.SubjectSeat&&_resolutionStack.OfType<DyingFrame>().SingleOrDefault(d=>d.Id==dying)?.XuRongProof==proof&&_resolutionStack[i-1]is ProgramLifecycleTriggerWindowFrame w&&w.ResumeDyingFrameId==dying&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]);
  return f.WindowContext?.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&_resolutionStack[i-1]is PlayPhaseStartingBoundaryFrame phase&&phase.Id==r.ParentFrameId&&phase.OwnerSeat==r.SubjectSeat&&phase.ItemIndex>=0&&phase.ItemIndex<phase.Items.Count&&phase.Items[phase.ItemIndex].Candidate is {} c&&MountObserverCandidateMatches(f,c);
 }
 private bool ValidXuRong(ProgramSkillFrame f)
 {
  if(f.XuRong is not {} r||!ExactXuRongParent(f,r))return false;
  if(r.DyingFrameId is {} dying)
  {
   if(GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause||f.WindowContext?.XuRongDying is not {} proof||proof.DyingFrameId!=dying||proof.RescueCardsRequired!=1-proof.HpAtEntry||proof.HpAtEntry>0||CompleteProgramEventHistory().OfType<XionghuoPoolChangedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.Kind=="dying")!=1)return false;
  }
  else if(GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.SettleXionghuoMarkedPlay||r.Branch is <0 or >2||r.RandomBefore==r.RandomAfter||r.Grants.Count==0||r.Grants.Any(g=>g.TargetSeat!=r.SubjectSeat||g.Source.OwnerSeat!=f.OwnerSeat||g.Count<1||CompleteProgramEventHistory().OfType<XionghuoMarkTransferredEvent>().Count(e=>e.Grant==g)!=1)||CompleteProgramEventHistory().OfType<XionghuoPlayDebtPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.PlayWindowId==r.ParentFrameId&&e.SubjectSeat==r.SubjectSeat&&e.ActualTurnNumber==r.ActualTurnNumber&&e.PhaseInstanceId==r.PhaseInstanceId&&e.Branch==r.Branch&&e.RandomBefore==r.RandomBefore&&e.RandomAfter==r.RandomAfter&&e.RemovedCount==r.Grants.Sum(g=>g.Count))!=1)return false;
  if(r.Stage is "fire-native" or "hp-native")return f.PendingMovementContinuation is null;
  if(r.Stage is not ("take-native" or "claim-native")||r.MovementBefore<0||r.MovementAfter<r.MovementBefore||r.MovementAfter>_movementSequence||f.PendingMovementContinuation is not {SubjectSeat:var seat,BeforeCount:0,CoverageResultBind:null}||seat!=f.OwnerSeat)return false;
  var reason=r.Stage=="claim-native"?XuRongClaimReason:XuRongTakeReason;
  return r.PaidCardIds.Distinct().Count()==r.PaidCardIds.Count&&CompleteProgramEventHistory().OfType<XuRongCardsPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.Kind==reason&&e.Before==r.MovementBefore&&e.After==r.MovementAfter&&e.CardIds.SequenceEqual(r.PaidCardIds))==1&&r.PaidCardIds.All(id=>_cardMovements.Count(m=>m.CardId==id&&m.Sequence>r.MovementBefore&&m.Sequence<=r.MovementAfter&&m.Reason.Value==reason&&m.To==CardLocation.Hand(f.OwnerSeat))==1);
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 230)]
 private void RequireIssuedXuRong(ProgramSkillFrame f)
 {if(f.XuRong is not null&&!ValidXuRong(f))throw new InvalidOperationException("Xu Rong lost its exact marked phase, frozen random payment or native dying causal invoice.");}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 190)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 190)]
 private bool CanContinueIssuedXuRong(ProgramSkillFrame f)=>ValidXuRong(f);
 private bool IsXuRongMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>e is not null&&(e.Op is SkillProgramEffectOp.SettleXionghuoMarkedPlay or SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&(f.XuRong?.Stage is "take-native" or "claim-native")&&ValidXuRong(f);
 private bool XuRongFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!ValidXuRong(root)||root.XuRong is not {} r)return false;
  if(r.Stage=="fire-native"&&root.AttackAttempt is {SourceLess:false,TargetSeat:var target,SourceSeat:var source} attack&&target==r.SubjectSeat&&source==root.OwnerSeat)
  {if(child is DamageFrame damage)return damage.ParentFrameId==root.Id&&damage.TargetSeat==target&&damage.SourceSeat==source&&damage.Amount==attack.DamageAmount&&damage.Nature==attack.Nature;if(child is BeforeDamageProgramWindowFrame before)return(before.ContinuationAttackResolutionId??before.ParentFrameId)==root.Id&&before.TargetSeat==target&&before.SourceSeat==source&&before.Amount==attack.DamageAmount&&before.Nature==attack.Nature;}
  if(child is HpChangedTriggerWindowFrame hp&&r.Stage=="hp-native")return hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.TargetSeat==r.SubjectSeat&&hp.Change.Amount==1&&hp.Change.Kind==HpChangeKind.Loss&&hp.Change.SourceSeat is null;
  if(child is DyingFrame dying&&r.Stage=="hp-native")return dying.ParentFrameId==root.Id&&dying.VictimSeat==r.SubjectSeat&&dying.Continuation==DyingContinuationKind.ProgramSkill;
  if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>m.Sequence>r.MovementBefore&&m.Sequence<=r.MovementAfter&&r.PaidCardIds.Contains(m.CardId)&&_cardMovements.Contains(m));
  return false;
 }
 private ProgramSkillFrame? XuRongObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.XuRongFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
 private bool HasXuRongDamageObserver(long id)=>XuRongObserverRoot()is {}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool HasXuRongBeforeDamageObserver(long id)=>XuRongObserverRoot()is {}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool IsXuRongDying()=>ActiveDying is {}d&&XuRongObserverRoot()is {}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 260)]
 private bool TryAdvanceXuRongSubtree()=>_pendingDecision is null&&XuRongObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsXuRongNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||XuRongObserverRoot()is not {}r||r.Id==observer.Id)return false;
  var e=GetPausedPrivateOfferEffect(observer);return e is {Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is {}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,23600)]
 private bool ResumeXuRong(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{XuRong:{} r}f||f.Id!=id)return false;RequireIssuedXuRong(f);
  if(f.AttackAttempt is not null)return true;
  if(TryBeginQueuedRecoveryReplacement(id,f.PendingMovementContinuation is null?PostEventContinuation.Program:PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,f.PendingMovementContinuation is null?PostEventContinuation.Program:PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  AdvanceEventRulesAndQueueFact(new XuRongCompletedEvent(f.Id,r.Source,r.Stage,r.SubjectSeat,r.DyingFrameId));ReplaceRuntimeTop(f=f with{XuRong=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23600)]
 private bool ReturnXuRongMovement(ProgramSkillFrame f)=>f.XuRong is not null&&ResumeXuRong(f.Id);
}
