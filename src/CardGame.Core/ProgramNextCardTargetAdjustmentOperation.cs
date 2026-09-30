namespace CardGame.Core;

internal interface INextCardTargetAdjustmentProgramHost
{
    void GrantNextCardTargetAdjustment(ProgramSkillFrame frame);
}

internal sealed class GrantNextCardTargetAdjustmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantNextCardTargetAdjustment;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantNextCardTargetAdjustmentHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A next-card target adjustment must be granted to the owner.");
        return new(Op, SkillProgramEffectTarget.Owner, 1, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class GrantNextCardTargetAdjustmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantNextCardTargetAdjustment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((INextCardTargetAdjustmentProgramHost)host).GrantNextCardTargetAdjustment(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
