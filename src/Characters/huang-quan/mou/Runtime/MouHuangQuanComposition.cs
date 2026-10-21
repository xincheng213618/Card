namespace CardGame.Core;
internal static class MouHuangQuanComposition
{
    [ProgramCatalogValidation(1790)]internal static void Validate(string path,SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveMouHuangQuanQiaodui or SkillProgramEffectOp.ResolveMouHuangQuanTuicheng;
        var ours=p.Id is "mou:qiaodui" or "mou:tuicheng";
        if(!ours&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(Reserved))return;
        if(!ours||p.Activations.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0)throw new InvalidOperationException(path+": Huang Quan requires his original trigger bindings.");
        var valid=p.Id=="mou:qiaodui"?p.Triggers is[{Id:"give-for-original-effect",Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,OwnerRelation:SkillProgramCardActionOwnerRelation.Observer,SingleActionInstance:false,IncludeResponseUses:false,Optional:false,Effects:[{Op:SkillProgramEffectOp.ResolveMouHuangQuanQiaodui}]}]:
            p.Triggers is[{Id:"after-native-give",Window:SkillProgramTriggerWindow.CardsMoved,SourceZones:[],MovementOccurrence:SkillProgramMovementOccurrence.PerBatch,Optional:false,Effects:[{Op:SkillProgramEffectOp.ResolveMouHuangQuanTuicheng}]}];
        if(!valid)throw new InvalidOperationException(path+": Huang Quan changed his original-use or real-give trigger.");
    }
}
