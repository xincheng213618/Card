namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool ValidHuangQuanDraw(ProgramSkillFrame f)
 {
  if(f.HuangQuan is not{}p||p.Source!=HuangQuanSource(f)||p.GameplayHash!=f.GameplayHash||p.InstructionIndex!=f.InstructionIndex||f.InstructionIndex!=1||p.Kind is not("jianji" or "dianhu")||p.Stage is not("draw-paid" or "offer" or "issuing" or "native-use" or "returned")||p.Before<0||p.After<p.Before||p.After>_movementSequence||p.CardIds.Count>1||p.CardIds.Distinct().Count()!=p.CardIds.Count||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
  if(GetPausedPrivateOfferEffect(f)?.Op!=(p.Kind=="jianji"?SkillProgramEffectOp.DrawAndOfferJianjiExactCard:SkillProgramEffectOp.DrawForDianhuActualOccurrence))return false;
  if(p.Kind=="jianji"&&(f.TriggerId is not null||p.RecipientSeat==f.OwnerSeat||f.SelectedTargetSeats is not[var target]||target!=p.RecipientSeat))return false;
  if(p.Kind=="dianhu"&&(p.Occurrence is not{}r||f.WindowContext?.Dianhu!=r||p.RecipientSeat!=f.OwnerSeat||r.ActualAmount<=0||r.TargetSeat!=r.Relation.TargetSeat||CompleteProgramEventHistory().OfType<DianhuQualifiedEvent>().Count(e=>e.Receipt==r)!=1||CompleteProgramEventHistory().OfType<DianhuArmedEvent>().Count(e=>e.Relation==r.Relation)!=1))return false;
  return CompleteProgramEventHistory().OfType<HuangQuanDrawPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Source==p.Source&&e.GameplayHash==p.GameplayHash&&e.Kind==p.Kind&&e.RecipientSeat==p.RecipientSeat&&e.ActualTurn==p.ActualTurn&&e.TurnOwner==p.TurnOwner&&e.ActualPhase==p.ActualPhase&&e.Before==p.Before&&e.After==p.After&&e.CardCount==p.CardIds.Count&&e.Occurrence==p.Occurrence)==1&&_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.To==CardLocation.Hand(p.RecipientSeat)&&m.Reason.Value=="program."+p.Kind+".draw")==p.CardIds.Count&&p.CardIds.All(id=>_cardMovements.Count(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.CardId==id&&m.To==CardLocation.Hand(p.RecipientSeat)&&m.Reason.Value=="program."+p.Kind+".draw")==1);
 }
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 270)]
 private void RequireIssuedHuangQuan(ProgramSkillFrame f)
 {
  if(f.HuangQuan is not{}p)return;
  if(!ValidHuangQuanDraw(f)||p.Stage=="draw-paid"&&(f.PendingMovementContinuation is not{SubjectSeat:var target,BeforeCount:0,CoverageResultBind:null}||target!=p.RecipientSeat)||p.Stage!="draw-paid"&&f.PendingMovementContinuation is not null||p.Stage is "issuing" or "native-use" or "returned"&&(p.NativeUse is not{}r||!ValidJianjiReceipt(r,f,p.Stage!="issuing")))throw new InvalidOperationException("Huang Quan lost its exact relation occurrence, actual draw payment, native actor or owning request.");
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 230)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 230)]
 private bool CanContinueIssuedHuangQuan(ProgramSkillFrame f)
 {if(f.HuangQuan is null)return false;RequireIssuedHuangQuan(f);return true;}
 private bool IsHuangQuanMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation continuation)=>f.HuangQuan is{Stage:"draw-paid",RecipientSeat:var target}&&continuation.SubjectSeat==target&&continuation.BeforeCount==0&&continuation.CoverageResultBind is null&&e?.Op is SkillProgramEffectOp.DrawForDianhuActualOccurrence or SkillProgramEffectOp.DrawAndOfferJianjiExactCard&&ValidHuangQuanDraw(f);
 private bool HuangQuanFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!ValidHuangQuanDraw(root)||root.HuangQuan is not{}p)return false;
  if(IsJianjiDeclarationChild(root,child))return true;
  if(child is CardUseFrame use)return JianjiNativeUseRidesOn(use,root);
  return p.Stage=="draw-paid"&&child is CardsMovedTriggerWindowFrame moved&&moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&p.CardIds.Contains(m.CardId)&&_cardMovements.Contains(m));
 }
 private ProgramSkillFrame? HuangQuanObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.HuangQuanFirstChild(r,c),static(e,i)=>e.HuangQuanDeclarationObserverSuffix(i));
 private bool HasHuangQuanDamageObserver(long id)=>HuangQuanObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool HasHuangQuanBeforeDamageObserver(long id)=>HuangQuanObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 private bool IsHuangQuanDying()=>ActiveDying is{}d&&HuangQuanObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 300)]
 private bool TryAdvanceHuangQuanSubtree()=>_pendingDecision is null&&HuangQuanObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsHuangQuanNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
 {
  if(observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||HuangQuanObserverRoot()is not{}root||root.Id==observer.Id)return false;var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(observer,p):ResolveProgramEffectTarget(observer,e.Target));
 }
 private void AssertHuangQuanNativeUses()
 {foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.JianjiUse is not null))if(_resolutionStack.SingleOrDefault(f=>f.Id==use.JianjiUse!.ProgramFrameId)is not ProgramSkillFrame parent||!JianjiNativeUseRidesOn(use,parent))throw new InvalidOperationException("Jianji native use lost its exact actual draw owner.");}
}
