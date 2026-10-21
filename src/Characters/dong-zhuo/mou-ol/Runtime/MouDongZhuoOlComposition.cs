namespace CardGame.Core;
internal static class MouDongZhuoOlComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.InitializeMouDongZhuoGuanbian or SkillProgramEffectOp.RemoveMouDongZhuoGuanbian or
        SkillProgramEffectOp.ResolveMouDongZhuoXiongni or SkillProgramEffectOp.ResolveMouDongZhuoFengshang or SkillProgramEffectOp.ResolveMouDongZhuoZhibing;
    [ProgramCatalogValidation(1690)]
    internal static void Validate(string path,SkillProgram p)
    {
        var own=p.Id is MouDongZhuoOlRuntime.Guanbian or MouDongZhuoOlRuntime.Xiongni or MouDongZhuoOlRuntime.Fengshang or MouDongZhuoOlRuntime.Zhibing;
        if(!own&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(e=>Handles(e.Op)))return;
        bool Effect(IReadOnlyList<SkillProgramEffect> e,SkillProgramEffectOp op)=>e is[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}x]&&x.Op==op;
        var valid=own&&p.ViewAs.Count==0&&p.CardPolicies.Count==0&&p.BooleanStates.Count==0&&p.DamageModifiers.Count==0&&p.HpSkillGrants.Count==0;
        valid&=p.Id switch {
            MouDongZhuoOlRuntime.Guanbian=>p.Activations.Count==0&&p.Triggers.Count==2&&p.Modifiers.Count==3&&
                p.Triggers[0].Id=="freeze-game-population"&&p.Triggers[0].Window==SkillProgramTriggerWindow.GameStarting&&!p.Triggers[0].Optional&&Effect(p.Triggers[0].Effects,SkillProgramEffectOp.InitializeMouDongZhuoGuanbian)&&
                p.Triggers[1].Id=="first-round-loss"&&p.Triggers[1].Window==SkillProgramTriggerWindow.RoundEnding&&!p.Triggers[1].Optional&&Effect(p.Triggers[1].Effects,SkillProgramEffectOp.RemoveMouDongZhuoGuanbian)&&
                p.Modifiers.All(m=>m.Operation==SkillRuleOperation.Add&&m.ValueExpression==SkillRuleValueExpression.OwnerMarkerCount&&m.ValueMarker==PlayerMarkerKind.MouDongZhuoGuanbian)&&
                p.Modifiers.Select(m=>m.Query).Order().SequenceEqual(new[]{SkillRuleQuery.HandLimit,SkillRuleQuery.IncomingDistance,SkillRuleQuery.OutgoingDistance}.Order()),
            MouDongZhuoOlRuntime.Xiongni=>p.Activations.Count==0&&p.Modifiers.Count==0&&p.Triggers is[{Id:"same-suit-walk",Window:SkillProgramTriggerWindow.PlayPhaseStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:true}t]&&Effect(t.Effects,SkillProgramEffectOp.ResolveMouDongZhuoXiongni),
            MouDongZhuoOlRuntime.Fengshang=>p.Modifiers.Count==0&&p.Triggers is[{Id:"dying-distribute-pair",Window:SkillProgramTriggerWindow.DyingEntering,Subject:SkillProgramTriggerSubject.Any,Optional:true}t]&&Effect(t.Effects,SkillProgramEffectOp.ResolveMouDongZhuoFengshang)&&
                p.Activations is[{Id:"distribute-pair",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerTurn:null,UsesPerPhase:1}a]&&Effect(a.Effects,SkillProgramEffectOp.ResolveMouDongZhuoFengshang),
            _=>p.Activations.Count==0&&p.Modifiers.Count==0&&p.Triggers is[{Id:"cumulative-black-thresholds",Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,Subject:SkillProgramTriggerSubject.Owner,Optional:false}t]&&Effect(t.Effects,SkillProgramEffectOp.ResolveMouDongZhuoZhibing)};
        if(!valid)throw new InvalidOperationException(path+": Mou Dong Zhuo requires his full native population, actual discard-pair and repeatable Preparing bindings.");
    }
}
