namespace CardGame.Core;

internal sealed record RequireActivationEntry : ProgramResourceOperation;
internal interface IAlternatingSuitAndCompletedTopHost
{
    SkillProgramStepOutcome BeginAlternatingSuitDrawDiscard(ProgramSkillFrame frame);
    SkillProgramStepOutcome BeginFirstCategoryCompletedTop(ProgramSkillFrame frame);
}
internal abstract class AlternatingSuitAndCompletedTopDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e,c)=>c.Draw(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.Owner) throw new InvalidOperationException($"Invalid program at {r.Path}: owner required.");
        var e=new SkillProgramEffect(Op,SkillProgramEffectTarget.Owner,1,r.Condition());RequireAlways(e,r.Path);return e;
    }
}
internal sealed class AlternatingSuitDrawDiscardDescriptor : AlternatingSuitAndCompletedTopDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AlternatingSuitDrawDiscard;
    public override ISkillProgramEffectHandler Handler { get; }=new AlternatingSuitDrawDiscardHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireActivationEntry()];
}
public sealed class AlternatingSuitDrawDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.AlternatingSuitDrawDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IAlternatingSuitAndCompletedTopHost)h).BeginAlternatingSuitDrawDiscard(f);
}
internal sealed class FirstCategoryCompletedTopDescriptor : AlternatingSuitAndCompletedTopDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.FirstCategoryCompletedTop;
    public override ISkillProgramEffectHandler Handler { get; }=new FirstCategoryCompletedTopHandler();
    public override ProgramContextCapability RequiredCapabilities=>ProgramContextCapability.CardAction;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted),new RequireCardActionActor()];
}
public sealed class FirstCategoryCompletedTopHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.FirstCategoryCompletedTop;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IAlternatingSuitAndCompletedTopHost)h).BeginFirstCategoryCompletedTop(f);
}
public sealed record AlternatingSuitTopDraft(string Mode,string Stage,int Required,IReadOnlyList<int> CardIds,IReadOnlyList<int> SelectedIds,long? ActionId=null,IReadOnlyList<Suit>? PaidSuits=null);
public sealed record AlternatingSuitStateCommittedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,bool NextYin,int TurnNumber,int PhaseInstanceId) : IGameEvent;
public sealed record PlayPhaseSuitAllowanceGrantedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,int TurnNumber,int PhaseInstanceId,IReadOnlyList<Suit> Suits,IReadOnlyList<int> DiscardedCardIds) : IGameEvent;
