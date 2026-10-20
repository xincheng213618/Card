namespace CardGame.Core;

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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashFullyDodged)];
}

public sealed class ZengouNullifyDodgeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZengouNullifyDodge;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IQingHeGongZhuProgramHost>().ZengouNullifyDodge(f, e);
}
