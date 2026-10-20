namespace CardGame.Core;

// 哀尘: the compulsory dying-entry removal reads its remaining options from
// committed history; the estimate stays neutral because the trigger is locked.
internal sealed class AichenRemoveOptionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AichenRemoveOption;
    public override ISkillProgramEffectHandler Handler { get; } = new AichenRemoveOptionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}

public sealed class AichenRemoveOptionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AichenRemoveOption;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITengFangLanProgramHost>().AichenRemoveOption(f, e);
}
