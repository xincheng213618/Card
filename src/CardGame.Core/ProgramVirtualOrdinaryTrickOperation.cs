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
        ((IVirtualOrdinaryTrickProgramHost)host).UseVirtualOrdinaryTrick(frame, effect);
}

internal sealed class UseVirtualOrdinaryTrickProgramOperationDescriptor : ProgramOperationDescriptorBase
{
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
        if (target != SkillProgramEffectTarget.Owner ||
            actor.Kind is not (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget) ||
            recipient.Kind is not (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget) ||
            kind is not (CardKind.DrawTwo or CardKind.Dismantlement) ||
            (kind == CardKind.DrawTwo) != (actor.Kind == recipient.Kind))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: virtual ordinary trick requires an owner placeholder, owner/selectedTarget references, self DrawTwo or distinct-participant Dismantlement.");
        var condition = reader.Condition();
        if (condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.PindianWon or SkillProgramConditionKind.PindianNotWon))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.condition: virtual ordinary trick accepts always or a named Pindian branch.");
        return new SkillProgramEffect(Op, target, 0, condition, outputKind: kind,
            actorReference: actor, targetReference: recipient);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Array.AsReadOnly(ParticipantResources(effect.ActorReference, effect.TargetReference)
            .Concat(new ProgramResourceOperation[] { new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding) }).ToArray());
}
