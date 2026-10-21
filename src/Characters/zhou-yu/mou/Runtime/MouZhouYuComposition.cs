namespace CardGame.Core;
internal static class MouZhouYuComposition
{
 [ProgramCatalogValidation(1820)] internal static void Validate(string path,SkillProgram p)
 {
  var ours=p.Id is MouZhouYuRuntime.Ronghuo or MouZhouYuRuntime.Yingmou;
  var operations=p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects));
  if(!ours&&!operations.Any(e=>e.Op is SkillProgramEffectOp.InitializeMouZhouYuYingmou or SkillProgramEffectOp.ResolveMouZhouYuYingmou or SkillProgramEffectOp.IssueMouZhouYuRonghuo))return;
  var valid=p.Id==MouZhouYuRuntime.Ronghuo?p.Triggers is[var fire]&&fire.Window==SkillProgramTriggerWindow.AfterDamageApplied&&fire.Subject==SkillProgramTriggerSubject.Any&&fire.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage&&!fire.Optional&&fire.Effects is[{Op:SkillProgramEffectOp.IssueMouZhouYuRonghuo}]:p.Triggers is [var opening,var use]&&opening.Window==SkillProgramTriggerWindow.GameStarting&&opening.Subject==SkillProgramTriggerSubject.Owner&&!opening.Optional&&opening.Effects is[{Op:SkillProgramEffectOp.InitializeMouZhouYuYingmou}]&&use.Window==SkillProgramTriggerWindow.CardUseCompleted&&use.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&use.Optional&&use.Effects is[{Op:SkillProgramEffectOp.ResolveMouZhouYuYingmou}];
  if(!ours||!valid||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.CardPolicies.Count!=0)throw new InvalidOperationException(path+": Mou Zhou Yu requires current additive Ronghuo and native completed-Use Yingmou.");
 }
}
