namespace CardGame.Core;

/// <summary>Marks the current card action as ineffective for this trigger owner only.</summary>
internal sealed class NullifyCurrentCardEffectProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyCurrentCardEffect;
    public override ISkillProgramEffectHandler Handler { get; } =
        new NullifyCurrentCardEffectSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (_, context) => context.NullifyCurrentCardEffect());

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: must be owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class NullifyCurrentCardEffectSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyCurrentCardEffect;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.NullifyCurrentCardEffect(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
