namespace CardGame.Core;

internal interface IXunChenProgramHost
{
    void GivePindianCard(ProgramSkillFrame frame, int targetSeat);
}

// 锋略's settlement gift: the owner's own pindian card leaves the discard pile
// for the counterpart's hand after the contest resolves.
internal sealed class GivePindianCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GivePindianCard;
    public override ISkillProgramEffectHandler Handler { get; } = new GivePindianCardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("Pindian-card giving requires the selected counterpart.");
        var condition = r.Condition();
        if (condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs))
            throw new InvalidOperationException("Pindian-card giving accepts always or a named-choice branch.");
        return new SkillProgramEffect(Op, target, 1, condition);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}

public sealed class GivePindianCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GivePindianCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    {
        ((IXunChenProgramHost)h).GivePindianCard(f, seat);
        return SkillProgramStepOutcome.Continue;
    }
}
