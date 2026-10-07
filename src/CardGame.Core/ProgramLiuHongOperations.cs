namespace CardGame.Core;

internal interface ILiuHongProgramHost
{
    SkillProgramStepOutcome YujueResolve(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhihuExpire(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome TuxingArmGameDamage(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 鬻爵: the optional play-phase invocation spends one chosen equipment slot,
// takes a hand card from one chosen other character and grants that character
// 执笏 until the owner's next turn. One op drives the whole chained prompt; the
// estimate prices the owner's net card gain through the shared GainCards path.
internal sealed class YujueResolveDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YujueResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new YujueResolveHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class YujueResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YujueResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILiuHongProgramHost)host).YujueResolve(f, e);
}

// 执笏 expiry: the locked turn-start sweep removes grants sourced by the
// current turn's grantor; the estimate stays neutral because the trigger is
// locked and carries no choices.
internal sealed class ZhihuExpireDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhihuExpire;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhihuExpireHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow])];
}

public sealed class ZhihuExpireHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhihuExpire;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILiuHongProgramHost)host).ZhihuExpire(f, e);
}

// 图兴 second clause catch-up: the locked turn-start sweep arms the game-long
// damage bonus once every equipment slot stands abolished; the estimate stays
// neutral because the trigger is locked and carries no choices.
internal sealed class TuxingArmGameDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TuxingArmGameDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new TuxingArmGameDamageHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
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

public sealed class TuxingArmGameDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TuxingArmGameDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILiuHongProgramHost)host).TuxingArmGameDamage(f, e);
}
