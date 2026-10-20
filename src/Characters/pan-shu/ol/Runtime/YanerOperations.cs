namespace CardGame.Core;

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
        host.GetCapability<IPanShuProgramHost>().YanerResolvePairBenefit(f, e);
}
