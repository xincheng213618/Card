namespace CardGame.Core;

internal interface IZhaoYanProgramHost
{
    SkillProgramStepOutcome TongxieArm(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome TongxieFollowUp(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome TongxieGuard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 同协 arm: the owner's public selection of up to two fellow members plus the
// unique-least-hand draw; the choice and the draw both belong to this op.
internal sealed class TongxieArmDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TongxieArm;
    public override ISkillProgramEffectHandler Handler { get; } = new TongxieArmHandler();
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

public sealed class TongxieArmHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TongxieArm;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IZhaoYanProgramHost)host).TongxieArm(f, e);
}

// 同协 follow-up: a mandatory completed-slash window whose per-member prompts
// are issued by the op itself, so the estimate stays neutral here.
internal sealed class TongxieFollowUpDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TongxieFollowUp;
    public override ISkillProgramEffectHandler Handler { get; } = new TongxieFollowUpHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RequestSlashByTarget,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

public sealed class TongxieFollowUpHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TongxieFollowUp;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IZhaoYanProgramHost)host).TongxieFollowUp(f, e);
}

// 同协 guard: a mandatory before-damage window whose prevention prompts run in
// the op; the reflection cost is not priced at the trigger boundary.
internal sealed class TongxieGuardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TongxieGuard;
    public override ISkillProgramEffectHandler Handler { get; } = new TongxieGuardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}

public sealed class TongxieGuardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TongxieGuard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IZhaoYanProgramHost)host).TongxieGuard(f, e);
}
