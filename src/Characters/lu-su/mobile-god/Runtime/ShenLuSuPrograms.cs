using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IShenLuSuHost { SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal abstract class ShenLuSuDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new ShenLuSuHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>op switch
    {
        SkillProgramEffectOp.ResolveShenLuSuTamo=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.ResolveShenLuSuZhimeng=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)],
        _=>[new ReadSelectedTarget()]
    };
}
internal sealed class ResolveShenLuSuTamoDescriptor():ShenLuSuDescriptor(SkillProgramEffectOp.ResolveShenLuSuTamo);
internal sealed class ResolveShenLuSuDingzhouDescriptor():ShenLuSuDescriptor(SkillProgramEffectOp.ResolveShenLuSuDingzhou);
internal sealed class ResolveShenLuSuZhimengDescriptor():ShenLuSuDescriptor(SkillProgramEffectOp.ResolveShenLuSuZhimeng);
internal sealed class ShenLuSuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IShenLuSuHost>().Execute(f,op);
}
internal interface IShenLuSuSelection:IProgramSelectionQuery { bool DingzhouTarget(int owner,int target); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget,38900,SkillProgramEffectOp.ResolveShenLuSuDingzhou)]
internal sealed class ShenLuSuDingzhouTarget:IProgramSelectionTargetFilter
{ public bool Allows(ProgramSelectionContext c,int target,IProgramSelectionQueries q)=>q.Get<IShenLuSuSelection>().DingzhouTarget(c.OwnerSeat,target); }

public enum ShenLuSuStage { SeatChoice,GiftChoice,GiftChildren,ObtainChildren,PeerChoice,DivideChildren }
public sealed record ShenLuSuReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,ShenLuSuStage Stage)
{
    public int Target {get;init;}=-1;
    public int Amount {get;init;}
    public int FixedSeat {get;init;}=-1;
    public IReadOnlyList<int> OriginalOrder {get;init;}=[];
    public IReadOnlyList<int> Order {get;init;}=[];
    public IReadOnlyList<int> Cards {get;init;}=[];
    public IReadOnlyList<int> OwnerShare {get;init;}=[];
    public IReadOnlyList<ShenLuSuDivisionCard> Division {get;init;}=[];
    public IReadOnlyList<ShenLuSuMoveInvoice> MovementInvoices {get;init;}=[];
    public long Before {get;init;}
    public long After {get;init;}
    public long BatchId {get;init;}
    public int MovementCount {get;init;}
    public bool Paid {get;init;}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ShenLuSuReceipt? ShenLuSu {get;init;} }
public sealed record ShenLuSuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int Target,int Amount):IGameEvent;
public sealed record ShenLuSuSeatChosenEvent(long FrameId,int Index,int Seat):IGameEvent;
public sealed record ShenLuSuGiftChosenEvent(long FrameId,int Index):IGameEvent;
public sealed record ShenLuSuDivisionCard(int Index,int CardId,int FromSeat,int ToSeat);
public sealed record ShenLuSuMoveInvoice(int CardId,CardLocation From,CardLocation To,long Sequence);
public sealed record ShenLuSuDivisionEvent(long FrameId,int TargetSeat,int PoolCount,int OwnerCount):IGameEvent;
public sealed record ShenLuSuMovementPaidEvent(long FrameId,ShenLuSuStage Stage,long BatchId,int Count,long Before,long After):IGameEvent;
public sealed record ShenLuSuSeatsAppliedEvent(long FrameId,long WindowId,int OwnerSeat,int FirstSeat,int Count):IGameEvent;

internal static class ShenLuSuComposition
{
    internal const string Tamo="mobile:tamo",Dingzhou="mobile:dingzhou",Zhimeng="mobile:zhimeng";
    [ProgramCatalogValidation(38900)] internal static void Validate(string path,SkillProgram p)
    {
        static bool Owned(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveShenLuSuTamo or SkillProgramEffectOp.ResolveShenLuSuDingzhou or SkillProgramEffectOp.ResolveShenLuSuZhimeng;
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Owned(e.Op)))return;
        static bool Exact(IReadOnlyList<SkillProgramEffect> es,SkillProgramEffectOp op)=>es is [{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]&&e.Op==op;
        bool valid=p.Id switch
        {
            Tamo=>p.Activations.Count==0&&p.Triggers is [{Id:"redistribute-seats",Window:SkillProgramTriggerWindow.GameStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:true}t]&&Exact(t.Effects,SkillProgramEffectOp.ResolveShenLuSuTamo),
            Dingzhou=>p.Triggers.Count==0&&p.Activations is [{Id:"give-and-obtain",MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,TargetKind:SkillProgramTargetKind.OtherLiving,UsesPerTurn:null,UsesPerPhase:1,UsesPerGame:null}a]&&Exact(a.Effects,SkillProgramEffectOp.ResolveShenLuSuDingzhou),
            Zhimeng=>p.Activations.Count==0&&p.Triggers is [{Id:"divide-hands",Window:SkillProgramTriggerWindow.AfterTurnEnded,Subject:SkillProgramTriggerSubject.Owner,TurnOwnerScope:SkillProgramTurnOwnerScope.Own,Optional:true}t]&&Exact(t.Effects,SkillProgramEffectOp.ResolveShenLuSuZhimeng),
            _=>false
        };
        if(!valid||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.ViewAs.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||
            p.Triggers.Any(t=>t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.UsageScope is not null))
            throw new InvalidOperationException(path+": Shen Lu Su requires his exact native setup, once-per-Play exchange and actual own-turn-ended division.");
    }
}
