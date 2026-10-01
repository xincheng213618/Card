namespace CardGame.Core;
internal interface IDeferredAlignmentProgramHost
{
    void ScheduleDeferredHandAlignment(ProgramSkillFrame frame, string continuationId);
    SkillProgramStepOutcome ResolveDeferredHandAlignment(ProgramSkillFrame frame);
}
internal sealed class ScheduleDeferredHandAlignmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleDeferredHandAlignment;
    public override ISkillProgramEffectHandler Handler { get; } = new ScheduleDeferredHandAlignmentHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.DeferredHandAlignment());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var e = new SkillProgramEffect(Op,r.RequiredEnum<SkillProgramEffectTarget>("target"),0,r.Condition(),stateId:r.RequiredIdentifier("stateId"));
        if(e.Target!=SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("Deferred alignment schedules one selected other participant.");
        RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget(),
            new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class ResolveDeferredHandAlignmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDeferredHandAlignment;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveDeferredHandAlignmentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.Draw(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var e=new SkillProgramEffect(Op,r.RequiredEnum<SkillProgramEffectTarget>("target"),0,r.Condition(),resultBind:r.RequiredIdentifier("resultBind"));
        if(e.Target!=SkillProgramEffectTarget.Owner)throw new InvalidOperationException("Deferred alignment resolver uses an owner placeholder.");RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding),new ReadSelectedTarget()];
}
public sealed class ScheduleDeferredHandAlignmentHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ScheduleDeferredHandAlignment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h){((IDeferredAlignmentProgramHost)h).ScheduleDeferredHandAlignment(f,e.StateId!);return SkillProgramStepOutcome.Continue;}
}
public sealed class ResolveDeferredHandAlignmentHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveDeferredHandAlignment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IDeferredAlignmentProgramHost)h).ResolveDeferredHandAlignment(f);
}
