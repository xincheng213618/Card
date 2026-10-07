namespace CardGame.Core;

internal interface IHuangChengYanProgramHost
{
    SkillProgramStepOutcome JiezhenReplaceSkills(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome JiezhenRestoreSkills(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZecaiRoundSettlement(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YinshiPreventSourcelessDamage(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YinshiClaimBaguaJudgmentCard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 解阵 replace: disables the selected counterpart's replaceable skills and
// grants 八阵 until the restoration trigger fires.
internal sealed class JiezhenReplaceSkillsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenReplaceSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new JiezhenReplaceSkillsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting), new ReadSelectedTarget()];
}

public sealed class JiezhenReplaceSkillsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenReplaceSkills;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IHuangChengYanProgramHost)host).JiezhenReplaceSkills(f, e);
}

// 解阵 restore: returns the replaced skills, removes the granted 八阵 and takes
// one card from the converted character's zones.
internal sealed class JiezhenRestoreSkillsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenRestoreSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new JiezhenRestoreSkillsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner,
            1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class JiezhenRestoreSkillsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenRestoreSkills;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IHuangChengYanProgramHost)host).JiezhenRestoreSkills(f, e);
}

// 择才 settlement: at the owner's first turn start after a round boundary the
// finished round settles once; the limited activation offers the 集智 grant.
internal sealed class ZecaiRoundSettlementDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZecaiRoundSettlement;
    public override ISkillProgramEffectHandler Handler { get; } = new ZecaiRoundSettlementHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class ZecaiRoundSettlementHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZecaiRoundSettlement;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IHuangChengYanProgramHost)host).ZecaiRoundSettlement(f, e);
}

// 隐世 ①: prevents the first damage of the turn that no colored game card caused.
internal sealed class YinshiPreventSourcelessDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiPreventSourcelessDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new YinshiPreventSourcelessDamageHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class YinshiPreventSourcelessDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiPreventSourcelessDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IHuangChengYanProgramHost)host).YinshiPreventSourcelessDamage(f, e);
}

// 隐世 ②: claims the effective judgment card of any 八卦阵 judgment.
internal sealed class YinshiClaimBaguaJudgmentCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiClaimBaguaJudgmentCard;
    public override ISkillProgramEffectHandler Handler { get; } = new YinshiClaimBaguaJudgmentCardHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner,
            1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.JudgmentFinalized)];
}

public sealed class YinshiClaimBaguaJudgmentCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YinshiClaimBaguaJudgmentCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IHuangChengYanProgramHost)host).YinshiClaimBaguaJudgmentCard(f, e);
}
