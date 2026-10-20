using System.Globalization;
using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 600)]
    private bool AllowsRegisteredIsDianhuDrawTrigger(ProgramTriggerLegalityContext gate)
    {
        var candidate = gate.Candidate;
        var trigger = gate.Trigger;
        var context = gate.Context;
        return !(IsDianhuDrawTrigger(trigger) && !CanRunDianhu(candidate, context));
    }
 private bool HasDianhuRelation{get{foreach(var p in _players)if(p.DianhuRelations.Count!=0)return true;return false;}}
 private bool DianhuSourceEnabled(DianhuRelation g)=>_players[g.Source.OwnerSeat].IsAlive&&_players[g.Source.OwnerSeat].DianhuRelations.Contains(g)&&HasRuntimeSkillInstance(_players[g.Source.OwnerSeat],g.Source.SkillId,g.Source.SkillInstanceId)&&_contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash==g.GameplayHash;
 private IEnumerable<DianhuRelation> LiveDianhuRelations()=>_players.Where(p=>p.IsAlive&&p.DianhuRelations.Count>0).SelectMany(p=>p.DianhuRelations).Where(g=>_players[g.TargetSeat].IsAlive&&DianhuSourceEnabled(g));
 private bool IsDianhuDrawTrigger(SkillProgramTrigger t)=>t.Effects is [{Op:SkillProgramEffectOp.DrawForDianhuActualOccurrence}];
 private DianhuOccurrence? DianhuQualification(ProgramTriggerCandidate c,long nativeId)=>CompleteProgramEventHistory().OfType<DianhuQualifiedEvent>().Select(e=>e.Receipt).SingleOrDefault(r=>r.NativeChangeId==nativeId&&r.Relation.Source.OwnerSeat==c.OwnerSeat&&r.Relation.Source.SkillId==c.SkillId&&r.Relation.Source.SkillInstanceId==c.SkillInstanceId);
 private bool DianhuConsumed(DianhuOccurrence r)=>CompleteProgramEventHistory().OfType<HuangQuanDrawPaidEvent>().Any(e=>e.Occurrence==r);
 private SkillProgramStepOutcome ArmDianhuRelation(ProgramSkillFrame supplied,int target)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.WindowContext is not{Window:SkillProgramTriggerWindow.GameStarting}w||f.SelectedTargetSeats is not[var selected]||target!=selected||target==f.OwnerSeat||!_players[target].IsAlive||_players[f.OwnerSeat].DianhuRelations.Any(g=>g.Source.SkillInstanceId==f.SkillInstanceId))throw new InvalidOperationException("Dianhu requires its actual game-start other-living choice exactly once.");
  var grant=new DianhuRelation(f.Id,HuangQuanSource(f),f.GameplayHash,target,w.ParentFrameId);_players[f.OwnerSeat].DianhuRelations=Array.AsReadOnly(_players[f.OwnerSeat].DianhuRelations.Append(grant).ToArray());AdvanceEventRulesAndQueueFact(new DianhuArmedEvent(grant));return SkillProgramStepOutcome.Continue;
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 140)]
 private void ObserveDianhuDamageQualification(IGameEvent payload)
 {
  if(payload is not DamageAppliedEvent e||!HasDianhuRelation||e.SourceLess||e.Amount<=0||CurrentDamageAttempt is not{}attack||attack.IsSourceLess||attack.IsDelayedJudgmentDamage||!attack.DamageWasApplied||attack.TargetSeat!=e.TargetSeat||attack.SourceSeat!=e.SourceSeat)return;
  var native=_resolutionStack.OfType<DamageFrame>().LastOrDefault();if(native is null||native.ParentFrameId!=attack.ResolutionId||native.TargetSeat!=e.TargetSeat||native.Amount!=e.Amount)return;
  foreach(var g in LiveDianhuRelations().Where(g=>g.TargetSeat==e.TargetSeat&&g.Source.OwnerSeat==e.SourceSeat).ToArray())
  {var r=new DianhuOccurrence(g,false,native.Id,attack.ResolutionId,e.TargetSeat,e.Amount,e.SourceSeat);if(!CompleteProgramEventHistory().OfType<DianhuQualifiedEvent>().Any(q=>q.Receipt==r))AdvanceEventRulesAndQueueFact(new DianhuQualifiedEvent(r));}
 }
 private void CaptureDianhuRecoveryQualification(HpChangeContext change)
 {
  if(change.Kind!=HpChangeKind.Recovery||change.HpAfter<=change.HpBefore||!HasDianhuRelation)return;
  foreach(var g in LiveDianhuRelations().Where(g=>g.TargetSeat==change.TargetSeat).ToArray())AdvanceEventRulesAndQueueFact(new DianhuQualifiedEvent(new(g,true,change.Id,change.ParentFrameId??0,change.TargetSeat,change.HpAfter-change.HpBefore,change.SourceSeat)));
 }
 private IReadOnlyList<ProgramTriggerCandidate> CollectDianhuHpCandidates(HpChangeContext change)
 {
  if(change.Kind!=HpChangeKind.Recovery||!HasDianhuRelation)return [];
  return _players.Where(p=>p.IsAlive&&p.DianhuRelations.Count>0).SelectMany(p=>p.DianhuRelations).Where(g=>g.TargetSeat==change.TargetSeat&&DianhuSourceEnabled(g)).SelectMany(g=>CollectProgramTriggerCandidates(_players[g.Source.OwnerSeat],SkillProgramTriggerWindow.AfterHpRecovered).Where(c=>c.SkillId==g.Source.SkillId&&c.SkillInstanceId==g.Source.SkillInstanceId&&IsDianhuDrawTrigger(GetProgramTrigger(c))&&DianhuQualification(c,change.Id)is{}r&&!DianhuConsumed(r))).Distinct().ToArray();
 }
 private ProgramSkillWindowContext CreateDianhuHpContext(HpChangeContext change,ProgramTriggerCandidate c)
 {var r=DianhuQualification(c,change.Id)??throw new InvalidOperationException("Dianhu requires its actual recovered HP occurrence.");return new(SkillProgramTriggerWindow.AfterHpRecovered,change.Id,c.OwnerSeat,SourceSeat:change.SourceSeat,TargetSeat:change.TargetSeat,Amount:change.Amount,HpChange:change){Dianhu=r};}
 private ProgramSkillWindowContext CreateDianhuDamageContext(DamageTriggerWindowFrame w,ProgramTriggerCandidate c)
 {var r=DianhuQualification(c,w.ParentFrameId)??throw new InvalidOperationException("Dianhu requires its actual owner-caused damage occurrence.");return new(SkillProgramTriggerWindow.AfterDamageApplied,w.Id,c.OwnerSeat,SourceSeat:r.SourceSeat,TargetSeat:r.TargetSeat,DamageFrameId:w.ParentFrameId,Amount:r.ActualAmount){Dianhu=r};}
 private bool CanOfferDianhuDamage(int owner,SkillProgramTrigger t,IDamageAttempt attack)=>!IsDianhuDrawTrigger(t)||!attack.IsSourceLess&&!attack.IsDelayedJudgmentDamage&&attack.SourceSeat==owner&&_players[owner].DianhuRelations.Any(g=>g.TargetSeat==attack.TargetSeat&&DianhuSourceEnabled(g)&&CompleteProgramEventHistory().OfType<DianhuQualifiedEvent>().Any(e=>e.Receipt.Relation==g&&e.Receipt.NativeParentId==attack.ResolutionId&&!e.Receipt.Recovery&&!DianhuConsumed(e.Receipt)));
 private bool CanRunDianhu(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {if(context.Dianhu is not{}r||r.Relation.Source.OwnerSeat!=c.OwnerSeat||r.Relation.Source.SkillId!=c.SkillId||r.Relation.Source.SkillInstanceId!=c.SkillInstanceId||!DianhuSourceEnabled(r.Relation)||DianhuConsumed(r)||r!=DianhuQualification(c,r.NativeChangeId))return false;return r.Recovery?context.HpChange is{Kind:HpChangeKind.Recovery}hp&&hp.Id==r.NativeChangeId&&hp.HpAfter-hp.HpBefore==r.ActualAmount:context.DamageFrameId==r.NativeChangeId&&context.SourceSeat==c.OwnerSeat&&context.Amount==r.ActualAmount;}
 private SkillProgramStepOutcome DrawForDianhuActualOccurrence(ProgramSkillFrame supplied)
 {var f=GetActiveProgramFrame(supplied.Id);var r=f.WindowContext?.Dianhu??throw new InvalidOperationException("Dianhu lost its native occurrence.");if(DianhuConsumed(r))throw new InvalidOperationException("Dianhu cannot draw twice for the same actual event.");return IssueHuangQuanDraw(f,f.OwnerSeat,"dianhu",r);}
}
