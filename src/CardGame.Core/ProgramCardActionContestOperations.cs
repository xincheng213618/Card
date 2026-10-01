namespace CardGame.Core;
internal interface ICardActionContestProgramHost
{
    void ReplaceAllSlashTargets(ProgramSkillFrame frame,string sourceBind);
    SkillProgramStepOutcome StartCardActionPindian(ProgramSkillFrame frame,SkillProgramEffect effect);
}
internal sealed record RequireTopHandPayment(string Name) : ProgramResourceOperation;
internal sealed class ReplaceAllSlashTargetsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ReplaceAllSlashTargets;
    public override ISkillProgramEffectHandler Handler{get;}=new ReplaceAllSlashTargetsHandler();
    public override ProgramContextCapability RequiredCapabilities=>ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,static (_,c)=>c.NullifyCurrentCardEffect());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op","target","sourceBind","condition"); var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),sourceBind:r.RequiredIdentifier("sourceBind"));RequireAlways(e,r.Path);return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted),new ReadSingleCardSet(e.SourceBind!),new RequireTopHandPayment(e.SourceBind!)];
}
internal sealed class StartCardActionPindianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.StartCardActionPindian;
    public override ISkillProgramEffectHandler Handler{get;}=new StartCardActionPindianHandler();
    public override ProgramContextCapability RequiredCapabilities=>ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.StartPindian,static (e,c)=>c.StartPindian(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op","target","opponentRef","resultBind","visibility","condition"); var opponent=r.RequiredParticipantReference("opponentRef"); if(opponent.Kind is not (ProgramParticipantRef.Actor or ProgramParticipantRef.EventTarget)) throw new InvalidOperationException("Card-action Pindian requires actor or eventTarget.");var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),resultBind:r.RequiredIdentifier("resultBind"),visibility:r.RequiredEnum<SkillProgramCardSetVisibility>("visibility"),opponentReference:opponent);RequireAlways(e,r.Path);return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(e.OpponentReference!.Kind==ProgramParticipantRef.Actor?SkillProgramTriggerWindow.CardUseBeforeTargetEffects:SkillProgramTriggerWindow.SlashBeforeResponse),new CreatePindianResult(e.ResultBind!)];
}
public sealed class ReplaceAllSlashTargetsHandler : ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.ReplaceAllSlashTargets;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h){((ICardActionContestProgramHost)h).ReplaceAllSlashTargets(f,e.SourceBind!);return SkillProgramStepOutcome.Continue;}}
public sealed class StartCardActionPindianHandler : ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.StartCardActionPindian;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((ICardActionContestProgramHost)h).StartCardActionPindian(f,e);}
