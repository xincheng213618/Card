using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 590)]
    private bool AllowsRegisteredIsXianfuEchoTrigger(ProgramTriggerLegalityContext gate)
    {
        var candidate = gate.Candidate;
        var trigger = gate.Trigger;
        var context = gate.Context;
        return !(IsXianfuEchoTrigger(trigger) && !CanRunXianfuEcho(candidate, context));
    }
 private bool HasXianfuRelation {get{foreach(var p in _players)if(p.XianfuRelations.Count>0)return true;return false;}}
 private IEnumerable<XianfuRelationGrant> LiveXianfuRelations()=>_players.Where(p=>p.IsAlive&&p.XianfuRelations.Count>0).SelectMany(p=>p.XianfuRelations).Where(g=>_players[g.TargetSeat].IsAlive&&HasRuntimeSkillInstance(_players[g.Source.OwnerSeat],g.Source.SkillId,g.Source.SkillInstanceId));
 private SkillProgramStepOutcome ArmXianfuRelation(ProgramSkillFrame supplied,int target)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.WindowContext is not {Window:SkillProgramTriggerWindow.GameStarting} w||f.SelectedTargetSeats is not [var selected]||selected!=target||target==f.OwnerSeat||!_players[target].IsAlive||!_players[f.OwnerSeat].IsAlive||_players[f.OwnerSeat].XianfuRelations.Any(g=>g.Source.SkillInstanceId==f.SkillInstanceId))throw new InvalidOperationException("Xianfu requires its original actual game-start other-character selection.");
  var g=new XianfuRelationGrant(f.Id,XiZhiCaiSource(f),f.GameplayHash,target,w.ParentFrameId);_players[f.OwnerSeat].XianfuRelations=_players[f.OwnerSeat].XianfuRelations.Append(g).ToArray();AdvanceEventRulesAndQueueFact(new XianfuRelationArmedEvent(g));return SkillProgramStepOutcome.Continue;
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 130)]
 private void ObserveXianfuDamageQualification(IGameEvent payload)
 {
  if(payload is not DamageAppliedEvent{Amount:>0} e||!HasXianfuRelation||CurrentDamageAttempt is not {} attack||!attack.DamageWasApplied||attack.TargetSeat!=e.TargetSeat||attack.DamageAmount!=e.Amount)return;
  var native=_resolutionStack.OfType<DamageFrame>().LastOrDefault(d=>d.ParentFrameId==attack.ResolutionId&&d.TargetSeat==e.TargetSeat&&d.Amount==e.Amount)??throw new InvalidOperationException("Xianfu damage lacks its native damage owner.");
  foreach(var g in LiveXianfuRelations().Where(g=>g.TargetSeat==e.TargetSeat).ToArray())
  {
   var receipt=new XianfuEchoQualification(g,XianfuEchoKind.Damage,native.Id,native.ParentFrameId,e.TargetSeat,e.Amount,attack.IsSourceLess||attack.IsDelayedJudgmentDamage?null:e.SourceSeat,null,e.RemainingHp);
   if(!CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Any(x=>x.Receipt.Grant==g&&x.Receipt.NativeChangeId==native.Id))AdvanceEventRulesAndQueueFact(new XianfuEchoQualifiedEvent(receipt));
  }
 }
 private void CaptureXianfuRecoveryQualification(HpChangeContext change)
 {
  if(!HasXianfuRelation||change.Kind!=HpChangeKind.Recovery||change.HpAfter<=change.HpBefore)return;
  foreach(var g in LiveXianfuRelations().Where(g=>g.TargetSeat==change.TargetSeat).ToArray())
   AdvanceEventRulesAndQueueFact(new XianfuEchoQualifiedEvent(new(g,XianfuEchoKind.Recovery,change.Id,change.ParentFrameId??0,change.TargetSeat,change.HpAfter-change.HpBefore,change.SourceSeat,change.HpBefore,change.HpAfter)));
 }
 private bool IsXianfuEchoTrigger(SkillProgramTrigger trigger)=>trigger.Effects is [{Op:SkillProgramEffectOp.SettleXianfuNativeEcho}];
 private XianfuEchoQualification? XianfuQualification(ProgramTriggerCandidate c,long nativeId)=>CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Select(e=>e.Receipt).SingleOrDefault(r=>r.NativeChangeId==nativeId&&r.Grant.Source.OwnerSeat==c.OwnerSeat&&r.Grant.Source.SkillId==c.SkillId&&r.Grant.Source.SkillInstanceId==c.SkillInstanceId);
 private bool XianfuQualifiedSourceEnabled(XianfuEchoQualification r)=>_players[r.Grant.Source.OwnerSeat].IsAlive&&_players[r.Grant.Source.OwnerSeat].XianfuRelations.Contains(r.Grant)&&HasRuntimeSkillInstance(_players[r.Grant.Source.OwnerSeat],r.Grant.Source.SkillId,r.Grant.Source.SkillInstanceId);
 private bool XianfuEchoConsumed(XianfuEchoQualification r)=>CompleteProgramEventHistory().OfType<XianfuEchoIssuedEvent>().Any(e=>e.Receipt.Grant==r.Grant&&e.Receipt.NativeChangeId==r.NativeChangeId);
 private IReadOnlyList<ProgramTriggerCandidate> CollectXianfuHpCandidates(HpChangeContext change)
 {
  if(!HasXianfuRelation||change.Kind!=HpChangeKind.Recovery||!_players.Any(p=>p.IsAlive&&p.XianfuRelations.Any(g=>g.TargetSeat==change.TargetSeat)))return [];
  return _players.Where(p=>p.IsAlive&&p.XianfuRelations.Count>0).SelectMany(p=>p.XianfuRelations).Where(g=>g.TargetSeat==change.TargetSeat).SelectMany(g=>CollectProgramTriggerCandidates(_players[g.Source.OwnerSeat],SkillProgramTriggerWindow.AfterHpRecovered).Where(c=>c.SkillId==g.Source.SkillId&&c.SkillInstanceId==g.Source.SkillInstanceId&&IsXianfuEchoTrigger(GetProgramTrigger(c))&&XianfuQualification(c,change.Id) is {} q&&XianfuQualifiedSourceEnabled(q)&&!XianfuEchoConsumed(q))).Distinct().ToArray();
 }
 private ProgramSkillWindowContext CreateXianfuHpContext(HpChangeContext change,ProgramTriggerCandidate c)
 {var r=XianfuQualification(c,change.Id)??throw new InvalidOperationException("Xianfu lacks its actual recovered-HP qualification.");return new(SkillProgramTriggerWindow.AfterHpRecovered,change.Id,c.OwnerSeat,SourceSeat:change.SourceSeat,TargetSeat:change.TargetSeat,Amount:r.ActualAmount,HpChange:change){XianfuEcho=r};}
 private ProgramSkillWindowContext CreateXianfuDamageContext(DamageTriggerWindowFrame window,ProgramTriggerCandidate c)
 {var r=XianfuQualification(c,window.ParentFrameId)??throw new InvalidOperationException("Xianfu lacks its actual applied-damage qualification.");return new(SkillProgramTriggerWindow.AfterDamageApplied,window.Id,c.OwnerSeat,SourceSeat:r.OriginalSourceSeat,TargetSeat:r.OriginalTargetSeat,DamageFrameId:window.ParentFrameId,Amount:r.ActualAmount){XianfuEcho=r};}
 private bool CanOfferXianfuDamage(int ownerSeat,SkillProgramTrigger trigger,IDamageAttempt attack)
 {if(!IsXianfuEchoTrigger(trigger))return true;return _players[ownerSeat].XianfuRelations.Any(g=>g.Source.OwnerSeat==ownerSeat&&g.TargetSeat==attack.TargetSeat&&CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Any(e=>e.Receipt.Grant==g&&e.Receipt.Kind==XianfuEchoKind.Damage&&e.Receipt.NativeParentFrameId==attack.ResolutionId&&XianfuQualifiedSourceEnabled(e.Receipt)&&!XianfuEchoConsumed(e.Receipt)));}
 private bool CanRunXianfuEcho(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(context.XianfuEcho is not {} r||r.Grant.Source.OwnerSeat!=c.OwnerSeat||r.Grant.Source.SkillId!=c.SkillId||r.Grant.Source.SkillInstanceId!=c.SkillInstanceId||r.ActualAmount<=0||XianfuEchoConsumed(r)||!XianfuQualifiedSourceEnabled(r))return false;
  return r==XianfuQualification(c,r.NativeChangeId)&&(r.Kind==XianfuEchoKind.Damage?context.DamageFrameId==r.NativeChangeId&&context.Window==SkillProgramTriggerWindow.AfterDamageApplied:context.HpChange is {} hp&&context.Window==SkillProgramTriggerWindow.AfterHpRecovered&&hp.Id==r.NativeChangeId&&hp.Kind==HpChangeKind.Recovery&&hp.HpAfter-hp.HpBefore==r.ActualAmount);
 }
 private SkillProgramStepOutcome SettleXianfuNativeEcho(ProgramSkillFrame supplied)
 {
  var f=GetActiveProgramFrame(supplied.Id);var r=f.WindowContext?.XianfuEcho??throw new InvalidOperationException("Xianfu lost its issued native cause.");
  if(XianfuEchoConsumed(r))throw new InvalidOperationException("Xianfu cannot pay the same actual native mutation twice.");
  var nativeAmount=r.Kind==XianfuEchoKind.Damage?r.ActualAmount:Math.Max(0,Math.Min(r.ActualAmount,_players[f.OwnerSeat].MaxHp-_players[f.OwnerSeat].Hp));
  ReplaceRuntimeTop(f=f with{XianfuPending=new(r,f.InstructionIndex,_players[f.OwnerSeat].Hp,true,nativeAmount),PendingMovementContinuation=r.Kind==XianfuEchoKind.Recovery?new(f.OwnerSeat,0,null):null});AdvanceEventRulesAndQueueFact(new XianfuEchoIssuedEvent(f.Id,r,f.InstructionIndex,nativeAmount));
  if(!_players[f.OwnerSeat].IsAlive){FinishXianfuEcho(f);return SkillProgramStepOutcome.AwaitChild;}
  if(r.Kind==XianfuEchoKind.Damage)return BeginProgramSkillDamage(f,f.OwnerSeat,r.ActualAmount,sourceLess:true);
  new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,nativeAmount,null,null);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,23101)]private bool ResumeXianfuEcho(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{XianfuPending:{}} f||f.Id!=id)return false;RequireIssuedXiZhiCai(f);
  if(f.AttackAttempt is not null)return false;
  if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return true;
  FinishXianfuEcho(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,23101)]private bool ReturnXianfuEchoMovement(ProgramSkillFrame f)=>f.XianfuPending is not null&&ResumeXianfuEcho(f.Id);
 private void FinishXianfuEcho(ProgramSkillFrame f)
 {var r=f.XianfuPending!.Receipt;AdvanceEventRulesAndQueueFact(new XianfuEchoCompletedEvent(f.Id,r.NativeChangeId,r.Kind));ReplaceRuntimeTop(f=f with{XianfuPending=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
}
