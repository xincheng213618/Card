namespace CardGame.Core;

public sealed record FirstGameDomainCrossingEvent(int TurnNumber,long BatchId,bool MovedOut,int MovementOrdinal):IGameEvent;
public sealed record ProgramDomainCrossingDraft(string Stage,bool MovedOut,int OtherSeat=-1,int ParticipantIndex=0,
    string? ReferenceSkillId=null,string? ReferenceSkillInstanceId=null,CardLocation? ReferencePileLocation=null);
public sealed record ProgramPileEquipmentDraft(string Stage,CardLocation PileLocation,string PileInstance,
    IReadOnlyList<int> SelectedIds,int? RecipientSeat=null,bool HadActualEquipmentUse=false,long? ActiveUseFrameId=null,int? ActiveCardId=null);
internal interface IGameDomainProgramHost
{SkillProgramStepOutcome ExecuteGameDomain(SkillProgramEffect effect,ProgramSkillFrame frame);}
internal abstract class GameDomainOperationDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","skillIds","condition");
        var refs=r.Has("skillIds")?r.RequiredIdentifierArray("skillIds"):[];
        if(refs.Count!=(Op==SkillProgramEffectOp.ResolveFirstGameDomainCrossing?1:0))throw new InvalidOperationException("A first domain crossing needs exactly one source pile reference.");
        var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),skillIds:refs);RequireAlways(e,r.Path);return e;
    }
}
internal sealed class ResolveFirstGameDomainCrossingDescriptor:GameDomainOperationDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFirstGameDomainCrossing;
    public override ISkillProgramEffectHandler Handler{get;}=new ResolveFirstGameDomainCrossingHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.FirstGameDomainCrossing)];
}
internal sealed class StoreArbitraryOwnedPublicPileDescriptor:GameDomainOperationDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.StoreArbitraryOwnedPublicPile;
    public override ISkillProgramEffectHandler Handler{get;}=new StoreArbitraryOwnedPublicPileHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class UsePublicPileEquipmentSequenceDescriptor:GameDomainOperationDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.UsePublicPileEquipmentSequence;
    public override ISkillProgramEffectHandler Handler{get;}=new UsePublicPileEquipmentSequenceHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public abstract class GameDomainOperationHandler:ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op{get;}
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IGameDomainProgramHost)h).ExecuteGameDomain(e,f);
}
public sealed class ResolveFirstGameDomainCrossingHandler:GameDomainOperationHandler{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFirstGameDomainCrossing;}
public sealed class StoreArbitraryOwnedPublicPileHandler:GameDomainOperationHandler{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.StoreArbitraryOwnedPublicPile;}
public sealed class UsePublicPileEquipmentSequenceHandler:GameDomainOperationHandler{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.UsePublicPileEquipmentSequence;}
