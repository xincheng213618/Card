namespace CardGame.Core;

internal interface IPanShuProgramHost
{
    SkillProgramStepOutcome ZhirenReorderTopByLength(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhirenResolveFieldTiers(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YanerResolvePairBenefit(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 织纴 tier 1: the used card's printed name length X views the top X cards and
// rearranges them over the draw-pile top and bottom (classic top-reorder shape).
internal sealed class ZhirenReorderTopByLengthDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenReorderTopByLength;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhirenReorderTopByLengthHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.Move(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireContext(ProgramContextCapability.CardAction)];
}

public sealed class ZhirenReorderTopByLengthHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenReorderTopByLength;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IPanShuProgramHost)host).ZhirenReorderTopByLength(f, e);
}

// 织纴 tiers 2-4: with X >= 2 discard at most one field equipment and one field
// delayed trick, with X >= 3 recover one HP, and with X >= 4 draw three cards.
internal sealed class ZhirenResolveFieldTiersDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenResolveFieldTiers;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhirenResolveFieldTiersHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireContext(ProgramContextCapability.CardAction)];
}

public sealed class ZhirenResolveFieldTiersHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenResolveFieldTiers;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IPanShuProgramHost)host).ZhirenResolveFieldTiers(f, e);
}

// 燕尔 follow-up: draws two pairs (the owner first, then the last-hand loser)
// and when both pairs share one category set, grants the paired 织纴 its
// own-turn-free modification until the owner's next turn and recovers the loser.
internal sealed class YanerResolvePairBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YanerResolvePairBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new YanerResolvePairBenefitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition", "skillIds", "stateId");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            skillIds: r.RequiredIdentifierArray("skillIds"),
            stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path);
        if (effect.SkillIds.Count != 1)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.skillIds: yaner targets exactly one paired skill.");
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}

public sealed class YanerResolvePairBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YanerResolvePairBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IPanShuProgramHost)host).YanerResolvePairBenefit(f, e);
}
