namespace CardGame.Core;

internal static class MouKongRongComposition
{
    [ProgramCatalogValidation(1680)]
    internal static void Validate(string path,SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveMouKongRongLiwen or SkillProgramEffectOp.ResolveMouKongRongZhengyi or SkillProgramEffectOp.PayMouKongRongZhengyi;
        var ours=p.Id is MouKongRongRuntime.Liwen or MouKongRongRuntime.Zhengyi;
        if(!ours&&!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(Reserved))return;
        if(!ours||p.Activations.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.BooleanStates.Count!=0||
            p.Triggers.Any(t=>t.Optional||t.Effects.Any(e=>e.Target!=SkillProgramEffectTarget.Owner)))throw new InvalidOperationException(path+": Kong Rong requires its complete native lifecycle bindings.");
        if(p.Id==MouKongRongRuntime.Liwen)
        {
            if(p.Triggers.Count!=5||p.Triggers.Any(t=>t.Effects is not[{Op:SkillProgramEffectOp.ResolveMouKongRongLiwen}])||
                !p.Triggers.Select(t=>t.Window).Order().SequenceEqual(new[]{SkillProgramTriggerWindow.GameStarting,SkillProgramTriggerWindow.CardUseCompleted,SkillProgramTriggerWindow.CardResponseCompleted,SkillProgramTriggerWindow.AfterTurnEnded,SkillProgramTriggerWindow.AfterTurnEnded}.Order())||
                !p.Triggers.Where(t=>t.Window==SkillProgramTriggerWindow.AfterTurnEnded).Select(t=>t.TurnOwnerScope).Order().SequenceEqual(new[]{SkillProgramTurnOwnerScope.Own,SkillProgramTurnOwnerScope.OtherLiving}.Order()))
                throw new InvalidOperationException(path+": Liwen must retain actual start, Use, Played and every actual turn end.");
        }
        else if(p.Triggers is not[{Window:SkillProgramTriggerWindow.BeforeDamageApplied,Subject:SkillProgramTriggerSubject.Any,Effects:[{Op:SkillProgramEffectOp.ResolveMouKongRongZhengyi},{Op:SkillProgramEffectOp.PayMouKongRongZhengyi}]}])
            throw new InvalidOperationException(path+": Zhengyi must freeze consent, prevent the original damage, then pay real HP losses.");
    }
}
