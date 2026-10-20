namespace CardGame.Core;

// 诱敌: the selected counterpart privately picks one concealed hand slot of the
// owner and discards it; the settlement reads the discarded card only — a
// non-Slash lets the owner take one random counterpart hand card, a black card
// lets the owner draw one, and both can apply.
internal sealed class YoudiBaitDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YoudiBaitDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new YoudiBaitDiscardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TakeRandomHandCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
}

public sealed class YoudiBaitDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YoudiBaitDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhouFangProgramHost>().YoudiBaitDiscard(f, e);
}
