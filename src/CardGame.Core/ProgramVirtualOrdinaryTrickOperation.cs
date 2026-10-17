namespace CardGame.Core;

internal interface IVirtualOrdinaryTrickProgramHost
{
    SkillProgramStepOutcome UseVirtualOrdinaryTrick(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class UseVirtualOrdinaryTrickSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualOrdinaryTrick;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IVirtualOrdinaryTrickProgramHost>().UseVirtualOrdinaryTrick(frame, effect);
}

internal sealed class UseVirtualOrdinaryTrickProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    internal static bool IsGroupOutput(CardKind? kind) => kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage;
    internal static bool AllowsWindow(CardKind? kind, SkillProgramTriggerWindow window) => IsGroupOutput(kind)
        ? window is SkillProgramTriggerWindow.PlayPhaseStarting or SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.AfterTurnEnded
        : window == SkillProgramTriggerWindow.PlayEnding;

    internal static bool HasValidReferences(CardKind? kind, ProgramParticipantRef? actor, ProgramParticipantRef? recipient) =>
        IsGroupOutput(kind) ? actor == ProgramParticipantRef.Owner && recipient == ProgramParticipantRef.Owner :
        kind is (CardKind.DrawTwo or CardKind.Dismantlement) &&
        actor is (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget) &&
        recipient is (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget) &&
        (kind == CardKind.DrawTwo) == (actor == recipient);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualOrdinaryTrick;
    public override ISkillProgramEffectHandler Handler { get; } = new UseVirtualOrdinaryTrickSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (effect, context) => context.UseVirtualOrdinaryTrick(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "actorRef", "targetRef", "outputKind", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var actor = reader.RequiredParticipantReference("actorRef");
        var recipient = reader.RequiredParticipantReference("targetRef");
        var kind = reader.RequiredEnum<CardKind>("outputKind");
        if (target != SkillProgramEffectTarget.Owner || !HasValidReferences(kind, actor.Kind, recipient.Kind))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: virtual ordinary trick requires an owner placeholder and valid references for self DrawTwo, distinct-participant Dismantlement, or owner-origin global assault.");
        var condition = reader.Condition();
        if (IsGroupOutput(kind) ? condition.Kind != SkillProgramConditionKind.Always :
            condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.condition: virtual ordinary trick accepts always or a named Pindian branch.");
        return new SkillProgramEffect(Op, target, 0, condition, outputKind: kind,
            actorReference: actor, targetReference: recipient);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Array.AsReadOnly(ParticipantResources(effect.ActorReference, effect.TargetReference)
            .Concat(IsGroupOutput(effect.OutputKind)
                ? new ProgramResourceOperation[] { new RequireTriggerWindows([SkillProgramTriggerWindow.PlayPhaseStarting,
                    SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramTriggerWindow.AfterTurnEnded]) }
                : [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)]).ToArray());
}
