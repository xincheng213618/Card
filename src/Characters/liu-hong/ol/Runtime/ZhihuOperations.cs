namespace CardGame.Core;

// 执笏 expiry: the locked turn-start sweep removes grants sourced by the
// current turn's grantor; the estimate stays neutral because the trigger is
// locked and carries no choices.
internal sealed class ZhihuExpireDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhihuExpire;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhihuExpireHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow])];
}

public sealed class ZhihuExpireHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhihuExpire;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuHongProgramHost>().ZhihuExpire(f, e);
}
