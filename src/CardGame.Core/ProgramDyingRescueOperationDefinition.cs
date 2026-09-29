namespace CardGame.Core;

/// <summary>Consume one previously selected owner card as a virtual self-targeted Alcohol use by the dying victim.</summary>
internal sealed class UseBoundCardAsDyingAlcoholProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseBoundCardAsDyingAlcohol;
    public override ISkillProgramEffectHandler Handler { get; } = new UseBoundCardAsDyingAlcoholSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.DyingRescue, static (effect, context) => context.DyingRescue(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: dying rescue requires owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireContext(ProgramContextCapability.Dying), new ReadSingleCardSet(effect.SourceBind!),
            new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}

public sealed class UseBoundCardAsDyingAlcoholSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseBoundCardAsDyingAlcohol;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.UseBoundCardAsDyingAlcohol(frame, effect.SourceBind!,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

/// <summary>Resolves a cardless virtual self-targeted Alcohol use by the dying owner, costing the owner's face-up state.</summary>
internal sealed class UseVirtualDyingAlcoholProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualDyingAlcohol;
    public override ISkillProgramEffectHandler Handler { get; } = new UseVirtualDyingAlcoholSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Dying;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.DyingRescue, static (effect, context) => context.DyingRescue(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: dying rescue requires owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition());
        if (effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.condition: the virtual dying rescue must be unconditional or choice-gated.");
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireContext(ProgramContextCapability.Dying)];
}

public sealed class UseVirtualDyingAlcoholSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualDyingAlcohol;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.UseVirtualDyingAlcohol(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
