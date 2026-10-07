namespace CardGame.Core;

internal interface IChenDengProgramHost
{
    SkillProgramStepOutcome FengjiRoundChoice(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome XuanhuiSwapEffects(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 丰积: at each round start the owner walks the two options in text order,
// granting -1/+2 redistribution or keeping +1 per declined option for one round.
internal sealed class FengjiRoundChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.FengjiRoundChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new FengjiRoundChoiceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.FengjiRoundChoice,
        static (effect, context) => context.FengjiRoundChoice(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)];
}

public sealed class FengjiRoundChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.FengjiRoundChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IChenDengProgramHost)host).FengjiRoundChoice(f, e);
}

// 旋回: the owner exchanges the round's granted 丰积 effects once; the skill
// then disables until a character dies.
internal sealed class XuanhuiSwapEffectsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XuanhuiSwapEffects;
    public override ISkillProgramEffectHandler Handler { get; } = new XuanhuiSwapEffectsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.XuanhuiSwapEffects,
        static (effect, context) => context.XuanhuiSwapEffects(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class XuanhuiSwapEffectsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XuanhuiSwapEffects;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IChenDengProgramHost)host).XuanhuiSwapEffects(f, e);
}

// Public-only pricing: the choice's guaranteed floor is one declined +1; the
// redistribution path and the swap's +2 landing need private target knowledge.
internal sealed partial class ProgramAiEstimateContext
{
    internal void FengjiRoundChoice(SkillProgramEffect effect)
    {
        _ownerDraw += 1d;
    }

    internal void XuanhuiSwapEffects(SkillProgramEffect effect)
    {
        _ownerDraw += 2d;
    }
}
