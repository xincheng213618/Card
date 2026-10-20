namespace CardGame.Core;

// 抗歌 choice: the owner publicly marks one other living character for the rest
// of the game; the delayed payoffs stay unpriced here.
internal sealed class KanggeChooseTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeChooseTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new KanggeChooseTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class KanggeChooseTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeChooseTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITangJiProgramHost>().KanggeChooseTarget(f, e);
}

// 抗歌 gain: one candidate per observed hand-card gain of the marked character;
// the shared collector already filtered to attributed marked gains.
internal sealed class KanggeGainDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeGainDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new KanggeGainDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];
}

public sealed class KanggeGainDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeGainDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITangJiProgramHost>().KanggeGainDraw(f, e);
}

// 抗歌 rescue: the mandatory dying trigger filters to the marked victim inside
// the operation, then presents the voluntary recovery choice itself; the
// once-per-round ledger derives from committed evidence.
internal sealed class KanggeHealVictimDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeHealVictim;
    public override ISkillProgramEffectHandler Handler { get; } = new KanggeHealVictimHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RecoverTo,
        static (effect, context) => context.RecoverOtherDyingVictimTo(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var hp = r.RequiredInt("amount");
        if (hp != 1) throw new InvalidOperationException($"Invalid skill program at {r.Path}: kangge dying recovery HP must be exactly 1.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), hp, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}

public sealed class KanggeHealVictimHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeHealVictim;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITangJiProgramHost>().KanggeHealVictim(f, e);
}

// 抗歌 price: a mandatory death settlement whose marked-victim filter runs
// inside the operation before the discard-all-and-hp-loss payment.
internal sealed class KanggeDeathPriceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeDeathPrice;
    public override ISkillProgramEffectHandler Handler { get; } = new KanggeDeathPriceHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];
}

public sealed class KanggeDeathPriceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.KanggeDeathPrice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITangJiProgramHost>().KanggeDeathPrice(f, e);
}
