namespace CardGame.Core;
internal static class MouZhangHeComposition
{
    [ProgramCatalogValidation(1640)]
    internal static void Validate(string path,SkillProgram p)
    {
        static bool Op(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.PrepareMouZhangHeQiaobian or SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase or SkillProgramEffectOp.ResolveMouZhangHeEnding;
        if(p.Id is not(MouQiaobianRuntime.Qiaobian or MouQiaobianRuntime.Liaoshi))
        {
            if(p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Op(e.Op)))
                throw new InvalidOperationException(path+": Qiaobian operations require their complete actual-phase native bindings.");
            return;
        }
        if(p.Activations.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.CardPolicies.Count!=0||p.BooleanStates.Count!=0||p.HpSkillGrants.Count!=0||p.Contributions.Count!=0||p.CardIdentities.Count!=0)
            throw new InvalidOperationException(path+": Mou Zhang He must retain its complete phase-skip and obtained-entity conversion contract.");
        if(p.Id==MouQiaobianRuntime.Liaoshi)
        {
            if(p.Triggers.Count!=0||p.ViewAs.Count!=2||!p.ViewAs.Select(v=>v.OutputKind).Order().SequenceEqual(new[]{CardKind.Slash,CardKind.Dodge}.Order())||
                p.ViewAs.Any(v=>v.InputCount!=1||v.InputKinds.Count!=0||v.InputSuits.Count!=0||!v.SourceZones.SequenceEqual(new[]{CardZoneKind.Hand})||
                    v.ForPlay!=(v.OutputKind==CardKind.Slash)||!v.ForResponse))
                throw new InvalidOperationException(path+": Liaoshi requires both actual one-Hand-card Slash and Dodge paths.");
            return;
        }
        if(p.ViewAs.Count!=0||p.Triggers.Count!=3)throw new InvalidOperationException(path+": Qiaobian needs its Preparation, actual skipped phase and automatic Ending bindings.");
        foreach(var t in p.Triggers)
        {
            var pair=t.Id switch
            {
                "plan-skips"=>(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramEffectOp.PrepareMouZhangHeQiaobian,true),
                "actual-skipped-phase"=>(SkillProgramTriggerWindow.ActualPhaseSkipped,SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase,false),
                "ending-extra-play"=>(SkillProgramTriggerWindow.TurnEnding,SkillProgramEffectOp.ResolveMouZhangHeEnding,false),
                _=>throw new InvalidOperationException(path+": Unknown Qiaobian native binding.")
            };
            if(t.Window!=pair.Item1||t.Optional!=pair.Item3||t.Subject!=SkillProgramTriggerSubject.Owner||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||
                t.UsageScope is not null||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||
                t.Effects is not[var e]||e.Op!=pair.Item2||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always)
                throw new InvalidOperationException(path+": Qiaobian's costs, reward timing and compulsory extra Play cannot be overridden.");
        }
    }
}
