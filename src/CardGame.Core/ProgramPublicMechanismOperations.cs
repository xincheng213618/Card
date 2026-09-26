namespace CardGame.Core;

internal sealed class StartPindianProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StartPindian;
    public override ISkillProgramEffectHandler Handler { get; } = new StartPindianSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Pindian;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.StartPindian,
        static (effect, context) => context.StartPindian(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "opponentRef", "resultBind", "visibility", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(reader);
        var opponent = reader.RequiredParticipantReference("opponentRef");
        if (opponent.Kind is not (ProgramParticipantRef.SelectedTarget or ProgramParticipantRef.EventTarget))
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}.opponentRef: Pindian requires selectedTarget or a damage eventTarget.");
        var effect = new SkillProgramEffect(
            Op, target, 0, reader.Condition(),
            resultBind: reader.RequiredIdentifier("resultBind"),
            visibility: reader.RequiredEnum<SkillProgramCardSetVisibility>("visibility"),
            opponentReference: opponent);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.OpponentReference?.Kind == ProgramParticipantRef.EventTarget
            ? [new RequireContext(ProgramContextCapability.Damage), new CreatePindianResult(effect.ResultBind!)]
            : [new ReadSelectedTarget(), new CreatePindianResult(effect.ResultBind!)];
}

public sealed class StartPindianSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.StartPindian;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.StartPindian(
            frame,
            effect.OpponentReference ?? new ProgramParticipantReference(ProgramParticipantRef.SelectedTarget),
            effect.ResultBind ?? throw new InvalidOperationException("startPindian has no result binding."),
            effect.Visibility);
}

internal sealed class SetBooleanStateProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetBooleanState;
    public override ISkillProgramEffectHandler Handler { get; } = new SetBooleanStateSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.SetBooleanState,
        static (effect, context) => context.SetBooleanState(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "stateId", "value", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(),
            stateId: reader.RequiredIdentifier("stateId"), booleanValue: reader.RequiredBool("value"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class ToggleBooleanStateProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ToggleBooleanState;
    public override ISkillProgramEffectHandler Handler { get; } = new ToggleBooleanStateSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ToggleBooleanState,
        static (effect, context) => context.ToggleBooleanState(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "stateId", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(),
            stateId: reader.RequiredIdentifier("stateId"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class SetBooleanStateSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetBooleanState;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.SetBooleanState(frame, effect.StateId!, effect.BooleanValue!.Value);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class ToggleBooleanStateSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ToggleBooleanState;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.ToggleBooleanState(frame, effect.StateId!);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class GrantDirectedTurnCardPolicyProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantDirectedTurnCardPolicy;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantDirectedTurnCardPolicySkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.TurnEffects;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GrantDirectedTurnCardPolicy,
        static (effect, context) => context.GrantDirectedTurnCardPolicy(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "actorRef", "targetRef", "cardKinds", "effects", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(reader);
        var actor = reader.RequiredParticipantReference("actorRef", ProgramParticipantRef.Owner);
        var directedTarget = reader.RequiredParticipantReference("targetRef");
        var kinds = reader.OptionalEnumArray<CardKind>("cardKinds") ?? [];
        var flags = reader.RequiredEnumArray<DirectedTurnCardPolicyEffect>("effects")
            .Aggregate(DirectedTurnCardPolicyEffect.None, (current, value) => current | value);
        if (reader.Has("cardKinds") && kinds.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.cardKinds: explicit cardKinds must not be empty.");
        if (flags == DirectedTurnCardPolicyEffect.None)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: effects must not be empty.");
        return new(Op, target, 0, reader.Condition(), cardKinds: kinds,
            actorReference: actor, targetReference: directedTarget, directedPolicyEffects: flags);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.ActorReference, effect.TargetReference);
}

public sealed class GrantDirectedTurnCardPolicySkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantDirectedTurnCardPolicy;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantDirectedTurnCardPolicy(frame, effect.ActorReference!, effect.TargetReference!,
            effect.CardKinds, effect.DirectedPolicyEffects);
        return SkillProgramStepOutcome.Continue;
    }
}
