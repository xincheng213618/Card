namespace CardGame.Core;
internal static class MouCaoAngComposition
{
    [ProgramCatalogValidation(1890)]internal static void Validate(string path,SkillProgram p)
    {
        var ops=p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects));
        if(p.Id is not(MouCaoAngRuntime.Fengmin or MouCaoAngRuntime.Zhiwang)&&!ops.Any(e=>e.Op is SkillProgramEffectOp.ResolveMouCaoAngFengmin or SkillProgramEffectOp.ResolveMouCaoAngZhiwang))return;
        var valid=p.Id==MouCaoAngRuntime.Fengmin?p.Triggers is[{Id:"any-equipment-loss",Window:SkillProgramTriggerWindow.CardsMoved,Subject:SkillProgramTriggerSubject.Owner,Optional:false,MovementOccurrence:SkillProgramMovementOccurrence.PerBatch}loss]&&loss.SourceZones.SequenceEqual([CardZoneKind.Equipment])&&!loss.MovementDiscardOnly&&loss.Effects is[{Op:SkillProgramEffectOp.ResolveMouCaoAngFengmin,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]:p.Id==MouCaoAngRuntime.Zhiwang&&p.Triggers is[{Id:"ending-discard-and-use",Window:SkillProgramTriggerWindow.TurnEnding,Subject:SkillProgramTriggerSubject.Owner,Optional:false}ending]&&ending.Effects is[{Op:SkillProgramEffectOp.ResolveMouCaoAngZhiwang,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}];
        if(!valid||p.Activations.Count!=0||p.BooleanStates.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.CardPolicies.Count!=0)throw new InvalidOperationException(path+": Mou Cao Ang requires global real equipment loss, actual-turn Fengmin and original discard-entity Zhiwang Use.");
    }
}
