namespace CardGame.Core;

// 逊贤: a used or played card that settled into the discard pile is given to one
// character with more hand cards or more health than the owner.
internal sealed class XunxianGiftUsedCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XunxianGiftUsedCard;
    public override ISkillProgramEffectHandler Handler { get; } = new XunxianGiftUsedCardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiftSettledUsedCard,
        static (effect, context) => context.GiftSettledUsedCard(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

public sealed class XunxianGiftUsedCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XunxianGiftUsedCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IYanJunProgramHost>().XunxianGiftUsedCard(f, e);
}
