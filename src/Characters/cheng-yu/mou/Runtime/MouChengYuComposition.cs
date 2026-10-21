namespace CardGame.Core;
internal static class MouChengYuAnniversaryComposition
{
 internal static bool Committed(SkillProgramTrigger t)=>t.Id=="consume-name"&&t.Window==SkillProgramTriggerWindow.CardUseCommitted&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Observer&&t.IncludeResponseUses&&!t.Optional&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.Effects is[{Op:SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]&&t.CardKinds.Count==0&&t.CardCategories.Count==0&&t.UsageScope is null&&t.UsageLimit is null&&t.MarkerCost is null&&t.Subject is null;
 [ProgramCatalogValidation(1900)]internal static void Validate(string path,SkillProgram p)
 {
  static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveMouChengYuAnniversaryShizha or SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha or SkillProgramEffectOp.ResolveMouChengYuAnniversaryGaojian;
  var ours=p.Id is MouChengYuAnniversaryRuntime.Shizha or MouChengYuAnniversaryRuntime.Gaojian;if(!ours&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(Reserved))return;
  var valid=p.Id==MouChengYuAnniversaryRuntime.Shizha?p.Activations.Count==0&&p.Triggers is[{Id:"hp-change",Window:SkillProgramTriggerWindow.AfterHealthChanged,Subject:SkillProgramTriggerSubject.Owner,Optional:true}hp,{Id:"consume-name",Window:SkillProgramTriggerWindow.CardUseCommitted,OwnerRelation:SkillProgramCardActionOwnerRelation.Observer,IncludeResponseUses:true,Optional:false}use]&&hp.UsageLimit==1&&hp.Effects is[{Op:SkillProgramEffectOp.ResolveMouChengYuAnniversaryShizha}]&&use.Effects is[{Op:SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha}]:p.Triggers.Count==0&&p.Activations is[{Id:"reveal-and-advise",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerPhase:1,UsesPerTurn:null}a]&&a.Effects is[{Op:SkillProgramEffectOp.ResolveMouChengYuAnniversaryGaojian}];
  if(!ours||!valid||p.BooleanStates.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.DamageModifiers.Count!=0)throw new InvalidOperationException(path+": Mou Cheng Yu requires actual HP private deck selection, consumed true-use names and native reveal/fire damage.");
 }
}
internal sealed class MouChengYuAnniversaryCommittedContract:IProgramCommittedUseContract
{
 public string SkillId=>MouChengYuAnniversaryRuntime.Shizha;
 public void ValidateTrigger(SkillProgramTrigger trigger){if(!MouChengYuAnniversaryComposition.Committed(trigger))throw new InvalidOperationException("Shizha requires its mandatory observer of exact true committed Uses, including native response Uses, before its own optional ineffective choice.");}
}
