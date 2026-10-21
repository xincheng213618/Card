namespace CardGame.Core;
internal static class MouJiangJiComposition
{
 [ProgramCatalogValidation(1850)]internal static void Validate(string path,SkillProgram p)
 {
  var ours=p.Id is MouJiangJiRuntime.Shiju or MouJiangJiRuntime.Yingshi;
  if(!ours&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Op is SkillProgramEffectOp.ResolveMouJiangJiShiju or SkillProgramEffectOp.ResolveMouJiangJiYingshi))return;
  var valid=p.Id==MouJiangJiRuntime.Shiju?p.Triggers.Count==0&&p.BooleanStates.Count==0&&p.Activations is[{Id:"give-and-use",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerTurn:null,UsesPerPhase:1}a]&&a.Effects is[{Op:SkillProgramEffectOp.ResolveMouJiangJiShiju,Target:SkillProgramEffectTarget.Owner}]:p.Activations.Count==0&&p.BooleanStates is[{Id:"disabled"}]&&p.Triggers is[{Id:"ordinary-trick-target",Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,OwnerRelation:SkillProgramCardActionOwnerRelation.Actor,Optional:true}t]&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMouJiangJiYingshi,Target:SkillProgramEffectTarget.Owner}];
  if(!ours||!valid||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.CardPolicies.Count!=0)throw new InvalidOperationException(path+": Mou Jiang Ji requires actual per-Play Shiju gifts and selected-target native Yingshi continuations.");
 }
}
