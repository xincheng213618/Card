namespace CardGame.Core;

// 追还 arm: the owner secretly picks any living character; the benefit is
// delayed to that character's next preparation phase and stays unpriced here.
internal sealed class ZhuihuanArmDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanArm;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuihuanArmHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public sealed class ZhuihuanArmHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanArm;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IYangWanProgramHost>().ZhuihuanArm(f, e);
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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class ZhuihuanRetaliateHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuihuanRetaliate;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IYangWanProgramHost>().ZhuihuanRetaliate(f, e);
}
