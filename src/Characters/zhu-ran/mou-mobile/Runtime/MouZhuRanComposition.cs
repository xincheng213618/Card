namespace CardGame.Core;
internal static class MouZhuRanComposition
{
    [ProgramCatalogValidation(1660)]
    internal static void Validate(string path,SkillProgram p)
    {
        static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveMouZhuRanZhenwei or SkillProgramEffectOp.ResolveMouZhuRanHeyuan;
        static bool Effect(SkillProgramEffect e,SkillProgramEffectOp op)=>e.Op==op&&e.Target==SkillProgramEffectTarget.Owner&&e.Condition.Kind==SkillProgramConditionKind.Always;
        if(p.Id is not(MouZhuRanRuntime.Zhenwei or MouZhuRanRuntime.Heyuan))
        {
            if(p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Own(e.Op)))
                throw new InvalidOperationException(path+": Mou Zhu Ran operations require the complete sealed-payment/last-item skill bindings.");
            return;
        }
        var invalid=p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.BooleanStates.Count!=0||p.CardPolicies.Count!=0||
            p.HpSkillGrants.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0;
        if(p.Id==MouZhuRanRuntime.Zhenwei)
        {
            invalid|=p.Triggers.Count!=0||p.Activations.Count!=1;
            if(p.Activations is[var a])invalid|=a.Id!="sealed-discard"||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||
                a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerPhase!=1||a.UsesPerTurn is not null||a.UsesPerGame is not null||a.OtherCharactersOnly||
                a.Condition.Kind!=SkillProgramConditionKind.Always||a.Effects is not[var e]||!Effect(e,SkillProgramEffectOp.ResolveMouZhuRanZhenwei);
        }
        else
        {
            invalid|=p.Activations.Count!=0||p.Triggers.Count!=1;
            if(p.Triggers is[var t])invalid|=t.Id!="repeat-last"||t.Window!=SkillProgramTriggerWindow.TurnEnding||t.Subject!=SkillProgramTriggerSubject.Owner||
                t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||!t.Optional||t.UsageScope is not null||t.UsageLimit is not null||
                t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.SourceZones.Count!=0||
                t.Suits.Count!=0||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.DamageCardKinds.Count!=0||t.DestinationZones.Count!=0||
                t.Effects is not[var e]||!Effect(e,SkillProgramEffectOp.ResolveMouZhuRanHeyuan);
        }
        if(invalid)throw new InvalidOperationException(path+": Mou Zhu Ran must retain simultaneous HE commitments, all ordered outcomes and the full lifetime Heyuan obligation.");
    }
}
