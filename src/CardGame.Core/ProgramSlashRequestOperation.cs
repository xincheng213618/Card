namespace CardGame.Core;

internal sealed class RequestSlashByTargetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    internal const string UsedSlashOption = "used-slash";
    internal const string DeclinedOption = "declined";

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashByTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestSlashByTargetSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RequestSlashByTarget,
        static (effect, context) => context.RequestSlashByTarget(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "responderRef", "victimRef", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var responderRef = r.Has("responderRef") ? r.RequiredParticipantReference("responderRef") : null;
        var victimRef = r.Has("victimRef") ? r.RequiredParticipantReference("victimRef") : null;
        if (victimRef is not null && responderRef is null)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.victimRef: a bound victim requires a bound responder.");
        if (responderRef is { } responder)
        {
            if (target != SkillProgramEffectTarget.Owner ||
                responder.Kind is not (ProgramParticipantRef.SelectedFirst or ProgramParticipantRef.SelectedSecond))
                throw new InvalidOperationException(
                    $"Invalid skill program at {r.Path}.responderRef: a bound responder requires an owner placeholder and a selected participant.");
        }
        else if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: a slash request requires one selected target.");
        if (victimRef is { Kind: not (ProgramParticipantRef.SelectedFirst or ProgramParticipantRef.SelectedSecond) })
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.victimRef: a bound victim must be a selected participant.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            resultBind: r.RequiredIdentifier("resultBind"),
            targetReference: responderRef, actorReference: victimRef);
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        .. effect.TargetReference is { } responder
            ? ParticipantResources(responder)
            : new ProgramResourceOperation[] { new ReadSelectedTarget() },
        .. ParticipantResources(effect.ActorReference),
        new CreateChoiceResult(effect.ResultBind!, [UsedSlashOption, DeclinedOption])
    ];
}

public sealed class RequestSlashByTargetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashByTarget;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.RequestSlashByTarget(frame, targetSeat,
            effect.ActorReference is { } victim ? host.ResolveParticipant(frame, victim) : frame.OwnerSeat,
            effect.ResultBind!);
}
