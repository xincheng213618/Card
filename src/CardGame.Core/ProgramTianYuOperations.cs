namespace CardGame.Core;

internal interface ITianYuProgramHost
{
    SkillProgramStepOutcome SaodiExpandTargets(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuitaoMarkTarget(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuitaoRevoke(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 扫狄: one optional finalized-target offer turns every character standing
// between the owner and the single designated target into targets of the same
// card. The estimate prices the owner's expanded attack as a one-card gain so
// the optional trigger path accepts it.
internal sealed class SaodiExpandTargetsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SaodiExpandTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new SaodiExpandTargetsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
        new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor,
            [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel,
             CardKind.Dismantlement, CardKind.Snatch, CardKind.FireAttack, CardKind.IronChain,
             CardKind.BorrowedSword])
    ];
}

public sealed class SaodiExpandTargetsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SaodiExpandTargets;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ITianYuProgramHost)host).SaodiExpandTargets(f, e);
}

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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class ZhuitaoMarkTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuitaoMarkTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ITianYuProgramHost)host).ZhuitaoMarkTarget(f, e);
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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class ZhuitaoRevokeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuitaoRevoke;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ITianYuProgramHost)host).ZhuitaoRevoke(f, e);
}
