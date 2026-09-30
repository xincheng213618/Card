namespace CardGame.Core;

internal interface ITriggeredCardProgramHost
{
    SkillProgramStepOutcome UseTriggeredVirtualSlash(ProgramSkillFrame frame, int targetSeat);
    SkillProgramStepOutcome UseDiscardedCardAsDelayedTrick(ProgramSkillFrame frame, int targetSeat, CardKind kind);
    SkillProgramStepOutcome PayEquipmentColorDiscard(ProgramSkillFrame frame, string resultBind);
    void GrantPlayPhaseColorRestriction(ProgramSkillFrame frame, string sourceBind, int targetSeat);
}

internal sealed class UseVirtualSlashProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new UseVirtualSlashProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetRef", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var reference = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (reference is null && target != SkillProgramEffectTarget.SelectedTarget ||
            reference is not null && (target != SkillProgramEffectTarget.Owner || reference.Kind != ProgramParticipantRef.EventTarget))
            throw new InvalidOperationException("Virtual Slash requires a selected target or the event target.");
        return new(Op, target, 0, r.Condition(), targetReference: reference);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.TargetReference is null ? [new ReadSelectedTarget()] : ParticipantResources(effect.TargetReference);
}

public sealed class UseVirtualSlashProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ITriggeredCardProgramHost)host).UseTriggeredVirtualSlash(frame,
            effect.TargetReference is { } reference ? host.ResolveParticipant(frame, reference) : targetSeat);
}

internal sealed class UseDiscardedCardAsDelayedTrickProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseDiscardedCardAsDelayedTrick;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.MovementSource;
    public override ISkillProgramEffectHandler Handler { get; } = new UseDiscardedCardAsDelayedTrickProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "outputKind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var output = r.RequiredEnum<CardKind>("outputKind");
        if (target != SkillProgramEffectTarget.SelectedTarget || output != CardKind.SupplyShortage)
            throw new InvalidOperationException("Discarded-card conversion requires a selected Supply Shortage target.");
        return new(Op, target, 0, r.Condition(), outputKind: output);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived), new ReadSelectedTarget(), new RequireContext(ProgramContextCapability.MovementSource)];
}

public sealed class UseDiscardedCardAsDelayedTrickProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseDiscardedCardAsDelayedTrick;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ITriggeredCardProgramHost)host).UseDiscardedCardAsDelayedTrick(frame, targetSeat, effect.OutputKind!.Value);
}

internal sealed class PayEquipmentColorDiscardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayEquipmentColorDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new PayEquipmentColorDiscardProgramHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectOwnedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("Equipment-color discard is owner scoped.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 1, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new CreateCardSet(effect.ResultBind!, 1, false, AlreadyMoved: true)];
}

public sealed class PayEquipmentColorDiscardProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PayEquipmentColorDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ITriggeredCardProgramHost)host).PayEquipmentColorDiscard(frame, effect.ResultBind!);
}

internal sealed class GrantPlayPhaseColorRestrictionProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPlayPhaseColorRestriction;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.PhaseOwner;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantPlayPhaseColorRestrictionProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnHandColorRestriction, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "targetRef", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || r.RequiredParticipantReference("targetRef").Kind != ProgramParticipantRef.EventTarget)
            throw new InvalidOperationException("Phase color restriction requires the event target.");
        return new(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"), targetReference: r.RequiredParticipantReference("targetRef"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting), new ReadFrozenSingleCardSet(effect.SourceBind!), new RequireContext(ProgramContextCapability.PhaseOwner)];
}

public sealed class GrantPlayPhaseColorRestrictionProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPlayPhaseColorRestriction;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((ITriggeredCardProgramHost)host).GrantPlayPhaseColorRestriction(frame, effect.SourceBind!, host.ResolveParticipant(frame, effect.TargetReference!));
        return SkillProgramStepOutcome.Continue;
    }
}
