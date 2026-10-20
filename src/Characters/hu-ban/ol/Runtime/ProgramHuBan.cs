using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record HuiyunUseInvoice(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int RoundNumber,int ActualTurn,int TurnOwner,int ActorSeat,int ProviderSeat,CardActionCost Material,long BeforePayment);
public sealed record HuiyunUseIssuedEvent(HuiyunUseInvoice Invoice):IGameEvent;
public sealed record HuiyunPaymentRecordedEvent(long UseId,long ActionId,int CardId,CardLocation From,CardLocation To,CardMoveReason Reason,long Sequence,bool Followup):IGameEvent;
public sealed record HuiyunOriginalPaymentReceipt(long UseId,long ActionId,long PrimaryBatchId,long Before,long After);
public sealed record HuiyunOriginalPaymentIssuedEvent(HuiyunOriginalPaymentReceipt Receipt):IGameEvent;
public sealed record HuiyunEffectPaymentReceipt(long UseId,long ActionId,int EffectRound,int TargetIndex,int SourceSeat,int TargetSeat,int RevealedCardId,int DiscardCardId,long HandBatchId,long DiscardBatchId,long HandSequence,long DiscardSequence);
public sealed record HuiyunEffectPaymentIssuedEvent(HuiyunEffectPaymentReceipt Receipt):IGameEvent;
public sealed record HuiyunEffectEnteredEvent(long UseId,int EffectRound,int TargetIndex,int TargetSeat):IGameEvent;
public sealed record HuiyunEffectRevealedEvent(long UseId,int EffectRound,int TargetIndex,int TargetSeat,int CardId):IGameEvent;
public sealed record HuiyunOpportunity(int EffectRound,int TargetIndex,int TargetSeat,int? ShownCardId);
public sealed record HuiyunStartedEvent(long FrameId,HuiyunUseInvoice Origin,long ParentWindowId,int OpportunityCount):IGameEvent;
public sealed record HuiyunOpportunityIssuedEvent(long FrameId,int Index,HuiyunOpportunity Opportunity):IGameEvent;
public sealed record HuiyunOptionSpentEvent(long FrameId,int Index,CardConversionSource Source,int RoundNumber,int Option):IGameEvent;
public sealed record HuiyunTargetDeclinedEvent(long FrameId,int Index,int TargetSeat):IGameEvent;
public sealed record HuiyunRecastCardEvent(long FrameId,int Index,int CardId,CardKind Kind,int HolderSeat,long Sequence):IGameEvent;
public sealed record HuiyunMovementPaidEvent(long FrameId,int Index,HuiyunStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record HuiyunOpportunityCompletedEvent(long FrameId,int Index):IGameEvent;
public sealed record HuiyunCompletedEvent(long FrameId):IGameEvent;
public enum HuiyunStage { OptionChoice,HandUseChoice,Issuing,NativeUse,NativeChildren,RecastCostChildren,RecastDrawChildren,DrawChildren }
public static class HuiyunFreeze
{
    public static LegalAction Action(LegalAction a)=>a with{TargetSeats=Array.AsReadOnly(a.TargetSeats.ToArray()),SelectableCardIds=Array.AsReadOnly(a.SelectableCardIds.ToArray()),SelectableTargetSeats=Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),AdditionalConversionSources=Array.AsReadOnly((a.AdditionalConversionSources??[]).ToArray()),SelectableCardTargets=a.SelectableCardTargets is null?null:Array.AsReadOnly(a.SelectableCardTargets.ToArray()),SelectableTargetSequences=a.SelectableTargetSequences is null?null:Array.AsReadOnly(a.SelectableTargetSequences.Select(x=>(IReadOnlyList<int>)Array.AsReadOnly(x.ToArray())).ToArray())};
}
public sealed record HuiyunNativeInvoice(long ProgramFrameId,int OpportunityIndex,CardConversionSource Source,string GameplayHash,int ActorSeat,int CardId,CardKind Kind,LegalAction Plan,long BeforePayment,long UseId=0,long ActionId=0)
{
    private LegalAction _plan=HuiyunFreeze.Action(Plan);
    public LegalAction Plan {get=>_plan;init=>_plan=HuiyunFreeze.Action(value);}
}
public sealed record HuiyunNativeIssuedEvent(HuiyunNativeInvoice Invoice):IGameEvent;
public sealed record HuiyunNativeReturnedEvent(HuiyunNativeInvoice Invoice,long After):IGameEvent;
public sealed record HuiyunReceipt(HuiyunUseInvoice Origin,long ParentWindowId,HuiyunStage Stage,IReadOnlyList<HuiyunOpportunity> Opportunities,int Index=0,int Option=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] HuiyunNativeInvoice? Native=null,bool NativeCompleted=false,long Before=0,long After=0,int ActualCount=0)
{
    private IReadOnlyList<HuiyunOpportunity> _opportunities=Array.AsReadOnly(Opportunities.ToArray());
    public IReadOnlyList<HuiyunOpportunity> Opportunities {get=>_opportunities;init=>_opportunities=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuiyunReceipt? Huiyun{get;init;}}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuiyunUseInvoice? HuiyunOrigin{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuiyunNativeInvoice? HuiyunFollowup{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuiyunOriginalPaymentReceipt? HuiyunOriginalPayment{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuiyunEffectPaymentReceipt? HuiyunEffectPayment{get;init;}
}
internal interface IHuBanHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame);}
internal sealed class ResolveHuiyunCompletedFireAttackDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack;
    public override ISkillProgramEffectHandler Handler{get;}=new HuiyunHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
}
internal sealed class HuiyunHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IHuBanHost>().Resolve(f);
}
internal static class HuBanComposition
{
    [ProgramCatalogValidation(560)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack)))return;
        if(p.Id!="ol:huiyun"||p.Triggers is not[var t]||t.Id!="completed-converted-fire-attack"||t.Window!=SkillProgramTriggerWindow.CardUseCompleted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Optional||t.Priority!=0||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.MarkerCost is not null||
            t.Effects is not[{Op:SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||p.ViewAs is not[var v]||v.Id!="huiyun-fire-attack"||v.OutputKind!=CardKind.FireAttack||!v.SingleCardTrickUse||!v.ForPlay||v.ForResponse||v.InputCount!=1||v.VariableInputCount||!v.AllowSameKind||!v.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment])||v.InputKinds.Count!=0||v.InputSuits.Count!=0||v.UsesPerPhase is not null||v.UsesPerActualTurn is not null||
            p.Activations.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.DamageModifiers.Count!=0)
            throw new InvalidOperationException(path+": Huiyun requires its unrestricted one-HE FireAttack conversion and exact whole-use continuation; round options are paid separately.");
    }
}
