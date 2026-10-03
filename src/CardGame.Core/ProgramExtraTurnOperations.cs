namespace CardGame.Core;

internal sealed class PendExtraTurnProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PendExtraTurn;
    public override ISkillProgramEffectHandler Handler { get; } = new PendExtraTurnSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.TurnEffects;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PendExtraTurn,
        static (effect, context) => context.PendExtraTurn(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetRef", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: an extra turn always belongs to the skill owner.");
        var targetRef = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (targetRef is not null && targetRef.Kind != ProgramParticipantRef.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.targetRef: an extra turn beneficiary must be the selected target.");
        return new(Op, target, 0, r.Condition(), targetReference: targetRef);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.TargetReference);
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
        host.PendExtraTurn(frame,
            effect.TargetReference is { } reference ? host.ResolveParticipant(frame, reference) : null);
        return SkillProgramStepOutcome.Continue;
    }
}
