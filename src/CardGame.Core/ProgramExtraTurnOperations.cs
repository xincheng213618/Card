namespace CardGame.Core;

internal sealed class PendExtraTurnProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PendExtraTurn;
    public override ISkillProgramEffectHandler Handler { get; } = new PendExtraTurnSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.TurnEffects;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PendExtraTurn,
        static (_, context) => context.PendExtraTurn());

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: an extra turn always belongs to the skill owner.");
        return new(Op, target, 0, r.Condition());
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class PendExtraTurnSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PendExtraTurn;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.PendExtraTurn(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
