namespace CardGame.Core;

internal sealed class SelectAndMoveOwnedCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectAndMoveOwnedCard;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectAndMoveOwnedCardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.SelectAndMoveOwnedCard, static (effect, context) => context.SelectAndMoveOwnedCard(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "chooserRef", "cardOwnerRef", "zones", "count", "destination", "resultBind", "cardCategories", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: must be owner.");
        var count = r.RequiredInt("count");
        if (count != 1)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.count: must be 1.");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: must contain hand, equipment, or judgment.");
        var destination = r.RequiredEnum<SkillProgramCardDestination>("destination");
        if (destination is not (SkillProgramCardDestination.OwnerHand or SkillProgramCardDestination.DiscardPile))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.destination: must be ownerHand or discardPile.");
        var chooserRef = r.RequiredParticipantReference("chooserRef");
        var cardOwnerRef = r.RequiredParticipantReference("cardOwnerRef");
        var cardCategories = r.OptionalEnumArray<SkillProgramCardCategory>("cardCategories");
        if (cardCategories is { Count: 0 })
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardCategories: must not be empty when specified.");
        if (cardCategories is not null && chooserRef != cardOwnerRef)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardCategories: category filtering requires chooserRef and cardOwnerRef to name the same participant.");
        var effect = new SkillProgramEffect(Op, target, count, r.Condition(), zones: zones,
            destination: destination, resultBind: r.OptionalIdentifier("resultBind"),
            chooserRef: chooserRef, cardOwnerRef: cardOwnerRef, cardCategories: cardCategories);
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.ChooserRef, effect.CardOwnerRef)
            .Concat(effect.ResultBind is { } bind
                ? new ProgramResourceOperation[] { new CreateCardSet(bind, 1, false) }
                : Array.Empty<ProgramResourceOperation>()).ToArray();
}

internal sealed class RefundCardUseDebitProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RefundCardUseDebit;
    public override ISkillProgramEffectHandler Handler { get; } = new RefundCardUseDebitSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.RefundCardUseDebit, static (effect, context) => context.RefundCardUseDebit(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: must be owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
