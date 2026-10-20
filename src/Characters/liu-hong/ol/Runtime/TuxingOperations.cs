namespace CardGame.Core;

// 图兴 second clause catch-up: the locked turn-start sweep arms the game-long
// damage bonus once every equipment slot stands abolished; the estimate stays
// neutral because the trigger is locked and carries no choices.
internal sealed class TuxingArmGameDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TuxingArmGameDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new TuxingArmGameDamageHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class TuxingArmGameDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TuxingArmGameDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuHongProgramHost>().TuxingArmGameDamage(f, e);
}
