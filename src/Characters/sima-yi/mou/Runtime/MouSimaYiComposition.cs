namespace CardGame.Core;
internal static class MouSimaYiComposition
{
    internal const string Quanmou="mou:quanmou",Pingliao="mou:pingliao";
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.InitializeMouSimaYiQuanmou or SkillProgramEffectOp.ResolveMouSimaYiQuanmou or SkillProgramEffectOp.ResolveMouSimaYiPingliao or SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage or SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage;
    internal static bool Private(SkillProgramTrigger t)=>t.Id=="private-native-slash"&&t.Window==SkillProgramTriggerWindow.CardUseCommitted&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&!t.Optional&&!t.IncludeResponseUses&&!t.SingleActionInstance&&t.CardKinds.SequenceEqual(new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash})&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMouSimaYiPingliao,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}];
    [ProgramCatalogValidation(1840)]internal static void Validate(string path,SkillProgram p)
    {
        if(p.Id is not(Quanmou or Pingliao)&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(e=>Handles(e.Op)))return;
        bool T(SkillProgramTrigger t,string id,SkillProgramTriggerWindow w,SkillProgramTriggerSubject subject,SkillProgramEffectOp op)=>t.Id==id&&t.Window==w&&t.Subject==subject&&!t.Optional&&t.Effects is[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]&&e.Op==op;
        if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.BooleanStates.Count!=0||p.DamageModifiers.Count!=0||
            (p.Id==Pingliao?p.Activations.Count!=0||p.Triggers is not[var trigger]||!Private(trigger):p.Id!=Quanmou||p.Activations is not[var a]||a.Id!="in-range-give"||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=0||a.MaxTargets!=0||a.UsesPerTurn is not null||a.Effects is not[{Op:SkillProgramEffectOp.ResolveMouSimaYiQuanmou,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||p.Triggers.Count!=3||!T(p.Triggers[0],"initial-polarity",SkillProgramTriggerWindow.GameStarting,SkillProgramTriggerSubject.Owner,SkillProgramEffectOp.InitializeMouSimaYiQuanmou)||!T(p.Triggers[1],"prevent-next-phase-damage",SkillProgramTriggerWindow.BeforeDamageApplied,SkillProgramTriggerSubject.DamageSource,SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage)||!T(p.Triggers[2],"spread-next-phase-damage",SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramTriggerSubject.DamageSource,SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage)))throw new InvalidOperationException(path+": Mou Sima Yi requires original private Slash, actual per-target Play gift and exact next-damage polarity.");
    }
}
