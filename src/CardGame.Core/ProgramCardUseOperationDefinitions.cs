namespace CardGame.Core;

internal sealed class UseSelectedCardsAsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseSelectedCardsAs;
    public override ISkillProgramEffectHandler Handler { get; } = new UseSelectedCardsAsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (effect, context) => context.UseSelectedCardsAs(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "outputKind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget && target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: useSelectedCardsAs requires selectedTarget or owner.");
        var output = r.RequiredEnum<CardKind>("outputKind");
        if (output is not (CardKind.Slash or CardKind.FireSlash or CardKind.Peach or CardKind.ArrowBarrage) ||
            output == CardKind.Slash && target != SkillProgramEffectTarget.SelectedTarget ||
            output == CardKind.ArrowBarrage && target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.outputKind: Slash needs one target; Arrow Barrage targets all other players.");
        var effect = new SkillProgramEffect(
            Op,
            target,
            0,
            r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"),
            outputKind: output);
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.Target == SkillProgramEffectTarget.SelectedTarget
            ? [new ReadSelectedTarget(), new ConsumeSelectedCards(0)]
            : [new ConsumeSelectedCards(0)];
}

internal sealed class UseBoundCardByTargetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseBoundCardByTarget;
    public override ISkillProgramEffectHandler Handler { get; } =
        new UseBoundCardByTargetSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.UseBoundCardByTarget,
        static (effect, context) => context.UseBoundCardByTarget(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: useBoundCardByTarget requires selectedTarget.");
        return new SkillProgramEffect(
            Op,
            target,
            0,
            r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new ReadCardSet(effect.SourceBind!)];
}

internal sealed class GrantTurnSkillsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnSkillsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GrantTurnSkills,
        static (effect, context) => context.GrantTurnSkills(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "skillIds", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: grantTurnSkills requires owner.");
        var ids = r.RequiredIdentifierArray("skillIds");
        if (ids.Count == 0)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.skillIds: must not be empty.");
        return new SkillProgramEffect(Op, target, 0, r.Condition(), skillIds: ids);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class UseAllHandCardsAsOrdinaryTrickProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick;
    public override ISkillProgramEffectHandler Handler { get; } =
        new UseAllHandCardsAsOrdinaryTrickSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (effect, context) => context.UseSelectedCardsAs(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "viewAsId", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: useAllHandCardsAsOrdinaryTrick requires owner.");
        var effect = new SkillProgramEffect(
            Op,
            target,
            0,
            r.Condition(),
            sourceBind: r.RequiredIdentifier("viewAsId"));
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ConsumeSelectedCards(0)];
}
