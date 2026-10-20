namespace CardGame.Core;

// 丰积: at each round start the owner walks the two options in text order,
// granting -1/+2 redistribution or keeping +1 per declined option for one round.
internal sealed class FengjiRoundChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.FengjiRoundChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new FengjiRoundChoiceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.FengjiRoundChoice,
        static (effect, context) => context.FengjiRoundChoice(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)];
}

public sealed class FengjiRoundChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.FengjiRoundChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IChenDengProgramHost>().FengjiRoundChoice(f, e);
}
