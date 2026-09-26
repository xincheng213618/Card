namespace CardGame.Core;

internal sealed class ChangeAttributedMarkerProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChangeAttributedMarker;
    public override ISkillProgramEffectHandler Handler { get; } = new ChangeAttributedMarkerSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "targetRef", "marker", "amount", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(reader);
        var targetRef = reader.RequiredParticipantReference("targetRef");
        if (targetRef.Kind != ProgramParticipantRef.EventSource)
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}.targetRef: attributed damage markers require eventSource.");
        var amount = reader.RequiredInt("amount");
        if (amount is < 1 or > 20)
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}.amount: must be between 1 and 20.");
        return new SkillProgramEffect(Op, target, amount, reader.Condition(),
            targetReference: targetRef, marker: reader.RequiredEnum<PlayerMarkerKind>("marker"));
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.TargetReference);
}

public sealed class ChangeAttributedMarkerSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChangeAttributedMarker;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.ChangeAttributedMarker(
            frame,
            effect.TargetReference ?? throw new InvalidOperationException("Attributed marker has no target participant."),
            effect.Marker ?? throw new InvalidOperationException("Attributed marker has no marker kind."),
            effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class CauseDeathUnlessBoundCardKindProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CauseDeathUnlessBoundCardKind;
    public override ISkillProgramEffectHandler Handler { get; } = new CauseDeathUnlessBoundCardKindSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Death;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.CauseDeath, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "excludedCardKinds", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}.target: direct death requires selectedTarget.");
        var excluded = reader.RequiredEnumArray<CardKind>("excludedCardKinds");
        if (excluded.Count == 0)
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}.excludedCardKinds: must not be empty.");
        return new SkillProgramEffect(Op, target, 0, reader.Condition(),
            sourceBind: reader.RequiredIdentifier("sourceBind"), cardKinds: excluded);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile), new ReadSelectedTarget()];
}

public sealed class CauseDeathUnlessBoundCardKindSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.CauseDeathUnlessBoundCardKind;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.CauseDeathUnlessBoundCardKind(
            frame,
            effect.SourceBind ?? throw new InvalidOperationException("Direct death has no judgment binding."),
            effect.CardKinds);
}
