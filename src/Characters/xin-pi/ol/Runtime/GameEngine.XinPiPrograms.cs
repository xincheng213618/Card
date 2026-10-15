namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ChijieClaimReason="skill-program.chijie.claim",YinjuDrawReason="skill-program.yinju.draw";
 private bool XinPiSourceCurrent(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash;
 private SkillProgramStepOutcome BeginXinPi(ProgramSkillFrame supplied,SkillProgramEffect e)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.XinPi is not null||f.InstructionIndex!=1)throw new InvalidOperationException("XinPi requires its unpaid exact first instruction.");var source=XinPiSource(f);
  if(e.Op==SkillProgramEffectOp.ArmYinjuActualTurnRelation)
  {
   if(!XinPiSourceCurrent(f)||f.TriggerId is not null||_phase!=TurnPhase.Play||f.OwnerSeat!=_currentSeat||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats is not[var target]||target==f.OwnerSeat||!_players[target].IsAlive||_players[f.OwnerSeat].YinjuRelation is not null)throw new InvalidOperationException("Yinju requires its one real own-Play limited activation.");
   var relation=new YinjuTurnRelation(source,f.GameplayHash,_turnNumber,target,f.Id);_players[f.OwnerSeat].YinjuRelation=relation;AdvanceEventRulesAndQueueFact(new YinjuRelationArmedEvent(relation));return SkillProgramStepOutcome.Continue;
  }
  if(f.WindowContext is not{}w||f.TriggerId is not{}binding)throw new InvalidOperationException("XinPi trigger lost its native context.");var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,binding,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority);
  if(e.Op==SkillProgramEffectOp.InvalidateRemainingActualDamageCardEffects)
  {
   if(!XinPiSourceCurrent(f)||_players[f.OwnerSeat].ChijieUsedActualTurn==_turnNumber||ChijieDamageUse(f.OwnerSeat,w,out var damage)is not{Action:{}action}use||IsXinPiUseIneffective(use))throw new InvalidOperationException("Chijie requires real already-applied other-source card damage and its shared turn allowance.");
   _players[f.OwnerSeat].ChijieUsedActualTurn=_turnNumber;AdvanceEventRulesAndQueueFact(new ChijieUsedEvent(f.Id,source,f.GameplayHash,_turnNumber,"invalidate",use.Id,action.ActionId));var proof=new ChijieInvalidationProof(f.Id,source,f.GameplayHash,_turnNumber,use.Id,action.ActionId,damage.DamageFrameId,f.OwnerSeat,damage.SourceSeat);
   UpdateLifecycleCardUse(use.Id,u=>u with{ChijieInvalidation=proof});AdvanceEventRulesAndQueueFact(new ChijieUseInvalidatedEvent(proof));foreach(var seat in XinPiMainTargets(use))MarkCardEffectIneffective(use.Id,seat);return SkillProgramStepOutcome.Continue;
  }
  if(e.Op==SkillProgramEffectOp.ClaimCompletedUndamagedIncomingCardEntities)
  {
   if(!XinPiSourceCurrent(f)||_players[f.OwnerSeat].ChijieUsedActualTurn==_turnNumber||!ChijieUndamagedIncoming(f.OwnerSeat,w,out var use))throw new InvalidOperationException("Chijie claim requires the entire actual incoming use without any native damage.");
   var ids=ChijieClaimable(use);var before=_movementSequence;var receipt=new XinPiReceipt(source,f.GameplayHash,1,_turnNumber,w.ParentFrameId,XinPiStage.ClaimChildren,use.Id,use.Action!.ActionId,Before:before,After:before,Requested:ids.Count,Actual:ids.Count){Entities=ids,From=ids.Select(_cardZones.GetLocation).ToArray()};
   _players[f.OwnerSeat].ChijieUsedActualTurn=_turnNumber;ReplaceRuntimeTop(f with{XinPi=receipt,PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new ChijieUsedEvent(f.Id,source,f.GameplayHash,_turnNumber,"claim",use.Id,use.Action.ActionId));
   var claim=new XinPiClaimProof(f.Id,use.Id,use.Action.ActionId,source,f.GameplayHash,before){Entities=ids};UpdateLifecycleCardUse(use.Id,u=>u with{XinPiClaim=claim});AdvanceEventRulesAndQueueFact(new ChijieClaimIssuedEvent(f.Id,use.Id,use.Action.ActionId,source,f.GameplayHash,before,ids.Count));
   MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new(ChijieClaimReason),(batch,moves)=>{var active=GetActiveProgramFrame(f.Id);var r=active.XinPi! with{After=moves[^1].Sequence,BatchId=batch};ReplaceRuntimeTop(active with{XinPi=r});UpdateLifecycleCardUse(use.Id,u=>u with{XinPiClaim=claim with{After=r.After,BatchId=batch}});AdvanceEventRulesAndQueueFact(new ChijieClaimPaidEvent(f.Id,r.Source,use.Id,use.Action.ActionId,before,r.After,batch,ids.Count));});AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
  }
  if(e.Op==SkillProgramEffectOp.PreventYinjuDamageAndRecover)
  {
   if(!ExactYinjuBeforeCandidate(candidate,w,out var relation,out var before))throw new InvalidOperationException("Yinju prevention lost its issued exact source/recipient attempt.");var target=_players[relation.TargetSeat];var requested=target.HasHpValue?Math.Min(before.Amount,Math.Max(0,target.MaxHp-target.Hp)):0;var attempt=CurrentDamageAttempt?.ResolutionId??before.ParentFrameId;
   ReplaceRuntimeTop(f with{XinPi=new(source,f.GameplayHash,1,_turnNumber,w.ParentFrameId,XinPiStage.RecoveryChildren,DamageAttemptId:attempt,TargetSeat:target.Seat,Requested:requested,Relation:relation)});PreventProgramCurrentDamage(f);AdvanceEventRulesAndQueueFact(new YinjuRecoveryPaidEvent(f.Id,relation,before.Id,attempt,f.OwnerSeat,target.Seat,before.Amount,requested,target.Hp));if(requested>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,target.Seat,before.Amount,null,null);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
  }
  if(!ExactYinjuUseCandidate(candidate,w,out var drawRelation,out var native))throw new InvalidOperationException("Yinju target draw lost its exact accepted actual native use/recipient.");
  var rDraw=new XinPiReceipt(source,f.GameplayHash,1,_turnNumber,w.ParentFrameId,XinPiStage.OwnerDrawChildren,native.Id,native.Action!.ActionId,TargetSeat:drawRelation.TargetSeat,Relation:drawRelation);ReplaceRuntimeTop(f=f with{XinPi=rDraw});AdvanceEventRulesAndQueueFact(new YinjuUseDrawStartedEvent(f.Id,drawRelation,native.Id,native.Action.ActionId,w.ParentFrameId));PayYinjuDraw(f,f.OwnerSeat);return SkillProgramStepOutcome.AwaitChild;
 }
 private void PayYinjuDraw(ProgramSkillFrame f,int seat)
 {
  var r=f.XinPi! with{Before=_movementSequence,After=_movementSequence,Requested=1,Actual=0};ReplaceRuntimeTop(f with{XinPi=r,PendingMovementContinuation=new(seat,0,null)});var actual=DrawCards(_players[seat],1,true,new(YinjuDrawReason)).Count;r=r with{After=_movementSequence,Actual=actual};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{XinPi=r});AdvanceEventRulesAndQueueFact(new YinjuDrawPaidEvent(f.Id,r.Relation!,r.UseId!.Value,r.ActionId!.Value,seat,r.Before,r.After,actual));AdvanceRuntimeProgram(f.Id);
 }
 private bool DrainXinPi(ProgramSkillFrame f)
 {
  var movement=f.PendingMovementContinuation is not null;var continuation=movement?PostEventContinuation.AwaitedProgramMovement:PostEventContinuation.Program;return TryBeginQueuedRecoveryReplacement(f.Id,continuation)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,continuation)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 }
 private void FinishXinPi(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{XinPi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,25100)] private bool ResumeXinPi(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{XinPi:{}r}f||f.Id!=id)return false;AssertXinPiFrame(f);if(DrainXinPi(f))return true;
  if(r.Stage==XinPiStage.OwnerDrawChildren&&_players[f.OwnerSeat].IsAlive&&r.TargetSeat is{}target&&_players[target].IsAlive){ReplaceRuntimeTop(f=f with{XinPi=r with{Stage=XinPiStage.RecipientDrawChildren},PendingMovementContinuation=null});PayYinjuDraw(f,target);return true;}
  FinishXinPi(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,25100)] private bool ReturnXinPiMovement(ProgramSkillFrame f)=>f.XinPi is not null&&ResumeXinPi(f.Id);
 private bool CanContinueXinPi(ProgramSkillFrame f)
 {
  if(f.XinPi is null)return f.InstructionIndex==0&&f.TriggerId is{}trigger&&f.WindowContext is{}w&&f.ActivationId==trigger&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&HasIssuedYinjuCandidate(new(f.OwnerSeat,f.SkillId,trigger,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority),w);AssertXinPiFrame(f);return true;
 }
 private bool IsXinPiMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>f.XinPi is{Stage:XinPiStage.ClaimChildren or XinPiStage.OwnerDrawChildren or XinPiStage.RecipientDrawChildren}r&&e is not null&&XinPiComposition.IsOp(e.Op)&&CanContinueXinPi(f)&&p.SubjectSeat==(r.Stage==XinPiStage.RecipientDrawChildren?r.TargetSeat:f.OwnerSeat)&&p.BeforeCount==0&&p.CoverageResultBind is null;
 private void AssertXinPiFrame(ProgramSkillFrame f)
 {
  if(f.XinPi is not{}r)return;
  if(r.Source!=XinPiSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.ActualTurn!=_turnNumber||r.WindowId!=f.WindowContext?.ParentFrameId||!Enum.IsDefined(r.Stage)||CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.BindingId==GetProgramBindingId(f)&&e.SkillInstanceId==f.SkillInstanceId)!=1)throw new InvalidOperationException("XinPi pending instruction lost its exact source/hash/window receipt.");
  if(r.Stage==XinPiStage.RecoveryChildren)
  {
   if(r.Relation is not{}rel||!YinjuRelationValid(rel)||r.DamageAttemptId is not{}attempt||r.TargetSeat!=rel.TargetSeat||f.PendingMovementContinuation is not null||CompleteProgramEventHistory().OfType<YinjuRecoveryPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Relation==rel&&e.WindowId==r.WindowId&&e.DamageAttemptId==attempt&&e.RequestedRecovery==r.Requested&&e.SourceSeat==f.OwnerSeat&&e.TargetSeat==r.TargetSeat)!=1||!CompleteProgramEventHistory().OfType<ProgramDamagePreventedEvent>().Any(e=>e.FrameId==r.WindowId&&e.SourceSeat==f.OwnerSeat&&e.TargetSeat==r.TargetSeat))throw new InvalidOperationException("Yinju recovery lost its actually prevented exact attempt.");return;
  }
  if(f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null}p||p.SubjectSeat!=(r.Stage==XinPiStage.RecipientDrawChildren?r.TargetSeat:f.OwnerSeat)||r.Before<0||r.After<r.Before||r.After>_movementSequence)throw new InvalidOperationException("XinPi movement lost its exact once-paid return.");
  if(r.Stage==XinPiStage.ClaimChildren)
  {
   if(r.UseId is not{}useId||r.ActionId is not{}actionId||r.BatchId is not{}batch||r.Entities.Count==0||r.Entities.Count!=r.From.Count||r.Entities.Distinct().Count()!=r.Entities.Count||LifecycleCardUse(useId)is not{XinPiClaim:{}claim}use||claim.FrameId!=f.Id||claim.ActionId!=actionId||!claim.Entities.SequenceEqual(r.Entities)||CompleteProgramEventHistory().OfType<ChijieClaimPaidEvent>().Count(e=>e==new ChijieClaimPaidEvent(f.Id,r.Source,useId,actionId,r.Before,r.After,batch,r.Entities.Count))!=1||r.Entities.Where((id,i)=>r.From[i].Zone is not(CardZoneKind.Processing or CardZoneKind.DiscardPile)||!IsXinPiPhysicalClaim(useId,id)||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==id&&m.From==r.From[i]&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ChijieClaimReason)!=1).Any())throw new InvalidOperationException("Chijie claim changed its exact native paid entities.");return;
  }
  if(r.Relation is not{}relation||!YinjuRelationValid(relation)||r.UseId is not{}nativeId||r.ActionId is not{}nativeAction||r.Requested!=1||r.Actual is<0 or>1||CompleteProgramEventHistory().OfType<YinjuUseDrawStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Relation==relation&&e.UseId==nativeId&&e.ActionId==nativeAction&&e.WindowId==r.WindowId)!=1||CompleteProgramEventHistory().OfType<YinjuDrawPaidEvent>().Count(e=>e==new YinjuDrawPaidEvent(f.Id,relation,nativeId,nativeAction,p.SubjectSeat,r.Before,r.After,r.Actual))!=1||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(p.SubjectSeat)&&m.Reason.Value==YinjuDrawReason)!=r.Actual)throw new InvalidOperationException("Yinju draw changed its actual accepted use or paid draw invoice.");
 }
 [ProgramCapability]
 private sealed class XinPiProgramCapability(GameEngine engine) : IXinPiProgramHost
 {public SkillProgramStepOutcome BeginXinPi(ProgramSkillFrame f,SkillProgramEffect e)=>engine.BeginXinPi(f,e);}
}
