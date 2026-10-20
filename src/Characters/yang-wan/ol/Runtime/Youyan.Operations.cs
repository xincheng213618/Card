namespace CardGame.Core;

// 诱言: the own-discarded suits of this batch stay out of the gain; the host
// reads the window batch and takes one top card of every other suit.
internal sealed class YouyanGainSuitCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YouyanGainSuitCards;
    public override ISkillProgramEffectHandler Handler { get; } = new YouyanGainSuitCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 3, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

public sealed class YouyanGainSuitCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YouyanGainSuitCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IYangWanProgramHost>().YouyanGainSuitCards(f, e);
}
