using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IHappySunQuanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal abstract class HappySunQuanDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new HappySunQuanHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(op switch { SkillProgramEffectOp.GrantHappySunQuanLearning=>SkillProgramTriggerWindow.PlayPhaseStarting,SkillProgramEffectOp.ResolveHappySunQuanShehu=>SkillProgramTriggerWindow.CardUseTargetsFinalized,_=>SkillProgramTriggerWindow.TurnStartBeforeNormalFlow })];
}
internal sealed class GrantHappyLearningDescriptor():HappySunQuanDescriptor(SkillProgramEffectOp.GrantHappySunQuanLearning);
internal sealed class ResolveHappyLearningDescriptor():HappySunQuanDescriptor(SkillProgramEffectOp.ResolveHappySunQuanLearning);
internal sealed class HappyShehuDescriptor():HappySunQuanDescriptor(SkillProgramEffectOp.ResolveHappySunQuanShehu);
internal sealed class HappyDingliDescriptor():HappySunQuanDescriptor(SkillProgramEffectOp.ResolveHappySunQuanDingli);
internal sealed class HappySunQuanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHappySunQuanHost>().Resolve(f,op);
}
internal static class HappySunQuanComposition
{
    internal const string Quanxue="happy:quanxue",Learning="happy:learning",Shehu="happy:shehu",Dingli="happy:dingli";
    internal static bool Owns(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.GrantHappySunQuanLearning or SkillProgramEffectOp.ResolveHappySunQuanLearning or SkillProgramEffectOp.ResolveHappySunQuanShehu or SkillProgramEffectOp.ResolveHappySunQuanDingli;
    [ProgramCatalogValidation(40940)] internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Any(e=>Owns(e.Op)))return;
        if(p.Triggers is not [var t]||t.Effects is not [{Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always} e]||
            t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.MarkerCost is not null||p.Activations.Count!=0||p.Modifiers.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||
            !(p.Id switch {
                Quanxue=>t.Id=="grant-study"&&t.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&t.Optional&&e.Op==SkillProgramEffectOp.GrantHappySunQuanLearning&&t.UsageScope is null,
                Learning=>t.Id=="resolve-study"&&t.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&!t.Optional&&e.Op==SkillProgramEffectOp.ResolveHappySunQuanLearning&&t.UsageScope is null,
                Shehu=>t.Id=="slash-studying"&&t.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&!t.Optional&&e.Op==SkillProgramEffectOp.ResolveHappySunQuanShehu&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&t.CardKinds.Count==3&&t.CardKinds.All(k=>k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)&&t.UsageScope is null,
                Dingli=>t.Id=="removed-study"&&t.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&t.Optional&&e.Op==SkillProgramEffectOp.ResolveHappySunQuanDingli&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving&&t.UsageScope==SkillUsageScope.Round&&t.UsageLimit==1,
                _=>false }))throw new InvalidOperationException(path+": Happy Sun Quan requires his exact printed Quanxue/Shehu/Dingli and issued holder obligation.");
    }
}
public enum HappySunQuanStage { Targets,LearningChoice,HpLoss,RecoveryChildren,Drawing,MovementChildren,Discard,Finished }
public sealed record HappySunQuanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn)
{
    public HappySunQuanStage Stage {get;init;}
    public long ActionId {get;init;}
    public int Target {get;init;}
    public int Index {get;init;}
    public int Amount {get;init;}
    public int Branch {get;init;}
    public int ChosenCard {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public NativeDrawOutcome? Draw {get;init;}
    private readonly IReadOnlyList<int> _targets=[];
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<CardMovementRecord> _paid=[];
    public IReadOnlyList<CardMovementRecord> Paid {get=>_paid;init=>_paid=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<HappySunQuanPayment> _payments=[];
    public IReadOnlyList<HappySunQuanPayment> Payments {get=>_payments;init=>_payments=Array.AsReadOnly(value.ToArray());}
}
public sealed record HappySunQuanPayment(int Index,int Target,int CardId,long Before,long After)
{
    private readonly IReadOnlyList<CardMovementRecord> _movements=[];
    public IReadOnlyList<CardMovementRecord> Movements {get=>_movements;init=>_movements=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappySunQuanReceipt? HappySunQuan {get;init;} }
public sealed record HappyLearningStartReceipt(int ActualTurn,int HolderSeat)
{
    private readonly IReadOnlyList<HappyXueGrantEvent> _grants=[];
    public IReadOnlyList<HappyXueGrantEvent> Grants {get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramLifecycleTriggerWindowFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyLearningStartReceipt? HappyLearningStart {get;init;} }
public sealed record HappyXueGrantEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Target,int Before,int After):IGameEvent;
public sealed record HappyLearningIssuedEvent(long WindowId,int ActualTurn,int HolderSeat,int SourceCount,int Total):IGameEvent;
public sealed record HappyLearningRemovedEvent(long FrameId,long WindowId,int ActualTurn,int HolderSeat,int Total):IGameEvent;
public sealed record HappyLearningChoiceEvent(long FrameId,long WindowId,int ActualTurn,int HolderSeat,int Branch):IGameEvent;
public sealed record HappyLearningReturnedEvent(long WindowId,int ActualTurn,int HolderSeat):IGameEvent;
public sealed record HappySunQuanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn,long ActionId,int Target,int Amount):IGameEvent;
public sealed record HappySunQuanMovementEvent(long FrameId,int Index,long Before,long After,int Count):IGameEvent;
public sealed record HappySunQuanCompletedEvent(long FrameId):IGameEvent;
public sealed record HappyShehuTargetEvent(long FrameId,int Index,long ActionId,int Target,int MarkerCount):IGameEvent;
public sealed record HappyShehuSkippedEvent(long FrameId,int Index,int Target):IGameEvent;
public sealed record HappyDingliBenefitEvent(long FrameId,long WindowId,int OtherSeat,int OwnerHp,int OtherHp,int Branch,int Amount):IGameEvent;
