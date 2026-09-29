namespace CardGame.Core;

/// <summary>Select up to the requested number from a participant's own zones, without moving cards.</summary>
internal sealed class SelectOwnedCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectOwnedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectOwnedCardsSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.SelectOwnedCards, static (effect, context) => context.SelectOwnedCards(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "numberExpression", "minimumCards", "maximumCards",
            "cardKinds", "suits", "zones", "resultBind", "targetRef", "condition");
        var variable = r.Has("minimumCards") || r.Has("maximumCards");
        if (variable != (r.Has("minimumCards") && r.Has("maximumCards")) ||
            variable && (r.Has("amount") || r.Has("numberExpression")))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: variable selection requires both card bounds and no fixed amount.");
        var minimum = variable ? r.RequiredInt("minimumCards") : 0;
        var maximum = variable ? r.RequiredInt("maximumCards") : 0;
        if (variable && (minimum < 1 || maximum < minimum || maximum > 20))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: owned-card bounds must satisfy 1 <= minimumCards <= maximumCards <= 20.");
        var cardKinds = r.OptionalEnumArray<CardKind>("cardKinds") ?? [];
        var suits = r.OptionalEnumArray<Suit>("suits") ?? [];
        if (r.Has("cardKinds") && cardKinds.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardKinds: must not be empty.");
        if (r.Has("suits") && suits.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.suits: must not be empty.");
        var expression = r.Has("numberExpression")
            ? r.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        if (expression is not null && (r.Has("amount") || expression is not
                (SkillProgramNumberExpression.OwnerLostHp or SkillProgramNumberExpression.AllOwnedZoneCards or
                 SkillProgramNumberExpression.HandHalfFloor or SkillProgramNumberExpression.SelectedPairHandDifference or
                 SkillProgramNumberExpression.LivingPlayersMinHp)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: selection accepts a constant, ownerLostHp, allOwnedZoneCards, handHalfFloor, livingPlayersMinHp or selectedPairHandDifference.");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: requires owned hand, equipment or judgment zones.");
        if (expression == SkillProgramNumberExpression.HandHalfFloor &&
            !zones.SequenceEqual([CardZoneKind.Hand]))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: handHalfFloor counts the owner hand and requires the hand zone only.");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var targetRef = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (targetRef is not null && (target != SkillProgramEffectTarget.Owner ||
            targetRef.Kind is not (ProgramParticipantRef.EventSource or ProgramParticipantRef.EventTarget)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetRef: owned-card selection requires an event participant and owner placeholder.");
        var effect = new SkillProgramEffect(Op, target,
            variable || expression is not null ? 0 : DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(),
            numberExpression: expression, zones: zones, resultBind: r.RequiredIdentifier("resultBind"),
            minimumCards: minimum, maximumCards: maximum, cardKinds: cardKinds, suits: suits,
            targetReference: targetRef);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect, [new CaptureSourceCard(effect.ResultBind!,
            effect.MaximumCards > 0 ? effect.MaximumCards :
                effect.NumberExpression is null ? effect.Amount : int.MaxValue,
            effect.Target == SkillProgramEffectTarget.Owner && effect.Zones.SequenceEqual([CardZoneKind.Hand]),
            effect.Target)])
            .Concat(ParticipantResources(effect.TargetReference)).ToArray();
}

public sealed class SelectOwnedCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectOwnedCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.SelectOwnedCards(frame,
            effect.TargetReference is { } reference ? host.ResolveParticipant(frame, reference) : targetSeat,
            effect.Amount, effect.NumberExpression, effect.Zones,
            effect.ResultBind!, effect.MinimumCards, effect.MaximumCards, effect.CardKinds,
            effect.Suits);
}
