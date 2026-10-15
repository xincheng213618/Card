namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<ZhanjiDrawEntitlement>? CaptureZhanjiDrawEntitlements(long invocationId,int owner,long before)
 {
  if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation))return null;
  var sources=GetSkillBindingShard(_players[owner]).GetInstanceTriggers(SkillProgramTriggerWindow.CardsGained)
   .Where(b=>ProgramInstructionResolver.Default.Features(b.Trigger).HasOperation(SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation))
   .Select(b=>new ZhanjiDrawEntitlement(new(b.SkillId,b.Trigger.Id,owner,b.SkillInstanceId),b.Program.GameplayHash))
   .Distinct().OrderBy(e=>e.Source.SkillId,StringComparer.Ordinal).ThenBy(e=>e.Source.BindingId,StringComparer.Ordinal).ThenBy(e=>e.Source.SkillInstanceId,StringComparer.Ordinal).ToArray();
  AdvanceEventRulesAndQueueFact(new ZhanjiDrawEntitlementsCapturedEvent(invocationId,owner,before,sources.Length));
  foreach(var source in sources)AdvanceEventRulesAndQueueFact(new ZhanjiDrawSourceCapturedEvent(invocationId,owner,before,source));
  return Array.AsReadOnly(sources);
 }
 private bool ValidZhanjiDrawEntitlements(NativeDrawInvocationProof proof)
 {
  if(proof.ZhanjiEntitlements is not{}sources)return !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation);
  var summary=CompleteProgramEventHistory().OfType<ZhanjiDrawEntitlementsCapturedEvent>().Where(e=>e.InvocationId==proof.InvocationId).ToArray();
  if(summary is not[var captured]||captured.OwnerSeat!=proof.OwnerSeat||captured.SequenceBefore!=proof.SequenceBefore||captured.Count!=sources.Count||sources.Distinct().Count()!=sources.Count)return false;
  var facts=CompleteProgramEventHistory().OfType<ZhanjiDrawSourceCapturedEvent>().Where(e=>e.InvocationId==proof.InvocationId).ToArray();
  return facts.Length==sources.Count&&facts.All(e=>e.OwnerSeat==proof.OwnerSeat&&e.SequenceBefore==proof.SequenceBefore)&&sources.All(s=>s.Source.OwnerSeat==proof.OwnerSeat&&_contentRegistry.GetSkill(s.Source.SkillId).Program is{}p&&p.GameplayHash==s.GameplayHash&&p.Triggers.Any(t=>t.Id==s.Source.BindingId&&ProgramInstructionResolver.Default.Features(t).HasOperation(SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation))&&facts.Count(e=>e.Entitlement==s)==1);
 }
 private static bool HasZhanjiDrawEntitlement(NativeDrawInvocationProof proof,int owner,string skill,string binding,string instance,string hash)=>proof.ZhanjiEntitlements?.Contains(new(new(skill,binding,owner,instance),hash))==true;
}
