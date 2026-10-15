using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record SimaHuiChainOriginProof(long WindowId,long AttackFrameId,int ActualTurn,int SourceSeat,int TargetSeat,DamageNature Nature,bool SourceLess,int LivingChainedCount);
public sealed record SimaHuiReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public long ParentFrameId {get;init;}
    public int ActualTurn {get;init;}
    public int InstructionIndex {get;init;}
    public string Stage {get;init;}="";
    public string Kind {get;init;}="";
    public int? PreviousHolder {get;init;}
    public int? ChosenHolder {get;init;}
    public int Cursor {get;init;}
    public long MovementBefore {get;init;}
    public long MovementAfter {get;init;}
    public SimaHuiChainOriginProof? ChainOrigin {get;init;}
    private IReadOnlyList<int> _candidates=Array.Empty<int>(),_cards=Array.Empty<int>(),_selected=Array.Empty<int>();
    public IReadOnlyList<int> Candidates {get=>_candidates;init=>_candidates=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> OriginalCards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> SelectedSeats {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SimaHuiReceipt? SimaHui {get;init;}
}
public sealed record SimaHuiChoiceStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,int ActualTurn,string Stage,IReadOnlyList<int> Candidates):IGameEvent;
public sealed record SimaHuiSealMovedEvent(long FrameId,CardConversionSource Issuer,string GameplayHash,int ActualTurn,PlayerMarkerKind Marker,int? PreviousHolder,int? Holder,string GrantSourceId):IGameEvent;
public sealed record SimaHuiTopViewedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SimaHuiChainOriginProof Origin,IReadOnlyList<int> CardIds,long Before,long After):IGameEvent;
public sealed record SimaHuiCardDistributedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int Cursor,int CardId,int Recipient,long Before,long After):IGameEvent;
public sealed record SimaHuiUnassignedReturnedEvent(long FrameId,CardConversionSource Source,string GameplayHash,IReadOnlyList<int> CardIds,long Before,long After):IGameEvent;
public sealed record SimaHuiYeyanSealsPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,IReadOnlyList<string> GrantSourceIds,IReadOnlyList<int> MaterialCardIds):IGameEvent;

internal interface ISimaHuiProgramHost
{
    SkillProgramStepOutcome BeginSimaHuiOperation(ProgramSkillFrame frame,SkillProgramEffectOp op);
}
internal sealed class SimaHuiOperationHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host)=>host.GetCapability<ISimaHuiProgramHost>().BeginSimaHuiOperation(frame,op);
}
internal abstract class SimaHuiDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler{get;}=new SimaHuiOperationHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)=>ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[];
}
internal sealed class AssignOrderedDragonPhoenixOnFirstActualTurnDescriptor():SimaHuiDescriptor(SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnBeforeStart)];}
internal sealed class TransferOwnedDragonOrPhoenixSealDescriptor():SimaHuiDescriptor(SkillProgramEffectOp.TransferOwnedDragonOrPhoenixSeal){}
internal sealed class TransferDeadDragonPhoenixSealDescriptor():SimaHuiDescriptor(SkillProgramEffectOp.TransferDeadDragonPhoenixSeal)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];}
internal sealed class DistributeTopCardsAfterElementalChainOriginDescriptor():SimaHuiDescriptor(SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
internal sealed class PreventDamageWithoutSealsAndArmorDescriptor():SimaHuiDescriptor(SkillProgramEffectOp.PreventDamageWithoutSealsAndArmor)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
internal sealed class ConsumeDragonPhoenixForNativeYeyanDescriptor():SimaHuiDescriptor(SkillProgramEffectOp.ConsumeDragonPhoenixForNativeYeyan){}

internal static class SimaHuiComposition
{
    internal static void ValidateTrigger(string path,SkillProgramTrigger trigger)
    {
        var ops=trigger.Effects.Where(e=>(int)e.Op is >=23500 and <=23505).ToArray();if(ops.Length==0)return;
        if(ops.Length!=1 || trigger.Effects.Count!=1 || ops[0].Target!=SkillProgramEffectTarget.Owner || ops[0].Condition.Kind!=SkillProgramConditionKind.Always || trigger.Condition.Kind!=SkillProgramTriggerConditionKind.Always)
            throw new InvalidOperationException(path+": Sima Hui requires its sole native-gated owner operation.");
        var expected=ops[0].Op switch
        {
            SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn=>SkillProgramTriggerWindow.TurnBeforeStart,
            SkillProgramEffectOp.TransferDeadDragonPhoenixSeal=>SkillProgramTriggerWindow.CharacterDied,
            SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin=>SkillProgramTriggerWindow.AfterDamageApplied,
            SkillProgramEffectOp.PreventDamageWithoutSealsAndArmor=>SkillProgramTriggerWindow.BeforeDamageApplied,
            _=>throw new InvalidOperationException(path+": transfer/Yeyan costs require an actual activation.")
        };
        if(trigger.Window!=expected || trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null)
            throw new InvalidOperationException(path+": Sima Hui requires its exact native lifecycle/damage window.");
        if(ops[0].Op is SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn or SkillProgramEffectOp.PreventDamageWithoutSealsAndArmor)
        {if(trigger.Optional || trigger.Subject!=(ops[0].Op==SkillProgramEffectOp.AssignOrderedDragonPhoenixOnFirstActualTurn?SkillProgramTriggerSubject.Owner:SkillProgramTriggerSubject.DamageTarget))throw new InvalidOperationException(path+": first assignment is compulsory owner; Yinshi is compulsory actual damage-target.");}
        else if(!trigger.Optional || trigger.Subject!=SkillProgramTriggerSubject.Any)throw new InvalidOperationException(path+": death transfer and chain-origin distribution are optional participant bindings.");
    }
    internal static void ValidateActivation(string path,SkillProgramActivation activation)
    {
        var ops=activation.Effects.Where(e=>(int)e.Op is >=23500 and <=23505).ToArray();if(ops.Length==0)return;
        if(ops.Length!=1 || activation.Effects[0]!=ops[0] || ops[0].Target!=SkillProgramEffectTarget.Owner || ops[0].Condition.Kind!=SkillProgramConditionKind.Always ||
            ops[0].Op is not(SkillProgramEffectOp.TransferOwnedDragonOrPhoenixSeal or SkillProgramEffectOp.ConsumeDragonPhoenixForNativeYeyan))throw new InvalidOperationException(path+": Sima Hui activation requires one leading exact seal operation.");
        if(ops[0].Op==SkillProgramEffectOp.TransferOwnedDragonOrPhoenixSeal && (activation.Effects.Count!=1 || activation.MinCards!=0 || activation.MaxCards!=0 || activation.MinTargets!=0 || activation.MaxTargets!=0))
            throw new InvalidOperationException(path+": seal transfer uses its owning native choice, without preselected entities or targets.");
        if(ops[0].Op==SkillProgramEffectOp.ConsumeDragonPhoenixForNativeYeyan)
        {
            var small=activation.Id=="small";var two=activation.Id=="two";var three=activation.Id=="three";var split=activation.Id=="two-and-one";
            var damage=activation.Effects.Last();
            if(!(small||two||three||split) || activation.UsesPerGame!=1 || activation.UsageGroup!="limited" || activation.TargetKind!=SkillProgramTargetKind.AnyLiving ||
                damage.Op!=SkillProgramEffectOp.DamageParticipants || damage.DamageNature!=DamageNature.Fire || damage.Amount!=(small?1:three?3:2) || damage.MinimumValue!=(split?1:small?1:three?3:2) ||
                activation.MinTargets!=(split?2:1) || activation.MaxTargets!=(small?3:split?2:1) || activation.MinCards!=(small?0:4) || activation.MaxCards!=(small?0:4) ||
                activation.Effects.Count!=(small?2:4) || !small && (!activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) || !activation.SelectedCardsDistinctSuits || !activation.ContinueAfterOwnerDeath ||
                activation.Effects[1].Op!=SkillProgramEffectOp.DiscardSelected || activation.Effects[1].Amount!=4 || activation.Effects[2].Op!=SkillProgramEffectOp.LoseHpUnclamped || activation.Effects[2].Amount!=3))
                throw new InvalidOperationException(path+": dedicated SP Yeyan must retain its native limited allocation and exact four-suit/LoseHp costs.");
        }
    }
}
