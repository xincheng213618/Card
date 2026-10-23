using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum HappyPangTongPlanKind { Upper, Middle, Lower }
public enum HappyPangTongStage { Grant, NextPlan, MiddleCard, GainChildren, UpperTarget, UpperCard, UpperUse, UpperReturned, LowerChoice, LowerDrawing, LowerDrawChildren, LowerRecoveryChildren, Settling, RewardDrawing, RewardChildren, GrowthMaximumChildren, GrowthHpChildren }
public sealed record HappyPangTongPlanIssuedEvent(long PlanId,CardConversionSource Source,string GameplayHash,HappyPangTongPlanKind Kind,int TargetSeat,int IssuedTurn,long IssuedActionBoundary):IGameEvent;
public sealed record HappyPangTongPlanSettledEvent(long RootFrameId,long PlanId,int OwnerSeat,int TargetSeat,HappyPangTongPlanKind Kind,int ActualTurn,int ActualTurnOwner,bool Success):IGameEvent;
public sealed record HappyPangTongStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentId,SkillProgramEffectOp Operation,int ActualTurn):IGameEvent;
public sealed record HappyPangTongDueIssuedEvent(long FrameId,int Ordinal,long PlanId,long ParentId):IGameEvent;
public sealed record HappyPangTongRecoveryIssuedEvent(long FrameId,long PlanId,int TargetSeat,int Amount):IGameEvent;
public sealed record HappyPangTongSlashRecordedEvent(int ActualTurn,int ActorSeat,long UseId,long ActionId,int Ordinal):IGameEvent;
public sealed record HappyPangTongDamageRecordedEvent(int ActualTurn,int ActualTurnOwner,long DamageId,int SourceSeat,int Amount):IGameEvent;
public sealed record HappyPangTongBenefitIssuedEvent(long FrameId,long PlanId,string Branch,int Amount,int MaximumBefore,int MaximumAfter):IGameEvent;
public sealed record HappyPangTongDrawReturnedEvent(long FrameId,long PlanId,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record HappyPangTongUpperBoundEvent(long FrameId,long PlanId,long UseId,long ActionId):IGameEvent;
public sealed record HappyPangTongUpperReturnedEvent(long FrameId,long PlanId,long UseId,long ActionId):IGameEvent;
public sealed record HappyPangTongSettledReturn(long RootId,long PlanId,bool Success,int ActualTurn);
public sealed record HappyPangTongUpperReturn(long RootId,long PlanId,long UseId,long ActionId);
public sealed record HappyPangTongReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn,HappyPangTongStage Stage)
{
    private readonly IReadOnlyList<long> _plans=Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> Plans {get=>_plans;init=>_plans=Array.AsReadOnly(value.ToArray());}
    public int Cursor{get;init;}
    public long CurrentPlanId{get;init;}
    public int SlashTarget{get;init;}=-1;
    public long NativeUseId{get;init;}
    public long NativeActionId{get;init;}
    public int GainCardId{get;init;}
    public CardLocation? GainFrom{get;init;}
    public long Before{get;init;}
    public long After{get;init;}
    public int DrawCount{get;init;}
    public NativeDrawOutcome? DrawOutcome{get;init;}
    public int MaximumBefore{get;init;}
    public int MaximumAfter{get;init;}
    public bool LowerAccepted{get;init;}
    public bool FactionPending{get;init;}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyPangTongReceipt? HappyPangTong{get;init;}}
public sealed partial record ProgramLifecycleTriggerWindowFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyPangTongSettledReturn? HappyPlanReturn{get;init;}}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyPangTongUpperReturn? HappyPangTongUpper{get;init;}}
internal interface IHappyPangTongHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp operation);}
internal abstract class HappyPangTongDescriptor(SkillProgramEffectOp operation):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>operation;
    public override ISkillProgramEffectHandler Handler{get;}=new HappyPangTongHandler(operation);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>operation switch
    {
        SkillProgramEffectOp.IssueHappyPangTongPlan=>[new RequireTriggerWindows([SkillProgramTriggerWindow.RoundStarting,SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.HappyPlanSettled])],
        SkillProgramEffectOp.ResolveHappyPangTongMiddle=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)],
        SkillProgramEffectOp.ResolveHappyPangTongEnding=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)],
        _=>[new RequireTriggerWindow(SkillProgramTriggerWindow.HappyPlanSettled)]
    };
}
internal sealed class IssueHappyPangTongPlanDescriptor():HappyPangTongDescriptor(SkillProgramEffectOp.IssueHappyPangTongPlan);
internal sealed class ResolveHappyPangTongMiddleDescriptor():HappyPangTongDescriptor(SkillProgramEffectOp.ResolveHappyPangTongMiddle);
internal sealed class ResolveHappyPangTongEndingDescriptor():HappyPangTongDescriptor(SkillProgramEffectOp.ResolveHappyPangTongEnding);
internal sealed class ResolveHappyPangTongLanhaiDescriptor():HappyPangTongDescriptor(SkillProgramEffectOp.ResolveHappyPangTongLanhai);
internal sealed class HappyPangTongHandler(SkillProgramEffectOp operation):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHappyPangTongHost>().Resolve(f,operation);
}
internal static class HappyPangTongComposition
{
    internal const string Lunce="happy:lunce",Lanhai="happy:lanhai";
    internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.IssueHappyPangTongPlan or SkillProgramEffectOp.ResolveHappyPangTongMiddle or SkillProgramEffectOp.ResolveHappyPangTongEnding or SkillProgramEffectOp.ResolveHappyPangTongLanhai;
    [ProgramCatalogValidation(42180)]internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Own(e.Op)))return;
        var valid=p.Id==Lunce?p.Triggers is[
            {Id:"round-plan",Window:SkillProgramTriggerWindow.RoundStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:true,Effects:[{Op:SkillProgramEffectOp.IssueHappyPangTongPlan}]},
            {Id:"turn-plan",Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,Subject:SkillProgramTriggerSubject.Owner,Optional:true,Effects:[{Op:SkillProgramEffectOp.IssueHappyPangTongPlan}]},
            {Id:"first-slash-plan",Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,OwnerRelation:SkillProgramCardActionOwnerRelation.Observer,Optional:false,Effects:[{Op:SkillProgramEffectOp.ResolveHappyPangTongMiddle}]},
            {Id:"ended-plan",Window:SkillProgramTriggerWindow.AfterTurnEnded,Subject:SkillProgramTriggerSubject.Owner,TurnOwnerScope:SkillProgramTurnOwnerScope.AnyLiving,Optional:false,Effects:[{Op:SkillProgramEffectOp.ResolveHappyPangTongEnding}]},
            {Id:"renew-plan",Window:SkillProgramTriggerWindow.HappyPlanSettled,Subject:SkillProgramTriggerSubject.Owner,Optional:false,Effects:[{Op:SkillProgramEffectOp.IssueHappyPangTongPlan}]}]:
            p.Id==Lanhai&&p.Triggers is[{Id:"settled-plan",Window:SkillProgramTriggerWindow.HappyPlanSettled,Subject:SkillProgramTriggerSubject.Owner,Optional:false,Priority:10,Effects:[{Op:SkillProgramEffectOp.ResolveHappyPangTongLanhai}]}];
        if(!valid||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.BooleanStates.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||
            p.Triggers.Any(t=>t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Effects.Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path+": Happy Pang Tong requires the published independent plans, actual first Slash/end boundaries and one native settled-plan reward.");
    }
}
