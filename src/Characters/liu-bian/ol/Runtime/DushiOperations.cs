namespace CardGame.Core;

// 毒逝: on the owner's death a chosen other character acquires 毒逝 itself,
// carrying both the death grant and the dying self-rescue limit onward.
internal sealed class DuShiGrantSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DuShiGrantSkill;
    public override ISkillProgramEffectHandler Handler { get; } = new DuShiGrantSkillHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

public sealed class DuShiGrantSkillHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DuShiGrantSkill;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuBianProgramHost>().DuShiGrantSkill(f, e);
}
