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
        r.AllowOnly("op", "target", "chooserRef", "cardOwnerRef", "zones", "count", "destination", "targetRef", "resultBind", "cardCategories", "skipIfNoCards", "allowSameOwnerHandReturn", "coverageResultBind", "awaitMovementTriggers", "revealBeforeMove", "condition");
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
        if (destination is not (SkillProgramCardDestination.OwnerHand or SkillProgramCardDestination.DiscardPile or
                SkillProgramCardDestination.SelectedTargetHand or SkillProgramCardDestination.SelectedTargetEquipment))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.destination: unsupported destination.");
        if ((destination is SkillProgramCardDestination.SelectedTargetHand
                or SkillProgramCardDestination.SelectedTargetEquipment) != r.Has("targetRef"))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: selectedTargetHand and selectedTargetEquipment require targetRef only.");
        if (destination == SkillProgramCardDestination.SelectedTargetEquipment &&
            (!r.Has("zones") || r.RequiredEnumArray<CardZoneKind>("zones").Count != 1 ||
             r.RequiredEnumArray<CardZoneKind>("zones")[0] != CardZoneKind.Hand ||
             r.OptionalEnumArray<SkillProgramCardCategory>("cardCategories") is not { Count: 1 } equipmentFilter ||
             equipmentFilter[0] != SkillProgramCardCategory.Equipment))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: an equipment gift moves one category-filtered hand card.");
        var chooserRef = r.RequiredParticipantReference("chooserRef");
        var cardOwnerRef = r.RequiredParticipantReference("cardOwnerRef");
        var cardCategories = r.OptionalEnumArray<SkillProgramCardCategory>("cardCategories");
        if (cardCategories is { Count: 0 })
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardCategories: must not be empty when specified.");
        if (cardCategories is not null && chooserRef != cardOwnerRef)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardCategories: category filtering requires chooserRef and cardOwnerRef to name the same participant.");
        var skipIfNoCards = r.Has("skipIfNoCards") && r.RequiredBool("skipIfNoCards");
        var allowSameOwnerHandReturn = r.Has("allowSameOwnerHandReturn") && r.RequiredBool("allowSameOwnerHandReturn");
        var coverageResultBind = r.Has("coverageResultBind") ? r.OptionalIdentifier("coverageResultBind") : null;
        var awaitMovementTriggers = r.Has("awaitMovementTriggers") && r.RequiredBool("awaitMovementTriggers");
        var revealBeforeMove = r.Has("revealBeforeMove") && r.RequiredBool("revealBeforeMove");
        if (revealBeforeMove && (chooserRef.Kind != ProgramParticipantRef.Owner ||
            cardOwnerRef.Kind != ProgramParticipantRef.Owner || zones.Count != 1 || zones[0] != CardZoneKind.Hand ||
            destination != SkillProgramCardDestination.SelectedTargetHand || !r.Has("resultBind") ||
            r.RequiredParticipantReference("targetRef").Kind != ProgramParticipantRef.SelectedTarget ||
            !awaitMovementTriggers || skipIfNoCards))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: public transfer requires the owner's single hand card, a selected recipient, resultBind and movement continuation.");
        if (allowSameOwnerHandReturn && (destination != SkillProgramCardDestination.SelectedTargetHand ||
            zones.Any(zone => zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment)) ||
            r.RequiredParticipantReference("targetRef") != cardOwnerRef))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: same-owner hand returns require matching cardOwnerRef/targetRef and public non-hand source zones.");
        if (coverageResultBind is not null && (zones.Count != 1 || zones[0] != CardZoneKind.Equipment ||
            destination is not (SkillProgramCardDestination.SelectedTargetHand or SkillProgramCardDestination.DiscardPile)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: coverageResultBind requires equipment movement to hand or discard.");
        var effect = new SkillProgramEffect(Op, target, count, r.Condition(), zones: zones,
            destination: destination, resultBind: r.OptionalIdentifier("resultBind"),
            chooserRef: chooserRef, cardOwnerRef: cardOwnerRef, cardCategories: cardCategories,
            targetReference: r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null,
            skipIfNoCards: skipIfNoCards, allowSameOwnerHandReturn: allowSameOwnerHandReturn,
            coverageResultBind: coverageResultBind, awaitMovementTriggers: awaitMovementTriggers,
            revealBeforeMove: revealBeforeMove);
        if (effect.Condition.Kind != SkillProgramConditionKind.Always && effect.ResultBind is not null)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.condition: conditional card movement cannot produce a result binding.");
        if (effect.CoverageResultBind is not null &&
            (effect.Condition.Kind != SkillProgramConditionKind.Always || effect.SkipIfNoCards))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: coverageResultBind requires an unconditional non-skipping movement.");
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.ChooserRef, effect.CardOwnerRef, effect.TargetReference)
            .Concat(effect.CoverageResultBind is { } coverage
                ? new ProgramResourceOperation[] { new CreateCoverageResult(coverage) }
                : Array.Empty<ProgramResourceOperation>())
            .Concat(effect.ResultBind is { } bind
                ? new ProgramResourceOperation[] { new CreateCardSet(bind, 1, false,
                    AlreadyMoved: effect.RevealBeforeMove,
                    CardOwner: effect.Destination is SkillProgramCardDestination.SelectedTargetHand
                        or SkillProgramCardDestination.SelectedTargetEquipment
                        ? SkillProgramEffectTarget.SelectedTarget : SkillProgramEffectTarget.Owner) }
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

internal sealed class ChooseOtherOwnedCardDiscardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOtherOwnedCardDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseOtherOwnedCardDiscardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ChooseOtherOwnedCardDiscard,
        static (effect, context) => context.ChooseOtherOwnedCardDiscard(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "chooserRef", "zones", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: must be owner.");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not
                (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.zones: must contain hand, equipment, or judgment.");
        return new SkillProgramEffect(Op, target, 1, r.Condition(), zones: zones,
            chooserRef: r.RequiredParticipantReference("chooserRef"));
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.ChooserRef);
}

internal sealed class ChooseOwnCardDiscardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOwnCardDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseOwnCardDiscardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ChooseOwnCardDiscard,
        static (effect, context) => context.ChooseOwnCardDiscard(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "chooserRef", "zones", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: must be owner.");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not
                (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.zones: must contain hand or equipment.");
        var chooserRef = r.Has("chooserRef") ? r.RequiredParticipantReference("chooserRef") : null;
        if (chooserRef is not null && chooserRef.Kind is not
            (ProgramParticipantRef.EventSource or ProgramParticipantRef.EventTarget))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.chooserRef: own-card discard choosers must be eventSource or eventTarget.");
        return new SkillProgramEffect(Op, target, 1, r.Condition(), zones: zones, chooserRef: chooserRef);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        ParticipantResources(effect.ChooserRef);
}
