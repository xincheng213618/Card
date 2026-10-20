namespace CardGame.Core;

// 旋回: the owner exchanges the round's granted 丰积 effects once; the skill
// then disables until a character dies.
internal sealed class XuanhuiSwapEffectsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XuanhuiSwapEffects;
    public override ISkillProgramEffectHandler Handler { get; } = new XuanhuiSwapEffectsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.XuanhuiSwapEffects,
        static (effect, context) => context.XuanhuiSwapEffects(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class XuanhuiSwapEffectsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XuanhuiSwapEffects;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IChenDengProgramHost>().XuanhuiSwapEffects(f, e);
}
