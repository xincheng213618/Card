namespace CardGame.Core;
internal static class MouJiaXuComposition
{
 [ProgramCatalogValidation(1870)]internal static void Validate(string path,SkillProgram p)
 {
  static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.InitializeMouJiaXuFumou or SkillProgramEffectOp.ResolveMouJiaXuFumou or SkillProgramEffectOp.RecordMouJiaXuSushen or SkillProgramEffectOp.RestoreMouJiaXuRushi;
  var ours=p.Id is MouJiaXuRuntime.Sushen or MouJiaXuRuntime.Fumou or MouJiaXuRuntime.Rushi;if(!ours&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(Reserved))return;
  var valid=p.Id==MouJiaXuRuntime.Fumou?p.BooleanStates is[{Id:"yin"}]&&p.Triggers is[{Id:"opening-state",Window:SkillProgramTriggerWindow.GameStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:false}t]&&t.Effects is[{Op:SkillProgramEffectOp.InitializeMouJiaXuFumou}]&&p.Activations is[{Id:"view-show-and-use",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerTurn:null,UsesPerPhase:1}a]&&a.Effects is[{Op:SkillProgramEffectOp.ResolveMouJiaXuFumou}]:p.BooleanStates.Count==0&&p.Triggers.Count==0&&p.Activations is[{MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerTurn:null,UsesPerGame:1}limited]&&(p.Id==MouJiaXuRuntime.Sushen?limited.Id=="record-and-enter"&&limited.Effects is[{Op:SkillProgramEffectOp.RecordMouJiaXuSushen}]:limited.Id=="restore-recorded-values"&&limited.Effects is[{Op:SkillProgramEffectOp.RestoreMouJiaXuRushi}]);
  if(!ours||!valid||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.DamageModifiers.Count!=0)throw new InvalidOperationException(path+": Mou Jia Xu requires opening conversion, private shown Hand, real recorded Sushen and derived limited Rushi.");
 }
}
