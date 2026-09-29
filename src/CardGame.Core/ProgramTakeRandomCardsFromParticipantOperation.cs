namespace CardGame.Core;

/// <summary>
/// Takes a fixed number of random cards from one bound program participant's declared
/// areas into the owner's hand. The participant is a frozen selection slot, never a
/// skill identity, so the primitive stays reusable across forced-choice skills.
/// </summary>
internal sealed class TakeRandomCardsFromParticipantProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomCardsFromParticipant;
    public override ISkillProgramEffectHandler Handler { get; } =
        new TakeRandomCardsFromParticipantSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.TakeRandomCardsFromParticipant,
        static (effect, context) => context.TakeRandomCardsFromParticipant(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "participantRef", "amount", "zones", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: a participant take requires the owner placeholder.");
        var participantRef = r.RequiredParticipantReference("participantRef");
        if (participantRef.Kind is not (ProgramParticipantRef.SelectedFirst or ProgramParticipantRef.SelectedSecond))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.participantRef: the participant must be a selected slot.");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.zones: random participant takes support hand and equipment only.");
        var effect = new SkillProgramEffect(Op, target, DrawProgramOperationDescriptor.Amount(r, 20),
            r.Condition(), zones: zones, targetReference: participantRef);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.TargetReference);
}

public sealed class TakeRandomCardsFromParticipantSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomCardsFromParticipant;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.TakeRandomCardsFromParticipant(frame,
            effect.TargetReference ?? throw new InvalidOperationException(
                "A participant take lost its bound participant."),
            effect.Amount, effect.Zones,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}
