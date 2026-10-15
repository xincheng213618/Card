namespace CardGame.Core;
public sealed record QirangEntityLease(CardConversionSource Source,string GameplayHash,int OwnerSeat,int CardId,CardKind PrintedKind,long EquipmentUseId,long EquipmentActionId,long GainSequence);
public sealed record YuhuaDelayDebt(CardConversionSource Source,string GameplayHash,int OwnerSeat,int CreatedActualTurn,long EquipmentUseId,long EquipmentActionId,int CardId,long GainSequence);
public sealed record YuhuaActiveQuota(int OwnerSeat,int ActualTurnNumber,int Count);
public sealed record QirangEquipmentOrigin(long CardUseFrameId,CardActionContext OriginalAction,long MovementBefore);
public sealed record QirangEquipmentOriginCapturedEvent(long CardUseFrameId,long ActionId,int OwnerSeat,int CardId,long MovementBefore):IGameEvent;
public sealed record QirangUseQualification(QirangEntityLease Lease,long CardUseFrameId,CardActionContext OriginalAction,long MovementBefore);
public enum ZhugeGuoStage { GainChildren,ExtraTargetChoice,YuhuaInspect }
public sealed record ZhugeGuoReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,long WindowId,ZhugeGuoStage Stage,
    long? UseId=null,long? ActionId=null,CardActionContext? OriginalAction=null,int? GainedCardId=null,CardKind? GainedKind=null,long Before=0,long After=0,
    QirangUseQualification? Qualification=null,int RequestedPeek=0,IReadOnlyList<int>? Viewed=null,bool PlacementIssued=false,IReadOnlyList<int>? PlacedTop=null,IReadOnlyList<int>? PlacedBottom=null)
{
    private readonly IReadOnlyList<int> _viewed=Array.AsReadOnly((Viewed??[]).ToArray());
    public IReadOnlyList<int> Viewed {get=>_viewed;init=>_viewed=Array.AsReadOnly((value??[]).ToArray());}
    private readonly IReadOnlyList<int> _placedTop=Array.AsReadOnly((PlacedTop??[]).ToArray());
    public IReadOnlyList<int> PlacedTop {get=>_placedTop;init=>_placedTop=Array.AsReadOnly((value??[]).ToArray());}
    private readonly IReadOnlyList<int> _placedBottom=Array.AsReadOnly((PlacedBottom??[]).ToArray());
    public IReadOnlyList<int> PlacedBottom {get=>_placedBottom;init=>_placedBottom=Array.AsReadOnly((value??[]).ToArray());}
}
public sealed record QirangGainPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long EquipmentUseId,long EquipmentActionId,int CardId,CardKind Kind,long Before,long After):IGameEvent;
public sealed record QirangEntityLeaseIssuedEvent(QirangEntityLease Lease):IGameEvent;
public sealed record QirangEntityLeaseEndedEvent(int OwnerSeat,int CardId,long GainSequence,string Reason):IGameEvent;
public sealed record QirangUseQualifiedEvent(long CardUseFrameId,long ActionId,int OwnerSeat,int CardId,long GainSequence,long MovementBefore):IGameEvent;
public sealed record QirangExtraTargetResolvedEvent(long FrameId,long CardUseFrameId,long ActionId,int? AddedTarget):IGameEvent;
public sealed record YuhuaDelayedDebtIssuedEvent(YuhuaDelayDebt Debt):IGameEvent;
public sealed record YuhuaActualTurnQuotaStartedEvent(int OwnerSeat,int ActualTurnNumber,int Count):IGameEvent;
public sealed record YuhuaPrivatePeekStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,int Requested,int ActualCount):IGameEvent;
public sealed record YuhuaPrivatePeekPlacedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,int TopCount,int BottomCount):IGameEvent;
internal interface IZhugeGuoProgramHost {SkillProgramStepOutcome BeginZhugeGuo(ProgramSkillFrame f,SkillProgramEffect e);}
internal abstract class ZhugeGuoDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class ObtainDeckTrickForActualEquipmentUseDescriptor:ZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse;
    public override ISkillProgramEffectHandler Handler {get;}=new ZhugeGuoHandler(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class AddTargetForQirangPhysicalTrickDescriptor:ZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AddTargetForQirangPhysicalTrick;
    public override ISkillProgramEffectHandler Handler {get;}=new ZhugeGuoHandler(SkillProgramEffectOp.AddTargetForQirangPhysicalTrick);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class InspectYuhuaTopDescriptor:ZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InspectYuhuaTop;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.TurnEnding])];
    public override ISkillProgramEffectHandler Handler {get;}=new ZhugeGuoHandler(SkillProgramEffectOp.InspectYuhuaTop);
}
internal sealed class ZhugeGuoHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IZhugeGuoProgramHost)h).BeginZhugeGuo(f,e);
}
internal static class ZhugeGuoComposition
{
    internal static void Activation(string path,SkillProgramActivation a)
    {
        if(a.Effects.Any(e=>e.Op is SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse or SkillProgramEffectOp.AddTargetForQirangPhysicalTrick or SkillProgramEffectOp.InspectYuhuaTop))throw new InvalidOperationException(path+": ZhugeGuo operations require native trigger windows, not activations.");
    }
    internal static void Trigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(e=>e.Op is SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse or SkillProgramEffectOp.AddTargetForQirangPhysicalTrick or SkillProgramEffectOp.InspectYuhuaTop))return;
        if(t.Effects is not[var e] || e.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": ZhugeGuo requires one exact unconditional native effect.");
        if(e.Op==SkillProgramEffectOp.InspectYuhuaTop && (t.Window is not(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding)||t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own))throw new InvalidOperationException(path+": Yuhua requires actual own prepare/ending mandatory entry.");
        if(e.Op is SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse or SkillProgramEffectOp.AddTargetForQirangPhysicalTrick && (t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor))throw new InvalidOperationException(path+": Qirang requires actual actor's finalized native equipment/trick Use.");
    }
}

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ZhugeGuoReceipt? ZhugeGuo {get;init;}
}
public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public QirangUseQualification? QirangQualification {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public QirangEquipmentOrigin? QirangEquipmentOrigin {get;init;}
}
public sealed partial class CharacterState
{
    private IReadOnlyList<QirangEntityLease>? _qirangEntityLeases;
    internal IReadOnlyList<QirangEntityLease>? QirangEntityLeases {get=>_qirangEntityLeases;set=>_qirangEntityLeases=value is null?null:Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<YuhuaDelayDebt>? _yuhuaDelayDebts;
    internal IReadOnlyList<YuhuaDelayDebt>? YuhuaDelayDebts {get=>_yuhuaDelayDebts;set=>_yuhuaDelayDebts=value is null?null:Array.AsReadOnly(value.ToArray());}
    internal YuhuaActiveQuota? YuhuaActiveQuota {get;set;}
}
