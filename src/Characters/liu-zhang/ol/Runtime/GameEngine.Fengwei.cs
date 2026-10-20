using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string FengweiDrawReason="skill-program.liu-zhang.fengwei.draw";
 private bool HasFengweiCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveFengweiRoundStartDraw);
 private bool HasLiuZhangCapability=>HasFengweiCapability||HasZonghuCapability;
 private static CardConversionSource FengweiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool FengweiLive(CharacterState p)=>p.IsAlive&&GetSkillBindingShard(p).ProgramInstances.Any(i=>i.SkillId=="ol:fengwei"&&i.Program.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveFengweiRoundStartDraw)));
 private int? FengweiMarkedHand(int seat)=>ProgramEventHistory<FengweiAcquiredEvent>().Where(e=>e.Source.OwnerSeat==seat&&e.Round==_roundNumber&&_cardZones.GetLocation(e.CardId)==CardLocation.Hand(seat)).Select(e=>(int?)e.CardId).FirstOrDefault();
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition,27510,SkillProgramEffectOp.ResolveFengweiRoundStartDraw,SkillProgramEffectOp.ResolveFengweiActualDrawSkip)]
 private bool QualifyFengwei(ProgramTriggerLegalityContext c)
 {
  if(c.SkillId!="ol:fengwei"||c.WindowContext is not{}w||!c.Owner.IsAlive)return false;
  if(c.Trigger.Id=="draw-skip")return w.Window==SkillProgramTriggerWindow.DrawPhaseStarting&&c.OwnerSeat==_currentSeat&&FengweiMarkedHand(c.OwnerSeat)is not null;
  return c.Trigger.Id=="round-draw"&&w.Window==SkillProgramTriggerWindow.RoundStarting&&_roundTurnSeats.Count==1&&_turnProgression.Kind!=ActualTurnKind.Extra&&ProgramEventHistory<RoundStartedEvent>().Any(e=>e.RoundNumber==_roundNumber&&e.ActorSeat==_currentSeat)&&!ProgramEventHistory<FengweiStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Round==_roundNumber);
 }
 private SkillProgramStepOutcome ResolveFengwei(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.Fengwei is not null||f.InstructionIndex!=1||f.SkillId!="ol:fengwei"||f.WindowContext is not{}c||_resolutionStack.Count<2||_resolutionStack[^2]is not ProgramLifecycleTriggerWindowFrame w||w.Id!=c.ParentFrameId||w.Window!=c.Window)throw new InvalidOperationException("Fengwei requires its actual round/draw lifecycle parent.");
  var source=FengweiSource(f);
  if(op==SkillProgramEffectOp.ResolveFengweiActualDrawSkip){if(c.Window!=SkillProgramTriggerWindow.DrawPhaseStarting||f.OwnerSeat!=_currentSeat||FengweiMarkedHand(f.OwnerSeat)is not{}id)throw new InvalidOperationException("Fengwei skip requires a currently held original acquisition.");AdvanceEventRulesAndQueueFact(new FengweiDrawSkippedEvent(f.Id,source,_roundNumber,w.Id,id));ReplaceRuntimeFrame(w.Id,w with{ActualPhaseSubstitution=new(w.Id,f.Id,f.OwnerSeat,f.SkillId,f.SkillInstanceId,_turnNumber,SkillProgramTurnPhase.Draw)});AdvanceEventRulesAndQueueFact(new ProgramTurnPhaseSubstitutedEvent(f.Id,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.OwnerSeat,_turnNumber,c.Window,SkillProgramTurnPhase.Draw));return SkipProgramTurnPhases(f,[SkillProgramTurnPhase.Draw]);}
  if(c.Window!=SkillProgramTriggerWindow.RoundStarting||_roundTurnSeats.Count!=1||ProgramEventHistory<FengweiStartedEvent>().Any(e=>e.Source.OwnerSeat==f.OwnerSeat&&e.Round==_roundNumber))throw new InvalidOperationException("Fengwei may start once per genuine round.");
  var r=new FengweiReceipt(source,f.GameplayHash,w.Id,_roundNumber,FengweiStage.Choice);ReplaceRuntimeTop(f=f with{Fengwei=r});AdvanceEventRulesAndQueueFact(new FengweiStartedEvent(f.Id,source,f.GameplayHash,w.Id,_roundNumber));foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);PublishFengwei(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private IReadOnlyList<PromptChoice> FengweiChoices(ProgramSkillFrame f)=>Enumerable.Range(1,5).Select(n=>new PromptChoice(new($"fengwei.{f.Id}.{n}"),$"摸{n}张牌",[],[],new Dictionary<string,string>{{"program-action","liu-zhang-fengwei"},{"count",n.ToString(CultureInfo.InvariantCulture)}})).ToArray();
 private void PublishFengwei(ProgramSkillFrame f)
 {
  if(!_players[f.OwnerSeat].IsAlive){FinishFengwei(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);var choices=FengweiChoices(f);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"丰蔚：选择摸一至五张牌。",[],[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("liu-zhang-fengwei")]
 private void AnswerFengwei(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Fengwei:{Stage:FengweiStage.Choice}r}f||!AssistedChoicesEqual(FengweiChoices(f).Where(c=>c.Id==choice.Id).ToArray(),[choice]))throw new InvalidOperationException("Fengwei requires its actual positive round draw selection.");AssertFengwei(f);ClearPendingDecision();var count=int.Parse(choice.Parameters["count"],CultureInfo.InvariantCulture);var before=_movementSequence;ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,Fengwei=r with{Stage=FengweiStage.DrawChildren,Requested=count,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  var drawn=DrawCards(_players[f.OwnerSeat],count,true,new(FengweiDrawReason),r.Source);f=GetActiveProgramFrame(f.Id);var after=_movementSequence;ReplaceRuntimeTop(f=f with{Fengwei=f.Fengwei! with{Actual=drawn.Count,After=after}});
  foreach(var id in drawn){var m=_cardMovements.Single(m=>m.Sequence>before&&m.Sequence<=after&&m.CardId==id&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==FengweiDrawReason);AdvanceEventRulesAndQueueFact(new FengweiAcquiredEvent(f.Id,r.Source,r.Round,id,m.Sequence));}AdvanceEventRulesAndQueueFact(new FengweiDrawCompletedEvent(f.Id,r.Source,r.Round,count,drawn.Count,before,after));ContinueFengwei(f.Id);
 }
 private void ContinueFengwei(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Fengwei:{}r}f||f.Id!=id)return;AssertFengwei(f);if(r.Stage==FengweiStage.Choice){if(_pendingDecision is null)PublishFengwei(f);return;}if(DrainZonghu(f))return;FinishFengwei(f);}
 private void FinishFengwei(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{Fengwei=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramContinuation(ProgramContinuationKind.Resume,27510)]private bool ResumeFengwei(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Fengwei:not null}f||f.Id!=id)return false;ContinueFengwei(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,27510)]private bool ReturnFengweiMovement(ProgramSkillFrame f){if(f.Fengwei is null)return false;ContinueFengwei(f.Id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27510)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27510)]private bool ContinueIssuedFengwei(ProgramSkillFrame f){if(f.Fengwei is null)return false;AssertFengwei(f);return true;}
 [ProgramAiChoice(SkillProgramEffectOp.ResolveFengweiRoundStartDraw)]private PromptChoice AiFengwei(PendingDecision p,ProgramSkillFrame f)=>p.Choices.Last();
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27510)]
 private void AssertFengwei(ProgramSkillFrame f)
 {
  if(f.Fengwei is not{}r)return;var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);
  if(f.InstructionIndex!=1||f.SkillId!="ol:fengwei"||f.TriggerId!="round-draw"||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveFengweiRoundStartDraw||r.Source!=FengweiSource(f)||r.GameplayHash!=f.GameplayHash||r.Round!=_roundNumber||i<1||_resolutionStack[i-1]is not ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.RoundStarting}w||w.Id!=r.ParentId||f.WindowContext?.ParentFrameId!=w.Id||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||!MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex])||ProgramEventHistory<FengweiStartedEvent>().Count(e=>e==new FengweiStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.Round))!=1||r.Before<0||r.After<r.Before||r.After>_movementSequence)throw new InvalidOperationException("Fengwei lost its genuine round owner, original source and scalar issuance.");
  if(r.Stage==FengweiStage.DrawChildren){if(r.Requested is<1 or>5||r.Actual<0||r.Actual>r.Requested||f.PendingMovementContinuation is not{SubjectSeat:var subject,BeforeCount:0,CoverageResultBind:null}||subject!=f.OwnerSeat||ProgramEventHistory<FengweiDrawCompletedEvent>().Count(e=>e==new FengweiDrawCompletedEvent(f.Id,r.Source,r.Round,r.Requested,r.Actual,r.Before,r.After))!=1||ProgramEventHistory<FengweiAcquiredEvent>().Count(e=>e.FrameId==f.Id)!=r.Actual||_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsFengweiMovement(f,m)))throw new InvalidOperationException("Fengwei lost its exact positive or exhausted-zero draw completion.");}
  else if(r.Requested!=0||r.Actual!=0||r.Before!=0||r.After!=0||f.PendingMovementContinuation is not null)throw new InvalidOperationException("Fengwei cannot prepay its round draw.");
 }
 private bool IsFengweiMovement(ProgramSkillFrame f,CardMovementRecord m)=>f.Fengwei is{Stage:FengweiStage.DrawChildren}r&&m.Sequence>r.Before&&m.Sequence<=r.After&&(m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==FengweiDrawReason||m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle);
 private bool FengweiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
 {
  if(f.Fengwei is not{Stage:FengweiStage.DrawChildren})return false;AssertFengwei(f);
  if(child is CardsMovedTriggerWindowFrame moved)return ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsFengweiMovement(f,m));
  if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement;
  if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
  return child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&state.ResumeProgramFrameId==f.Id;
 }
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 200)]
 [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 190)]
 private bool IsLiuZhangRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.Zonghu is{Stage:ZonghuStage.CostChildren,Paid:{}p}&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&p.Gifts.Any(g=>g.Kind==CardKind.SilverLion&&g.From==CardLocation.Equipment(f.OwnerSeat))&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==f.OwnerSeat&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==ZonghuGiftReason;
}
