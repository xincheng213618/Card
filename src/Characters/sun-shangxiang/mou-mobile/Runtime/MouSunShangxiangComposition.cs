namespace CardGame.Core;

internal static class MouSunShangxiangComposition
{
    [ProgramCatalogValidation(1730)]
    internal static void Validate(string path,SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveMouSunShangxiangJieyin or SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu or SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji;
        var ours=p.Id is MouSunShangxiangRuntime.Jieyin or MouSunShangxiangRuntime.Liangzhu or MouSunShangxiangRuntime.Xiaoji;
        if(!ours&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(Reserved))return;
        if(!ours||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.BooleanStates.Count!=0)
            throw new InvalidOperationException(path+": Sun Shangxiang requires her actual mission, faction and Equipment bindings.");
        static bool Effect(IReadOnlyList<SkillProgramEffect> effects,SkillProgramEffectOp op)=>effects is[var e]&&e.Op==op&&e.Target==SkillProgramEffectTarget.Owner&&e.Condition.Kind==SkillProgramConditionKind.Always;
        bool valid;
        if(p.Id==MouSunShangxiangRuntime.Liangzhu)valid=p.Triggers.Count==0&&p.Activations is[var a]&&a.Id=="deposit-zhuang"&&a.UsesPerPhase==1&&a.UsesPerTurn is null&&a.MinCards==0&&a.MaxCards==0&&a.MinTargets==0&&a.MaxTargets==0&&Effect(a.Effects,SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu);
        else if(p.Id==MouSunShangxiangRuntime.Xiaoji)valid=p.Activations.Count==0&&p.Triggers is[var t]&&t.Id=="equipment-loss-draw-discard"&&t.Window==SkillProgramTriggerWindow.CardsMoved&&t.Subject==SkillProgramTriggerSubject.Owner&&!t.Optional&&t.MovementOccurrence==SkillProgramMovementOccurrence.PerCard&&t.SourceZones.SequenceEqual([CardZoneKind.Equipment])&&Effect(t.Effects,SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji);
        else valid=p.Activations.Count==0&&p.Triggers.Count==3&&p.Triggers.All(t=>!t.Optional&&Effect(t.Effects,SkillProgramEffectOp.ResolveMouSunShangxiangJieyin))&&
            p.Triggers.Any(t=>t.Id=="initial-zhu"&&t.Window==SkillProgramTriggerWindow.GameStarting&&t.Subject==SkillProgramTriggerSubject.Owner)&&
            p.Triggers.Any(t=>t.Id=="marked-holder-request"&&t.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&t.Subject==SkillProgramTriggerSubject.Owner)&&
            p.Triggers.Any(t=>t.Id=="holder-death-failure"&&t.Window==SkillProgramTriggerWindow.CharacterDied&&t.Subject==SkillProgramTriggerSubject.Owner);
        if(!valid)throw new InvalidOperationException(path+": Sun Shangxiang changed a reserved actual entry or phase use limit.");
    }
}
