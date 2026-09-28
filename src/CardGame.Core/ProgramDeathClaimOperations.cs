namespace CardGame.Core;

internal sealed class ClaimDeathCleanupCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDeathCleanupCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimDeathCleanupCardsSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Death;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ClaimDeathCleanupCards,
        static (_, context) => context.ClaimDeathCleanupCards());

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: death cleanup cards always belong to the skill owner.");
        return new(Op, target, 0, r.Condition());
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class ClaimDeathCleanupCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDeathCleanupCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.ClaimDeathCleanupCards(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
