namespace CardGame.Core;

public sealed record ProgramJiezhenConvertedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, IReadOnlyList<string> ReplacedSkillIds) : IGameEvent;
public sealed record ProgramJiezhenRestoredEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetSeat, IReadOnlyList<string> RestoredSkillIds, int TakenCardId) : IGameEvent;

// 解阵 replace: disables the selected counterpart's replaceable skills and
// grants 八阵 until the restoration trigger fires.
internal sealed class JiezhenReplaceSkillsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenReplaceSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new JiezhenReplaceSkillsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting), new ReadSelectedTarget()];
}

public sealed class JiezhenReplaceSkillsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenReplaceSkills;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHuangChengYanProgramHost>().JiezhenReplaceSkills(f, e);
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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class JiezhenRestoreSkillsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JiezhenRestoreSkills;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHuangChengYanProgramHost>().JiezhenRestoreSkills(f, e);
}
