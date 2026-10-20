namespace CardGame.Core;

// 诗怨: becoming the target of another character's card offers a draw of three,
// two or one cards by that character's health compared to the owner's; each
// option is limited per turn and 余威 doubles the limit on other Qun turns.
internal sealed class ShiYuanTargetDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ShiYuanTargetDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiYuanTargetDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}

public sealed class ShiYuanTargetDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ShiYuanTargetDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuBianProgramHost>().ShiYuanTargetDraw(f, e);
}
