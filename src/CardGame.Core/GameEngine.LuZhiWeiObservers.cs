namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool CanContinueIssuedLuZhiWei(ProgramSkillFrame f)
 {
  if(f.Qingzhong is null)
  {
   if(f.InstructionIndex!=0||f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayEnding,QingzhongDebt:{} issued}||f.TriggerId!=issued.EndingBindingId||f.SkillId!=issued.Source.SkillId||f.SkillInstanceId!=issued.Source.SkillInstanceId||f.OwnerSeat!=issued.Source.OwnerSeat||f.GameplayHash!=issued.GameplayHash||!_players[f.OwnerSeat].IsAlive||!_players[f.OwnerSeat].QingzhongDebts.Contains(issued)||CompleteProgramEventHistory().OfType<QingzhongDebtArmedEvent>().Count(e=>e.Debt==issued)!=1)return false;
   var i=_resolutionStack.FindIndex(x=>x.Id==f.Id);return i>0&&_resolutionStack[i-1]is ProgramLifecycleTriggerWindowFrame w&&w.Window==SkillProgramTriggerWindow.PlayEnding&&w.QingzhongEndingPhase==issued.Phase&&w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex])&&_contentRegistry.GetSkill(f.SkillId).Program!.Triggers.Single(t=>t.Id==f.TriggerId).Effects is [{Op:SkillProgramEffectOp.SettleQingzhongActualPlayDebt}];
  }
  var p=f.Qingzhong!;var d=p.Debt;
  if(GetPausedPrivateOfferEffect(f)?.Op!=(p.Stage==QingzhongStage.DrawChildren?SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt:SkillProgramEffectOp.SettleQingzhongActualPlayDebt))return false;
  if(d.Source.OwnerSeat!=f.OwnerSeat||d.Source.SkillId!=f.SkillId||d.Source.SkillInstanceId!=f.SkillInstanceId||d.GameplayHash!=f.GameplayHash||p.InstructionIndex!=f.InstructionIndex||f.InstructionIndex!=1||d.Phase.OwnerSeat!=f.OwnerSeat||!Enum.IsDefined(p.Stage)||CompleteProgramEventHistory().OfType<QingzhongDebtArmedEvent>().Count(e=>e.Debt==d)!=1)return false;
  var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(index<1)return false;
  if(p.Stage==QingzhongStage.DrawChildren)
  {
   if(f.Id!=d.ProgramFrameId||f.TriggerId!=d.Source.BindingId||f.WindowContext?.QingzhongPhase!=d.Phase||_resolutionStack[index-1]is not PlayPhaseStartingBoundaryFrame w||w.Id!=d.StartingWindowId||w.OwnerSeat!=f.OwnerSeat||w.QingzhongStartingPhase!=d.Phase||w.ItemIndex<0||w.ItemIndex>=w.Items.Count||w.Items[w.ItemIndex].Candidate is not {} candidate||!MountObserverCandidateMatches(f,candidate))return false;
  }
  else
  {
   if(f.TriggerId!=d.EndingBindingId||f.WindowContext?.QingzhongDebt!=d||_resolutionStack[index-1]is not ProgramLifecycleTriggerWindowFrame w||w.QingzhongEndingPhase!=d.Phase||w.Window!=SkillProgramTriggerWindow.PlayEnding||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||!MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex]))return false;
   if(p.Stage==QingzhongStage.ChooseMinimum)return f.PendingMovementContinuation is null&&_players[f.OwnerSeat].QingzhongDebts.Contains(d);
   if(p.TargetSeat is null||p.OwnerHandCount<0||p.TargetHandCount<0||p.ActualCount!=(p.TargetSeat==f.OwnerSeat?0:p.OwnerHandCount+p.TargetHandCount)||_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.Reason.Value==QingzhongSwapReason&&m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.Hand(p.TargetSeat.Value))!=(p.TargetSeat==f.OwnerSeat?0:p.OwnerHandCount)||_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.Reason.Value==QingzhongSwapReason&&m.From==CardLocation.Hand(p.TargetSeat.Value)&&m.To==CardLocation.Hand(f.OwnerSeat))!=(p.TargetSeat==f.OwnerSeat?0:p.TargetHandCount)||CompleteProgramEventHistory().OfType<QingzhongDebtConsumedEvent>().Count(e=>e.FrameId==f.Id&&e.Debt==d&&e.Settled)!=1)return false;
  }
  return p.Before>=0&&p.After>=p.Before&&p.After<=_movementSequence&&p.ActualCount>=0&&f.PendingMovementContinuation is {SubjectSeat:var seat,BeforeCount:0,CoverageResultBind:null}&&seat==f.OwnerSeat&&CompleteProgramEventHistory().OfType<QingzhongMovementIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Stage==p.Stage&&e.Before==p.Before&&e.After==p.After&&e.ActualCount==p.ActualCount&&e.TargetSeat==p.TargetSeat&&e.OwnerHandCount==p.OwnerHandCount&&e.TargetHandCount==p.TargetHandCount)==1&&_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&IsExactQingzhongMovement(f,p,m))==p.ActualCount;
 }
 private bool IsExactQingzhongMovement(ProgramSkillFrame f,QingzhongPending p,CardMovementRecord m)=>p.Stage==QingzhongStage.DrawChildren?m.Reason.Value==QingzhongDrawReason&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat):p.Stage==QingzhongStage.SwapChildren&&p.TargetSeat is {} target&&m.Reason.Value==QingzhongSwapReason&&(m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.Hand(target)||m.From==CardLocation.Hand(target)&&m.To==CardLocation.Hand(f.OwnerSeat));
 private void RequireIssuedLuZhiWei(ProgramSkillFrame f)
 {if(f.Qingzhong is not null&&!CanContinueIssuedLuZhiWei(f))throw new InvalidOperationException("Qingzhong lost its exact original phase, owning candidate or paid native movement bounds.");}
 private bool IsLuZhiWeiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>(e?.Op is SkillProgramEffectOp.DrawAndArmQingzhongActualPlayDebt or SkillProgramEffectOp.SettleQingzhongActualPlayDebt)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&CanContinueIssuedLuZhiWei(f);
 private bool LuZhiWeiFirstChild(ProgramSkillFrame root,ResolutionFrame child)=>CanContinueIssuedLuZhiWei(root)&&root.Qingzhong is {} p&&child is CardsMovedTriggerWindowFrame moved&&moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>p.Before&&m.Sequence<=p.After&&IsExactQingzhongMovement(root,p,m));
 private ProgramSkillFrame? LuZhiWeiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.LuZhiWeiFirstChild(r,c),static(e,i)=>e.HasMovementObserverSuffix(i));
 private bool HasLuZhiWeiDamageObserver(long id)=>LuZhiWeiObserverRoot()is {} root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool HasLuZhiWeiBeforeDamageObserver(long id)=>LuZhiWeiObserverRoot()is {} root&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool IsLuZhiWeiDying()=>ActiveDying is {} d&&LuZhiWeiObserverRoot()is {} root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool TryAdvanceLuZhiWeiSubtree()=>_pendingDecision is null&&LuZhiWeiObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsLuZhiWeiNestedDamage(ProgramSkillFrame o,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {
  if(sourceLess||o.AttackAttempt is not null||o.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=o.Id||LuZhiWeiObserverRoot()is not {} root||root.Id==o.Id)return false;
  var e=GetPausedPrivateOfferEffect(o);return e is {Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is {} p?ResolveProgramParticipant(o,p):ResolveProgramEffectTarget(o,e.Target));
 }
}
