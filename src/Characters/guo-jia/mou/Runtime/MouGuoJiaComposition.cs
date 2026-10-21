namespace CardGame.Core;
internal static class MouGuoJiaComposition
{
 [ProgramCatalogValidation(1930)]internal static void Validate(string path,SkillProgram p)
 {
  var own=p.Id is MouGuoJiaRuntime.Xianmou or MouGuoJiaRuntime.Lunshi or MouGuoJiaRuntime.Yiji;
  if(!own&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Op is SkillProgramEffectOp.InitializeMouGuoJiaXianmou or SkillProgramEffectOp.ResolveMouGuoJiaXianmou or SkillProgramEffectOp.ResolveMouGuoJiaYiji))return;
  bool Effect(SkillProgramTrigger t,SkillProgramEffectOp op)=>t.Effects is[var e]&&e.Op==op&&e.Target==SkillProgramEffectTarget.Owner;
  var valid=p.Id==MouGuoJiaRuntime.Lunshi?p.Triggers.Count==0&&p.BooleanStates.Count==0&&p.ViewAs is[{Id:"balanced-hand-nullification",InputCount:1,OutputKind:CardKind.Nullification,ForPlay:false,ForResponse:true}v]&&v.SourceZones.SequenceEqual(new[]{CardZoneKind.Hand}):p.Id==MouGuoJiaRuntime.Yiji?p.BooleanStates.Count==0&&p.ViewAs.Count==0&&p.Triggers is[{Id:"per-damage-point",Window:SkillProgramTriggerWindow.AfterDamageApplied,Subject:SkillProgramTriggerSubject.Owner,DamageOccurrence:SkillProgramDamageOccurrence.PerDamagePoint,Optional:true}y]&&Effect(y,SkillProgramEffectOp.ResolveMouGuoJiaYiji):p.Id==MouGuoJiaRuntime.Xianmou&&p.ViewAs.Count==0&&p.BooleanStates is[{Id:"yin",InitialValue:false}]&&p.Triggers is[{Id:"opening-state",Window:SkillProgramTriggerWindow.GameStarting,Optional:false}open,{Id:"own-ended-loss",Window:SkillProgramTriggerWindow.AfterTurnEnded,TurnOwnerScope:SkillProgramTurnOwnerScope.Own,Optional:true}ownEnd,{Id:"other-ended-loss",Window:SkillProgramTriggerWindow.AfterTurnEnded,TurnOwnerScope:SkillProgramTurnOwnerScope.OtherLiving,Optional:true}otherEnd]&&Effect(open,SkillProgramEffectOp.InitializeMouGuoJiaXianmou)&&Effect(ownEnd,SkillProgramEffectOp.ResolveMouGuoJiaXianmou)&&Effect(otherEnd,SkillProgramEffectOp.ResolveMouGuoJiaXianmou);
  if(!own||!valid||p.Activations.Count!=0||p.CardPolicies.Count!=0||p.DamageModifiers.Count!=0||p.Modifiers.Count!=0)throw new InvalidOperationException(path+": Mou Guo Jia requires actual end-loss conversion, independently sourced current Yiji and one-Hand qualified native Nullification.");
 }
}
