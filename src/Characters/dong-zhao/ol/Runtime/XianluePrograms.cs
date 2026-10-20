namespace CardGame.Core;
internal sealed class XianlueRecordDescriptor : DongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRecord;
    public override ISkillProgramEffectHandler Handler { get; } = new XianlueRecordHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class XianlueRedistributeDescriptor : DongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRedistribute;
    public override ISkillProgramEffectHandler Handler { get; } = new XianlueRedistributeHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
public sealed class XianlueRecordHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRecord;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) => h.GetCapability<IDongZhaoProgramHost>().XianlueRecord(f);
}
public sealed class XianlueRedistributeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XianlueRedistribute;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) => h.GetCapability<IDongZhaoProgramHost>().XianlueRedistribute(f);
}
