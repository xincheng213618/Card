namespace CardGame.Core;

internal interface IYangYiProgramHost
{
    SkillProgramStepOutcome JuanxiaDeclareTricks(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome JuanxiaRetaliation(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 狷狭 launch: at the owner's ending phase the owner picks one other character
// and declares up to three differently named single-target ordinary tricks
// against them; the launch then records the retaliation debt.
internal sealed class JuanxiaDeclareTricksDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JuanxiaDeclareTricks;
    public override ISkillProgramEffectHandler Handler { get; } = new JuanxiaDeclareTricksHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.JuanxiaTrickUse,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding),
        new ReadSelectedTarget()
    ];
}

public sealed class JuanxiaDeclareTricksHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JuanxiaDeclareTricks;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYangYiProgramHost)host).JuanxiaDeclareTricks(f, e);
}

// 狷狭 retaliation: at a debtor's ending phase the outstanding committed debt
// lets the debtor view as using that many Slashes against the owner.
internal sealed class JuanxiaRetaliationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JuanxiaRetaliation;
    public override ISkillProgramEffectHandler Handler { get; } = new JuanxiaRetaliationHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.JuanxiaRetaliationChoice,
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

public sealed class JuanxiaRetaliationHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JuanxiaRetaliation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IYangYiProgramHost)host).JuanxiaRetaliation(f, e);
}

// The 狷狭 tricks are worth about a card of tempo to the owner while the owed
// retaliation is the debtor's own choice, so the launch prices as one draw and
// the settlement carries no owner-side estimate.
internal sealed partial class ProgramAiEstimateContext
{
    internal void JuanxiaTrickUse(SkillProgramEffect effect)
    {
        _targetDraw += 0.5d;
        _otherAdjustment += 0.5d;
    }
}
