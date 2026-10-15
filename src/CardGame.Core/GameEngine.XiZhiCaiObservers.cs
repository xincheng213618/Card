namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool ValidXianfuEcho(ProgramSkillFrame f)
 {
  if(f.XianfuPending is not {} p)return false;var r=p.Receipt;var g=r.Grant;
  return g.Source.OwnerSeat==f.OwnerSeat&&g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId&&g.GameplayHash==f.GameplayHash&&r==f.WindowContext?.XianfuEcho&&p.InstructionIndex==f.InstructionIndex&&p.NativeIssued&&r.ActualAmount>0&&r.OriginalTargetSeat==g.TargetSeat&&
   CompleteProgramEventHistory().OfType<XianfuRelationArmedEvent>().Count(e=>e.Grant==g)==1&&CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Count(e=>e.Receipt==r)==1&&CompleteProgramEventHistory().OfType<XianfuEchoIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Receipt==r&&e.InstructionIndex==p.InstructionIndex&&e.NativeAmount==p.NativeAmount)==1&&
   GetPausedPrivateOfferEffect(f)?.Op==SkillProgramEffectOp.SettleXianfuNativeEcho;
 }
 private IDamageAttempt? ChouceOriginalAttack(ChoucePointReceipt r)
 {
  var owner=_resolutionStack.SingleOrDefault(f=>f.Id==r.OriginalAttackId);
  return owner switch
  {ProgramSkillFrame {AttackAttempt:not null}=>new ProgramAttackHandle(this,r.OriginalAttackId),
   CardUseFrame {CardAttack.Active:true} or ProgramSkillFrame {CardAttack.Active:true} or JudgmentFrame {CardAttack.Active:true}=>new CardAttackHandle(this,r.OriginalAttackId),_=>null};
 }
 private bool ValidChouce(ProgramSkillFrame f)
 {
  if(f.Chouce is not {} p)return false;var r=p.Receipt;
  if(r.Source!=XiZhiCaiSource(f)||r.GameplayHash!=f.GameplayHash||r.DamageTargetSeat!=f.OwnerSeat||r.PointIndex<0||r.PointIndex>=r.OriginalAmount||f.WindowContext is not {Window:SkillProgramTriggerWindow.AfterDamageApplied} c||c.ParentFrameId!=r.DamageWindowId||c.OccurrenceIndex!=r.PointIndex||f.InstructionIndex<r.StartInstructionIndex||f.InstructionIndex>r.StartInstructionIndex+1||!Enum.IsDefined(p.Stage)||CompleteProgramEventHistory().OfType<ChouceJudgmentIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Receipt==r)!=1)return false;
  var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
  if(index<1||_resolutionStack[index-1] is not DamageTriggerWindowFrame w||w.Id!=r.DamageWindowId||w.ParentFrameId!=r.DamageFrameId||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||!MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex].ToProgramCandidate()))return false;
  var original=ChouceOriginalAttack(r);
  if(original is null||original.ResolutionId!=r.OriginalAttackId||original.TargetSeat!=r.DamageTargetSeat||original.DamageAmount!=r.OriginalAmount||!original.DamageWasApplied||(original.IsSourceLess||original.IsDelayedJudgmentDamage?null:(int?)original.SourceSeat)!=r.DamageSourceSeat)return false;
  if(p.FrozenSuit is {} suit&&CompleteProgramEventHistory().OfType<ChouceJudgmentFrozenEvent>().Count(e=>e.FrameId==f.Id&&e.JudgmentFrameId==r.JudgmentFrameId&&e.EffectiveSuit==suit)!=1)return false;
  if(p.Stage==ChouceStage.Judgment)return GetPausedPrivateOfferEffect(f)?.Op is SkillProgramEffectOp.StartChouceDamagePointJudgment or SkillProgramEffectOp.ResolveChouceFrozenJudgment;
  if(p.FrozenSuit is null||f.InstructionIndex!=r.StartInstructionIndex+1||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveChouceFrozenJudgment)return false;
  if(p.Stage is ChouceStage.ChooseTarget or ChouceStage.ChooseCard)return f.PendingMovementContinuation is null;
  if(p.TargetSeat is not {} target||p.Before<0||p.After<p.Before||p.After>_movementSequence||f.PendingMovementContinuation is not {BeforeCount:0,CoverageResultBind:null,SubjectSeat:var actor}||actor!=target)return false;
  var red=ChouceIsRed(f);
  if(CompleteProgramEventHistory().OfType<ChouceOutcomeIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.JudgmentFrameId==r.JudgmentFrameId&&e.TargetSeat==target&&e.Red==red&&e.Before==p.Before&&e.After==p.After&&e.Count==p.Count&&e.CardId==p.CardId)!=1)return false;
  return red?p.Count is >=0 and <=2&&_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(target)&&m.Reason.Value=="program.chouce.red")==p.Count:
   p.Count==1&&p.CardId is {} id&&p.CardLocation is {} location&&_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.CardId==id&&m.From==location&&m.Reason.Value=="program.chouce.black"&&(m.To==CardLocation.DiscardPile||m.To==CardLocation.OutsideGame&&GetAdvancedCard(id).IsGeneralWeapon))==1;
 }
 private void RequireIssuedXiZhiCai(ProgramSkillFrame f)
 {if(f.XianfuPending is not null&&!ValidXianfuEcho(f))throw new InvalidOperationException("Xianfu lost its exact original native mutation and once-issued echo.");if(f.Chouce is not null&&!ValidChouce(f))throw new InvalidOperationException("Chouce lost its actual owning damage point, final judgment or paid outcome.");}
 private bool CanContinueIssuedXiZhiCai(ProgramSkillFrame f)=>ValidXianfuEcho(f)||ValidChouce(f);
 private bool IsXiZhiCaiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>p.BeforeCount==0&&p.CoverageResultBind is null&&
  (e?.Op==SkillProgramEffectOp.SettleXianfuNativeEcho&&f.XianfuPending is {Receipt.Kind:XianfuEchoKind.Recovery}&&p.SubjectSeat==f.OwnerSeat&&ValidXianfuEcho(f)||
   e?.Op==SkillProgramEffectOp.ResolveChouceFrozenJudgment&&f.Chouce is {Stage:ChouceStage.NativeChildren,TargetSeat:{} target}&&p.SubjectSeat==target&&ValidChouce(f));
 private bool XiZhiCaiFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(ValidXianfuEcho(root)&&root.XianfuPending is {} echo)
  {
   if(echo.Receipt.Kind==XianfuEchoKind.Damage&&root.AttackAttempt is {SourceLess:true,SourceSeat:var source,TargetSeat:var target} attack&&source==root.OwnerSeat&&target==root.OwnerSeat)
   {
    if(child is DamageFrame d)return d.ParentFrameId==root.Id&&d.SourceSeat==source&&d.TargetSeat==target&&d.Amount==attack.DamageAmount;
    if(child is BeforeDamageProgramWindowFrame before)return (before.ContinuationAttackResolutionId??before.ParentFrameId)==root.Id&&before.TargetSeat==target&&before.SourceSeat==source&&before.Amount==attack.DamageAmount;
   }
   if(echo.Receipt.Kind==XianfuEchoKind.Recovery)
   {
    var amount=echo.NativeAmount;
    if(child is RecoveryReplacementFrame recovery)return recovery.ParentFrameId==root.Id&&recovery.Attempt.SourceSeat==root.OwnerSeat&&recovery.Attempt.TargetSeat==root.OwnerSeat&&recovery.Attempt.Amount==amount&&recovery.Attempt.HpBefore==echo.HpBefore&&recovery.Attempt.Completion is {Producer:RecoveryAttemptProducer.Program,InstructionIndex:var instruction}&&instruction==echo.InstructionIndex;
    if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==root.OwnerSeat&&hp.Change.TargetSeat==root.OwnerSeat&&hp.Change.HpBefore==echo.HpBefore&&hp.Change.Amount==amount;
   }
  }
  if(!ValidChouce(root)||root.Chouce is not {} p)return false;
  if(child is JudgmentFrame j)return j.Id==p.Receipt.JudgmentFrameId&&j.ParentFrameId==root.Id&&j.TargetSeat==root.OwnerSeat&&j.SourceSeat==root.OwnerSeat&&j.Reason==p.Receipt.Reason&&j.Continuation==JudgmentContinuationKind.ProgramSkill&&j.ProgramResultBind==p.Receipt.ResultBind&&j.ProgramResultVisibility==SkillProgramCardSetVisibility.Public;
  if(p.Stage!=ChouceStage.NativeChildren)return false;
  if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>p.Before&&m.Sequence<=p.After&&m.Reason.Value==(ChouceIsRed(root)?"program.chouce.red":"program.chouce.black"));
  if(p.CardId is {} lion&&GetAdvancedCard(lion).Kind==CardKind.SilverLion&&p.CardLocation is {Zone:CardZoneKind.Equipment,OwnerSeat:{} seat})
  {
   if(child is RecoveryReplacementFrame recovery)return recovery.Attempt.SourceSeat==seat&&recovery.Attempt.TargetSeat==seat&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion is {Producer:RecoveryAttemptProducer.SilverLion,MoveReason:{} reason}&&reason.Value=="program.chouce.black";
   if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==root.Id&&hp.ResumeFrameId==root.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.TargetSeat==seat&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1;
  }
  return false;
 }
 private bool XiZhiCaiObserverEdge(int index)
 {
  var parent=_resolutionStack[index-1];var child=_resolutionStack[index];
  if(parent is JudgmentFrame j&&child is ProgramJudgmentTriggerWindowFrame w)return w.ParentFrameId==j.Id&&w.Judgment.JudgmentFrameId==j.Id;
  if(parent is ProgramJudgmentTriggerWindowFrame{ZhenyiBeforeResult:true} before&&child is ProgramSkillFrame zhenyi)return IsExactZhenyiBeforeResultProgramChild(before,zhenyi);
  if(parent is ProgramJudgmentTriggerWindowFrame window&&child is ProgramSkillFrame skill)return window.CandidateIndex>=0&&window.CandidateIndex<window.Candidates.Count&&skill.WindowContext is {Window:SkillProgramTriggerWindow.JudgmentFinalized,Judgment:{} final} context&&context.ParentFrameId==window.Id&&final==window.Judgment&&window.Candidates[window.CandidateIndex] is {} c&&c.OwnerSeat==skill.OwnerSeat&&c.SkillId==skill.SkillId&&c.TriggerId==skill.TriggerId&&c.SkillInstanceId==skill.SkillInstanceId&&c.GameplayHash==skill.GameplayHash;
  return IsMovementObserverEdge(index);
 }
 private ProgramSkillFrame? XiZhiCaiObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.XiZhiCaiFirstChild(r,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.XiZhiCaiObserverEdge(n),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
 private bool IsExactChouceFinalizedChildSubtree(ProgramJudgmentTriggerWindowFrame frame,JudgmentFrame? judgmentFrame,int frameIndex)
 {
  if(judgmentFrame is null||frameIndex<2||frameIndex+1>=_resolutionStack.Count||_resolutionStack[frameIndex].Id!=frame.Id||_resolutionStack[frameIndex-1].Id!=judgmentFrame.Id||
   _resolutionStack[frameIndex-2] is not ProgramSkillFrame {Chouce:{} pending} root||root.Id!=judgmentFrame.ParentFrameId||pending.Receipt.JudgmentFrameId!=judgmentFrame.Id||pending.Receipt.Reason!=judgmentFrame.Reason||
   frame.ParentFrameId!=judgmentFrame.Id||frame.Judgment.JudgmentFrameId!=judgmentFrame.Id||frame.Judgment.SubjectSeat!=root.OwnerSeat||frame.CandidateIndex<0||frame.CandidateIndex>=frame.Candidates.Count||
   _resolutionStack[frameIndex+1] is not ProgramSkillFrame child||child.WindowContext is not {Window:SkillProgramTriggerWindow.JudgmentFinalized,Judgment:{} final} context||context.ParentFrameId!=frame.Id||final!=frame.Judgment)return false;
  var candidate=frame.Candidates[frame.CandidateIndex];if(candidate.OwnerSeat!=child.OwnerSeat||candidate.SkillId!=child.SkillId||candidate.TriggerId!=child.TriggerId||candidate.SkillInstanceId!=child.SkillInstanceId||candidate.GameplayHash!=child.GameplayHash)return false;
  return ValidChouce(root)&&FindProgramObserverRoot(static(e,r,c)=>e.XiZhiCaiFirstChild(r,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.XiZhiCaiObserverEdge(n),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)),root.Id) is not null;
 }
 private bool HasXiZhiCaiDamageObserver(long id)=>XiZhiCaiObserverRoot() is {} root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool HasXiZhiCaiBeforeDamageObserver(long id)=>XiZhiCaiObserverRoot() is {} root&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool IsXiZhiCaiDying()=>ActiveDying is {} d&&XiZhiCaiObserverRoot() is {} root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool TryAdvanceXiZhiCaiSubtree()=>_pendingDecision is null&&XiZhiCaiObserverRoot() is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsXiZhiCaiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {
  if(sourceLess&&ValidXianfuEcho(f)&&f.XianfuPending is {Receipt.Kind:XianfuEchoKind.Damage} echo&&target==f.OwnerSeat&&amount==echo.Receipt.ActualAmount&&source is null&&nature is null)return true;
  if(sourceLess||f.AttackAttempt is not null||f.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=f.Id||XiZhiCaiObserverRoot() is not {} root||root.Id==f.Id)return false;
  var e=GetPausedPrivateOfferEffect(f);return e is {Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is {} p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
 }
}
