namespace CardGame.Core;
internal static class MouJuShouComposition
{
 [ProgramCatalogValidation(1910)]internal static void Validate(string path,SkillProgram p)
 {
  var ours=p.Id is MouJuShouRuntime.ZuoJun or MouJuShouRuntime.Muwang;
  if(!ours&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Op is SkillProgramEffectOp.ResolveMouJuShouZuoJun or SkillProgramEffectOp.ResolveMouJuShouMuwang))return;
  var shape=p.Id==MouJuShouRuntime.ZuoJun?p.Triggers.Count==0&&p.Activations is[{Id:"draw-and-choose",MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,UsesPerTurn:null,UsesPerPhase:1}a]&&a.Effects is[{Op:SkillProgramEffectOp.ResolveMouJuShouZuoJun,Target:SkillProgramEffectTarget.Owner}]:p.Activations.Count==0&&p.Triggers is[{Id:"first-loss",Window:SkillProgramTriggerWindow.CardsMoved,Subject:SkillProgramTriggerSubject.Owner,Optional:false,MovementOccurrence:SkillProgramMovementOccurrence.PerOwnerBatch}t]&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMouJuShouMuwang,Target:SkillProgramEffectTarget.Owner}];
  if(!ours||!shape||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.CardPolicies.Count!=0||p.BooleanStates.Count!=0)throw new InvalidOperationException(path+": Mou Ju Shou requires a real recipient draw/use continuation and exact native first-loss batches.");
 }
}
