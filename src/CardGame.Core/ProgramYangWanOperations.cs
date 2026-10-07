namespace CardGame.Core;

internal interface IYangWanProgramHost
{
    SkillProgramStepOutcome YouyanGainSuitCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuihuanArm(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuihuanRetaliate(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 诱言: the own-discarded suits of this batch stay out of the gain; the host
// reads the window batch and takes one top card of every other suit.
internal sealed class YouyanGainSuitCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YouyanGainSuitCards;
    public override ISkillProgramEffectHandler Handler { get; } = new YouyanGainSuitCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 3, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

public sealed class YouyanGainSuitCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YouyanGainSuitCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYangWanProgramHost)host).YouyanGainSuitCards(f, e);
}

// 追还 arm: the owner secretly picks any living character; the benefit is
// delayed to that character's next preparation phase and stays unpriced here.
internal sealed class ZhuihuanArmDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanArm;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuihuanArmHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public sealed class ZhuihuanArmHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanArm;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYangWanProgramHost)host).ZhuihuanArm(f, e);
}

// 追还 punishment: a mandatory preparation-phase settlement whose damager
// ledger derives from committed history; the estimate stays neutral because the
// trigger is not optional.
internal sealed class ZhuihuanRetaliateDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanRetaliate;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuihuanRetaliateHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class ZhuihuanRetaliateHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanRetaliate;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYangWanProgramHost)host).ZhuihuanRetaliate(f, e);
}
