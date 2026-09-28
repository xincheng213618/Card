namespace CardGame.Core;

internal sealed class GrantTurnHandCardProhibitionProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnHandCardProhibition;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnHandCardProhibitionSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GrantTurnHandColorRestriction,
        static (effect, context) => context.GrantTurnHandColorRestriction(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: hand-card prohibition requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect, []);
}

public sealed class GrantTurnHandCardProhibitionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnHandCardProhibition;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.GrantTurnHandCardProhibition(frame, targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}
