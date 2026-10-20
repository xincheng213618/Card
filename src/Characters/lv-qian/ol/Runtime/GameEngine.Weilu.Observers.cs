namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void ConsumeUnreturnedWeiluDebt(CharacterState owner,WeiluPaidDebt debt)
 {
  owner.WeiluDebts=(owner.WeiluDebts??[]).Where(g=>g!=debt).ToArray();
  AdvanceEventRulesAndQueueFact(new WeiluDebtConsumedEvent(0,debt.ProducerFrameId,debt.Phase,debt.TargetSeat,0,_players[debt.TargetSeat].IsAlive));
 }
 private WeiluPhaseKey? CaptureWeiluStartingPhase(CharacterState owner)=>owner.WeiluFuture?.FirstOrDefault(g=>g.FirstPhase is{}p&&p.ActualTurn==_turnNumber&&p.PhaseInstance==_cardUseDebitPhaseInstanceId&&LvQianSourceCurrent(g.Source,g.GameplayHash))?.FirstPhase;
 private WeiluPhaseKey? CaptureWeiluEndingPhase(CharacterState owner)=>owner.WeiluDebts?.Any(g=>g.Phase.ActualTurn==_turnNumber&&g.Phase.PhaseInstance==_cardUseDebitPhaseInstanceId)==true?new(_turnNumber,owner.Seat,_cardUseDebitPhaseInstanceId):null;
 private bool HasCurrentIssuedWeiluDebt(CharacterState owner)=>CaptureWeiluEndingPhase(owner)is not null;
 private ProgramSkillWindowContext CreateWeiluStartingContext(PlayPhaseStartingBoundaryFrame frame,ProgramSkillWindowContext context)=>context with{WeiluPhase=frame.WeiluStartingPhase};
 private IEnumerable<ProgramTriggerCandidate> IssuedWeiluEndingCandidates(CharacterState owner)=>(owner.WeiluDebts??[]).Where(g=>g.Phase.ActualTurn==_turnNumber&&g.Phase.PhaseInstance==_cardUseDebitPhaseInstanceId).GroupBy(g=>(g.Source.SkillId,g.Source.SkillInstanceId,g.GameplayHash)).Select(g=>new ProgramTriggerCandidate(owner.Seat,g.Key.SkillId,"restore-paid-phase",g.Key.SkillInstanceId,g.Key.GameplayHash,0));
 private ProgramSkillWindowContext CreateWeiluEndingContext(ProgramLifecycleTriggerWindowFrame frame,ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)=>context with{WeiluPhase=frame.WeiluEndingPhase};
 private bool HasIssuedWeiluDebtCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext w)=>w.Window==SkillProgramTriggerWindow.PlayEnding&&w.WeiluPhase is{}phase&&phase.OwnerSeat==c.OwnerSeat&&(_players[c.OwnerSeat].WeiluDebts??[]).Any(g=>g.Phase==phase&&g.Source.SkillId==c.SkillId&&g.Source.SkillInstanceId==c.SkillInstanceId&&g.GameplayHash==c.GameplayHash)&&c.BindingId=="restore-paid-phase"&&_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(f=>f.Id==w.ParentFrameId)is{}actual&&actual.OwnerSeat==c.OwnerSeat&&actual.WeiluEndingPhase==phase&&actual.CandidateIndex>=0&&actual.CandidateIndex<actual.Candidates.Count&&actual.Candidates[actual.CandidateIndex]==c;

 private bool TryBeginForcedWeiluPhaseEnd()
 {
  if(!HasLvQianCapability||_phase!=TurnPhase.Play||_winner!=Winner.None||_pendingDecision is not null||_resolutionStack.Count!=0)return false;
  var owner=_players[_currentSeat];var phase=CaptureWeiluEndingPhase(owner);if(phase is null)return false;var candidates=IssuedWeiluEndingCandidates(owner).ToArray();if(candidates.Length==0)return false;
  var facts=CaptureProgramTriggerFacts(owner);var frame=new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence,owner.Seat,SkillProgramTriggerWindow.PlayEnding,Array.AsReadOnly(candidates),ProgramLifecycleContinuation.CompleteWeiluForcedPlayEnd,facts){WeiluEndingPhase=phase};PushRuntimeFrame(frame);AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();return true;
 }

 private bool ExactWeiluDamageParent(ProgramSkillFrame f,long damageId,int attacker,int amount)
 {
  var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
  if(index<2||f.WindowContext is not{}context||_resolutionStack[index-1]is not DamageTriggerWindowFrame window||window.Id!=context.ParentFrameId||window.ParentFrameId!=damageId||window.TriggerWindow!=SkillProgramTriggerWindow.AfterDamageApplied||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||!MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex].ToProgramCandidate())||_resolutionStack[index-2]is not DamageFrame damage||damage.Id!=damageId||damage.SourceSeat!=attacker||damage.TargetSeat!=f.OwnerSeat||damage.Amount!=amount)return false;
  var attack=GetDamageTriggerAttack(window);return attack.ResolutionId==damage.ParentFrameId&&attack.DamageWasApplied&&!attack.IsSourceLess&&!attack.IsDelayedJudgmentDamage&&attack.SourceSeat==attacker&&attack.TargetSeat==f.OwnerSeat&&attack.DamageAmount==amount&&TurnDefaultStatDamageRequestMatches(CompleteProgramEventHistory().ToArray(),damageId,attacker,f.OwnerSeat,amount,damage.Nature,false);
 }

}
