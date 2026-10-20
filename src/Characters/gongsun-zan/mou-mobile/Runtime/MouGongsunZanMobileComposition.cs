namespace CardGame.Core;
internal static class MouGongsunZanMobileComposition
{
    internal const string Yicong="mou-mobile:yicong",Qiaomeng="mou-mobile:qiaomeng";
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.InitializeMouGongsunZanMobile or SkillProgramEffectOp.ResolveMouGongsunZanYicong or SkillProgramEffectOp.ResolveMouGongsunZanQiaomeng or SkillProgramEffectOp.ExpireMouGongsunZanRound;
    [ProgramCatalogValidation(1580)]
    internal static void Validate(string path,SkillProgram p)
    {
        if(p.Id is not(Yicong or Qiaomeng)&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Handles(e.Op)))return;
        bool T(SkillProgramTrigger t,string id,SkillProgramTriggerWindow w,SkillProgramEffectOp op,bool optional)=>t.Id==id&&t.Window==w&&t.Optional==optional&&t.Subject==(w==SkillProgramTriggerWindow.AfterDamageApplied?SkillProgramTriggerSubject.DamageSource:SkillProgramTriggerSubject.Owner)&&t.Effects is [var e]&&e.Op==op&&e.Target==SkillProgramEffectTarget.Owner&&e.ActorReference is null&&e.TargetReference is null&&e.Condition.Kind==SkillProgramConditionKind.Always;
        var valid=p.Activations.Count==0&&p.ViewAs.Count==0&&p.CardPolicies.Count==0&&p.Modifiers.Count==0;
        if(p.Id==Yicong)valid&=p.Triggers is [var a,var b,var e]&&T(a,"initial-charge",SkillProgramTriggerWindow.GameStarting,SkillProgramEffectOp.InitializeMouGongsunZanMobile,false)&&T(b,"round-charge-and-hu",SkillProgramTriggerWindow.RoundStarting,SkillProgramEffectOp.ResolveMouGongsunZanYicong,true)&&T(e,"round-fence",SkillProgramTriggerWindow.RoundEnding,SkillProgramEffectOp.ExpireMouGongsunZanRound,false);
        else valid&=p.Id==Qiaomeng&&p.Triggers is [var t]&&T(t,"slash-damage-reward",SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramEffectOp.ResolveMouGongsunZanQiaomeng,true)&&t.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage;
        if(!valid)throw new InvalidOperationException(path+": mobile Gongsun Zan requires the complete native charge/Hu and Slash damage reward bindings.");
    }
}
