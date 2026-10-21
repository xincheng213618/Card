namespace CardGame.Core;
internal static class MouXunYuAnniversaryComposition
{
 [ProgramCatalogValidation(1940)]internal static void Validate(string path,SkillProgram p)
 {
  static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.InitializeMouXunYuAnniversaryShimou or SkillProgramEffectOp.ResolveMouXunYuAnniversaryShimou or SkillProgramEffectOp.ResolveMouXunYuAnniversaryBizuo or SkillProgramEffectOp.SettleMouXunYuAnniversaryBizuo;
  var own=p.Id is MouXunYuAnniversaryRuntime.Bizuo or MouXunYuAnniversaryRuntime.Shimou;if(!own&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(Reserved))return;
  var valid=p.Id==MouXunYuAnniversaryRuntime.Shimou?p.BooleanStates is[{Id:"yin"}]&&p.Triggers is[{Id:"opening-state",Window:SkillProgramTriggerWindow.GameStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:false}t]&&t.Effects is[{Op:SkillProgramEffectOp.InitializeMouXunYuAnniversaryShimou}]&&p.Activations is[{Id:"adjust-and-command",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerPhase:1,UsesPerTurn:null}a]&&a.Effects is[{Op:SkillProgramEffectOp.ResolveMouXunYuAnniversaryShimou}]:p.BooleanStates.Count==0&&p.Activations.Count==0&&p.Triggers.Count==4&&p.Triggers.Count(t=>t.Optional&&t.UsageScope==SkillUsageScope.Game&&t.UsageLimit==1&&t.NamedUsageGroup=="bizuo-limited"&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMouXunYuAnniversaryBizuo}])==2&&p.Triggers.Count(t=>!t.Optional&&t.Effects is[{Op:SkillProgramEffectOp.SettleMouXunYuAnniversaryBizuo}])==2&&p.Triggers.All(t=>t.Window==SkillProgramTriggerWindow.AfterTurnEnded&&t.Subject==SkillProgramTriggerSubject.Owner);
  if(!own||!valid||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.DamageModifiers.Count!=0)throw new InvalidOperationException(path+": Mou Xun Yu requires native extreme-Hand adjustment, real foreign-actor zero-material ordinary Trick and issued extra-actual-turn Bizuo.");
 }
}
