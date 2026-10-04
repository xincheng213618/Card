namespace CardGame.Core;

internal interface IYiJiProgramHost
{
    SkillProgramStepOutcome GiveDrawPileBottomCard(ProgramSkillFrame frame);
}

// 机捷's payoff: the draw pile's bottom card is handed to the selected participant.
internal sealed class GiveDrawPileBottomCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveDrawPileBottomCard;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveDrawPileBottomCardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("Draw-pile bottom giving requires the selected participant.");
        var e = new SkillProgramEffect(Op, target, 1, r.Condition());
        RequireAlways(e, r.Path);
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}

public sealed class GiveDrawPileBottomCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveDrawPileBottomCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IYiJiProgramHost)h).GiveDrawPileBottomCard(f);
}
