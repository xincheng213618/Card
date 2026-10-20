namespace CardGame.Core;

// 节烈: one voluntary before-damage operation that prevents the current damage,
// charges the owner the damage value as hp loss and lets the marked character
// randomly take that many cards of the chosen suit from the discard pile.
internal sealed class JieliePreventAndGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JieliePreventAndGift;
    public override ISkillProgramEffectHandler Handler { get; } = new JieliePreventAndGiftHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class JieliePreventAndGiftHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JieliePreventAndGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITangJiProgramHost>().JieliePreventAndGift(f, e);
}
