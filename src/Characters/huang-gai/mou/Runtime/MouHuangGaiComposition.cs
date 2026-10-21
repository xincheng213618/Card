namespace CardGame.Core;
internal static class MouHuangGaiComposition
{
    [ProgramCatalogValidation(1760)]internal static void Validate(string path,SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ArmMouHuangGaiLieji or SkillProgramEffectOp.ResolveMouHuangGaiQuzhou;
        var ours=p.Id is"mou:lieji"or"mou:quzhou";if(!ours&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(Reserved))return;
        if(!ours||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.BooleanStates.Count!=0||p.HpSkillGrants.Count!=0)throw new InvalidOperationException(path+": Huang Gai requires his original complete bindings.");
        var valid=p.Id=="mou:lieji"?p.Activations.Count==0&&p.Triggers is[{Id:"after-trick",Window:SkillProgramTriggerWindow.CardUseCompleted,Optional:true,OwnerRelation:SkillProgramCardActionOwnerRelation.Actor,IncludeResponseUses:true,Effects:[{Op:SkillProgramEffectOp.ArmMouHuangGaiLieji}]}]:
            p.Triggers.Count==0&&p.Activations is[{Id:"reveal-and-sail",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerTurn:null,UsesPerPhase:1,Effects:[{Op:SkillProgramEffectOp.ResolveMouHuangGaiQuzhou}]}];
        if(!valid)throw new InvalidOperationException(path+": Huang Gai changed an actual completed Trick or once-Play reveal source.");
    }
}
