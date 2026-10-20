namespace CardGame.Core;

// 追讨 mark: the optional preparation-phase invocation points at one character
// whose distance the owner has not reduced this way; the estimate prices the
// chase as a one-card gain so the optional trigger path accepts it.
internal sealed class ZhuitaoMarkTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuitaoMarkTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuitaoMarkTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class ZhuitaoMarkTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuitaoMarkTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITianYuProgramHost>().ZhuitaoMarkTarget(f, e);
}

// 追讨 revoke: the locked damage sweep drops every directed distance reduction
// the owner holds against the damaged character; the estimate stays neutral
// because the trigger is locked and carries no choices.
internal sealed class ZhuitaoRevokeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuitaoRevoke;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuitaoRevokeHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class ZhuitaoRevokeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuitaoRevoke;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITianYuProgramHost>().ZhuitaoRevoke(f, e);
}
