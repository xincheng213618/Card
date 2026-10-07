namespace CardGame.Core;

// 长姬: at any character's ending phase the owner may have that character draw
// two cards (if she dealt damage this turn) or discard two cards (if she took
// damage this turn); the availability choice is made inside the operation.
internal sealed class ChangjiEndingDamageChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChangjiEndingDamageChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new ChangjiEndingDamageChoiceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public sealed class ChangjiEndingDamageChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChangjiEndingDamageChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IQingHeGongZhuProgramHost)host).ChangjiEndingDamageChoice(f, e);
}

// 谮构: a character within the owner's attack range used a Dodge that fully
// resolved; the owner pays one non-basic card or 1 HP to nullify it and gain
// its entity. The attack-range gate is also expressed as a trigger condition.
internal sealed class ZengouNullifyDodgeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouNullifyDodge;
    public override ISkillProgramEffectHandler Handler { get; } = new ZengouNullifyDodgeHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashFullyDodged)];
}

public sealed class ZengouNullifyDodgeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouNullifyDodge;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IQingHeGongZhuProgramHost)host).ZengouNullifyDodge(f, e);
}
