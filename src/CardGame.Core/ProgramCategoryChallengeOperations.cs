namespace CardGame.Core;

/// <summary>Binds the exact cards supplied to an active program without moving them.</summary>
internal sealed class CaptureSelectedCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CaptureSelectedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new CaptureSelectedCardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.CaptureSelectedCards,
        static (effect, context) => context.CaptureSelectedCards(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op,
            FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(),
            resultBind: reader.RequiredIdentifier("resultBind"));
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new CaptureActivationCards(effect.ResultBind!)];
}

internal sealed class RevealBoundCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealBoundCards;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealBoundCardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.RevealBoundCards, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op,
            FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(),
            sourceBind: reader.RequiredIdentifier("sourceBind"));
        if (effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: reveal requires an unconditional or named-choice branch.");
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!)];
}

internal sealed class ChooseDifferentCategoryDiscardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    internal const string DiscardedOption = "discarded";
    internal const string DeclinedOption = "declined";

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseDifferentCategoryDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseDifferentCategoryDiscardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ChooseDifferentCategoryDiscard, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "chooserRef", "cardOwnerRef", "zones", "sourceBind", "resultBind", "condition");
        var zones = reader.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not
                (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}.zones: requires owned hand, equipment or judgment zones.");
        var source = reader.RequiredIdentifier("sourceBind");
        var result = reader.RequiredIdentifier("resultBind");
        if (source == result)
            throw new InvalidOperationException(
                $"Invalid skill program at {reader.Path}: sourceBind and resultBind must differ.");
        var effect = new SkillProgramEffect(Op,
            FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(),
            sourceBind: source, resultBind: result, zones: zones,
            chooserRef: reader.RequiredParticipantReference("chooserRef"),
            cardOwnerRef: reader.RequiredParticipantReference("cardOwnerRef"));
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [
            new ReadCardSet(effect.SourceBind!),
            new CreateChoiceResult(effect.ResultBind!, [DiscardedOption, DeclinedOption]),
            .. ParticipantResources(effect.ChooserRef, effect.CardOwnerRef)
        ];
}

public sealed class CaptureSelectedCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.CaptureSelectedCards;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.CaptureSelectedCards(frame, effect.ResultBind ??
            throw new InvalidOperationException("captureSelectedCards requires a result binding."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class RevealBoundCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealBoundCards;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RevealBoundCards(frame, effect.SourceBind ??
            throw new InvalidOperationException("revealBoundCards requires a source binding."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class ChooseDifferentCategoryDiscardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseDifferentCategoryDiscard;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.ChooseDifferentCategoryDiscard(
        frame,
        effect.ChooserRef ?? throw new InvalidOperationException("category challenge requires chooserRef."),
        effect.CardOwnerRef ?? throw new InvalidOperationException("category challenge requires cardOwnerRef."),
        effect.Zones,
        effect.SourceBind ?? throw new InvalidOperationException("category challenge requires sourceBind."),
        effect.ResultBind ?? throw new InvalidOperationException("category challenge requires resultBind."),
        new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}
