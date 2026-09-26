namespace CardGame.Core;

internal sealed class StartVirtualDuelProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StartVirtualDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new StartVirtualDuelSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.Damage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: a virtual duel reads an ordered target pair.");
        var effect = new SkillProgramEffect(Op, target, 1, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadTargetSet(2, 2)];
}

internal sealed class RequestFactionCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestFactionCard;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestFactionCardSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.Damage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "providerFactionId", "requiredKind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var kind = r.RequiredEnum<CardKind>("requiredKind");
        if (target != SkillProgramEffectTarget.SelectedTarget || kind != CardKind.Slash)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: faction requests require a selected Slash target.");
        var effect = new SkillProgramEffect(Op, target, 1, r.Condition(), outputKind: kind,
            providerFactionId: r.RequiredIdentifier("providerFactionId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

internal sealed class TransferRandomOwnedCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TransferRandomOwnedCard;
    public override ISkillProgramEffectHandler Handler { get; } = new TransferRandomOwnedCardSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (effect, context) => context.TransferRandomOwnedCard(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: a random owned-card transfer requires one target.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new CreateCardSet(effect.ResultBind!, 1, false,
            AlreadyMoved: true, CardOwner: SkillProgramEffectTarget.SelectedTarget)];
}

internal sealed class AccumulateSelectedCardCountProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AccumulateSelectedCardCount;
    public override ISkillProgramEffectHandler Handler { get; } = new AccumulateSelectedCardCountSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiveSelected,
        static (effect, context) => context.AccumulateSelectedCardCount(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "usageId", "threshold", "resultBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var threshold = r.RequiredInt("threshold");
        if (target != SkillProgramEffectTarget.Owner || threshold is < 1 or > 64)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: selected-card accumulation requires owner and a threshold of 1..64.");
        var effect = new SkillProgramEffect(Op, target, threshold, r.Condition(),
            stateId: r.RequiredIdentifier("usageId"), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new CreateChoiceResult(effect.ResultBind!, ["crossed", "not-crossed"])];
}
