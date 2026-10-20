namespace CardGame.Core;
public sealed record QirangEntityLease(CardConversionSource Source,string GameplayHash,int OwnerSeat,int CardId,CardKind PrintedKind,long EquipmentUseId,long EquipmentActionId,long GainSequence);
public sealed record QirangEquipmentOrigin(long CardUseFrameId,CardActionContext OriginalAction,long MovementBefore);
public sealed record QirangEquipmentOriginCapturedEvent(long CardUseFrameId,long ActionId,int OwnerSeat,int CardId,long MovementBefore):IGameEvent;
public sealed record QirangUseQualification(QirangEntityLease Lease,long CardUseFrameId,CardActionContext OriginalAction,long MovementBefore);
public sealed record QirangGainPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long EquipmentUseId,long EquipmentActionId,int CardId,CardKind Kind,long Before,long After):IGameEvent;
public sealed record QirangEntityLeaseIssuedEvent(QirangEntityLease Lease):IGameEvent;
public sealed record QirangEntityLeaseEndedEvent(int OwnerSeat,int CardId,long GainSequence,string Reason):IGameEvent;
public sealed record QirangUseQualifiedEvent(long CardUseFrameId,long ActionId,int OwnerSeat,int CardId,long GainSequence,long MovementBefore):IGameEvent;
public sealed record QirangExtraTargetResolvedEvent(long FrameId,long CardUseFrameId,long ActionId,int? AddedTarget):IGameEvent;
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
