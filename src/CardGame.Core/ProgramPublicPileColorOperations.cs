namespace CardGame.Core;
internal interface IPublicPileColorHost { SkillProgramStepOutcome ExecutePublicPileColor(SkillProgramEffect effect, ProgramSkillFrame frame); }
internal sealed record ConsumePublicPileCardSet(string Name):ProgramResourceOperation;
internal sealed record RequirePublicPileExchangeBoundary : ProgramResourceOperation;
internal sealed record RequirePublicPileColorActivation : ProgramResourceOperation;
internal abstract class PublicPileColorDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e,c) => { if(e.Op==SkillProgramEffectOp.PublicPileColorDamage) c.Damage(new(SkillProgramEffectOp.Damage,SkillProgramEffectTarget.SelectedTarget,1,e.Condition)); else c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","sourceBind","skillIds","condition");
        var skills=r.Has("skillIds")?r.RequiredIdentifierArray("skillIds"):[];
        if(Op==SkillProgramEffectOp.StoreBoundHandInPublicPile ? skills.Count!=0 : skills.Count!=1) throw new InvalidOperationException("Color public pile requires exactly one referenced source skill.");
        var bind=r.Has("sourceBind")?r.RequiredIdentifier("sourceBind"):null;
        if((Op==SkillProgramEffectOp.StoreBoundHandInPublicPile)!=(bind is not null)) throw new InvalidOperationException("Only hand storage requires an owned card binding.");
        var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),1,r.Condition(),sourceBind:bind,skillIds:skills);RequireAlways(e,r.Path);return e;
    }
}
internal sealed class StoreBoundHandInPublicPileDescriptor : PublicPileColorDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.StoreBoundHandInPublicPile;
    public override ISkillProgramEffectHandler Handler {get;}=new StoreBoundHandInPublicPileHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting),new RequireOwnedCardSet(e.SourceBind!,SkillProgramEffectTarget.Owner,1,[CardZoneKind.Hand]),new ReadSingleCardSet(e.SourceBind!),new ConsumePublicPileCardSet(e.SourceBind!)];
}
internal sealed class PublicPileColorDamageDescriptor : PublicPileColorDescriptor
{
    public override bool UsesConversionPolarity=>true;
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PublicPileColorDamage;
    public override ISkillProgramEffectHandler Handler {get;}=new PublicPileColorDamageHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequirePublicPileColorActivation()];
}
internal sealed class RewardDiscardedActionColorDescriptor : PublicPileColorDescriptor
{
    public override bool UsesConversionPolarity=>true;
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RewardDiscardedActionColor;
    public override ISkillProgramEffectHandler Handler {get;}=new RewardDiscardedActionColorHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}
public abstract class PublicPileColorHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op {get;}
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IPublicPileColorHost)h).ExecutePublicPileColor(e,f);
}
public sealed class StoreBoundHandInPublicPileHandler:PublicPileColorHandler {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.StoreBoundHandInPublicPile;}
public sealed class PublicPileColorDamageHandler:PublicPileColorHandler {
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PublicPileColorDamage;}
public sealed class RewardDiscardedActionColorHandler:PublicPileColorHandler {
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RewardDiscardedActionColor;}
public sealed record PublicPileColorPayment(int CardId,CardLocation From,CardLocation PileLocation,string PileInstance,bool IsRed,int TargetSeat);
public sealed record ActionCardsDiscardedEvent(long BatchId,long ActionId,int ActorSeat,bool? IsRed,int TurnNumber):IGameEvent;
public sealed record ActionColorRewardOfferedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,long ParentFrameId,long ActionId):IGameEvent;

internal sealed record RequirePrecedingOwnerDraw:ProgramResourceOperation;
internal sealed class AwaitOwnedCardMovementDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AwaitOwnedCardMovement;
    public override ISkillProgramEffectHandler Handler {get;}=new AwaitOwnedCardMovementHandler();
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r){r.AllowOnly("op","target","condition");var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition());RequireAlways(e,r.Path);return e;}
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequirePrecedingOwnerDraw()];
}
public sealed class AwaitOwnedCardMovementHandler:ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.AwaitOwnedCardMovement;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>((IBoundCardMovementContinuationHost)h).AwaitBoundCardMovements(f.Id,f.OwnerSeat);}
internal sealed class AwaitBoundCardMovementsDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AwaitBoundCardMovements;
    public override ISkillProgramEffectHandler Handler {get;}=new AwaitBoundCardMovementsHandler();
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r){r.AllowOnly("op","target","condition");var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition());RequireAlways(e,r.Path);return e;}
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
public sealed class AwaitBoundCardMovementsHandler:ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.AwaitBoundCardMovements;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>((IBoundCardMovementContinuationHost)h).AwaitBoundCardMovements(f.Id,f.OwnerSeat);}
