using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IHappyLuBuHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame,SkillProgramEffectOp op); }
internal abstract class HappyLuBuDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new HappyLuBuHandler(op);
    public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements=>op==SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse?new(Triggered:ProgramTrackedFact.WholeActualUseDeclaration|ProgramTrackedFact.NativeActionAppearance):default;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(op switch{SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic=>SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse=>SkillProgramTriggerWindow.CardUseCommitted,_=>SkillProgramTriggerWindow.AfterTurnEnded})];
}
internal sealed class ObtainHappyLuBuBasicDescriptor():HappyLuBuDescriptor(SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic);
internal sealed class EnhanceHappyLuBuUseDescriptor():HappyLuBuDescriptor(SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse);
internal sealed class ResetHappyLuBuShenfenDescriptor():HappyLuBuDescriptor(SkillProgramEffectOp.ResetHappyShenLuBuShenfen);
internal sealed class HappyLuBuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IHappyLuBuHost>().Execute(f,op);}
internal static class HappyLuBuComposition
{
    internal const string Wuqian="happy:wuqian",Shenfen="happy:shenfen";
    internal static bool Owns(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic or SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse or SkillProgramEffectOp.ResetHappyShenLuBuShenfen;
    [ProgramCatalogValidation(41200)] internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Any(e=>Owns(e.Op)))return;
        bool Single(SkillProgramTrigger t,SkillProgramEffectOp op)=>!t.Optional&&t.UsageScope is null&&t.MarkerCost is null&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.Effects is [{Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always} e]&&e.Op==op;
        var valid=p.Modifiers.Count==0&&p.ViewAs.Count==0&&p.CardPolicies.Count==0&&p.CardIdentities.Count==0&&p.BooleanStates.Count==0;
        if(p.Id==Wuqian)valid&=p.Activations.Count==0&&p.Triggers is [var basic,var use]&&basic.Id=="actual-turn-basic"&&basic.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&basic.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&Single(basic,SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic)&&
            use.Id=="first-kind-use"&&use.Window==SkillProgramTriggerWindow.CardUseCommitted&&use.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&use.CardKinds.Count==4&&use.CardKinds.All(k=>k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel)&&Single(use,SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse);
        else valid&=p.Id==Shenfen&&p.Triggers is [var reset]&&reset.Id=="full-hp-reset"&&reset.Window==SkillProgramTriggerWindow.AfterTurnEnded&&reset.TurnOwnerScope==SkillProgramTurnOwnerScope.AnyLiving&&Single(reset,SkillProgramEffectOp.ResetHappyShenLuBuShenfen)&&
            p.Activations is [{Id:"limited-destruction",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerGame:1,UsesPerTurn:null,UsesPerPhase:null,MarkerCost:null,Effects:[
                {Op:SkillProgramEffectOp.LoseHp,Target:SkillProgramEffectTarget.Owner,Amount:3},
                {Op:SkillProgramEffectOp.DamageParticipants,Target:SkillProgramEffectTarget.Owner,TargetKind:SkillProgramTargetKind.OtherLiving,Amount:1},
                {Op:SkillProgramEffectOp.DiscardParticipantCards,Target:SkillProgramEffectTarget.Owner,TargetKind:SkillProgramTargetKind.OtherLiving,Amount:64,Zones:[CardZoneKind.Equipment]},
                {Op:SkillProgramEffectOp.DiscardParticipantCards,Target:SkillProgramEffectTarget.Owner,TargetKind:SkillProgramTargetKind.OtherLiving,Amount:4,Zones:[CardZoneKind.Hand]}]}];
        if(!valid)throw new InvalidOperationException(path+": Happy Lu Bu requires the complete formal actual-turn Wuqian and paid limited Shenfen chain.");
    }
}
public enum HappyLuBuStage { BasicChildren,AdditionalTarget,Finished }
public sealed record HappyLuBuReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn)
{
    public HappyLuBuStage Stage {get;init;}
    public long UseId {get;init;}
    public long ActionId {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    private readonly IReadOnlyList<CardMovementRecord> _paid=[];
    public IReadOnlyList<CardMovementRecord> Paid {get=>_paid;init=>_paid=Array.AsReadOnly(value.ToArray());}
}
public sealed record HappyLuBuUseReceipt(long ProgramId,long WindowId,long UseId,long ActionId,int ActualTurn,CardKind Kind,CardConversionSource Source,string GameplayHash)
{
    public bool Applied {get;init;}
    public int? AddedTarget {get;init;}
    private readonly IReadOnlyList<int> _targets=[];
    public IReadOnlyList<int> OriginalTargets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record HappyLuBuActualStartReceipt(int ActualTurn,CardConversionSource Source,string GameplayHash);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyLuBuReceipt? HappyLuBu {get;init;} }
public sealed partial record CardUseFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyLuBuUseReceipt? HappyLuBuWuqian {get;init;} }
public sealed partial record ProgramLifecycleTriggerWindowFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyLuBuActualStartReceipt? HappyLuBuActualStart {get;init;} }
public sealed record HappyLuBuActualStartIssuedEvent(long WindowId,int ActualTurn,CardConversionSource Source,string GameplayHash):IGameEvent;
public sealed record HappyLuBuActualStartReturnedEvent(long WindowId,int ActualTurn,int OwnerSeat):IGameEvent;
public sealed record HappyLuBuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn,long UseId,long ActionId):IGameEvent;
public sealed record HappyLuBuBasicPaidEvent(long FrameId,long Before,long After,int Count):IGameEvent;
public sealed record HappyLuBuUseAppliedEvent(long FrameId,long UseId,long ActionId,int? AddedTarget):IGameEvent;
public sealed record HappyLuBuShenfenResetEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int EndedOwner,string UsageGroup,int Before):IGameEvent;
