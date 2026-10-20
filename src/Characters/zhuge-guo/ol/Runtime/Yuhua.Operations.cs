namespace CardGame.Core;
public sealed record YuhuaDelayDebt(CardConversionSource Source,string GameplayHash,int OwnerSeat,int CreatedActualTurn,long EquipmentUseId,long EquipmentActionId,int CardId,long GainSequence);
public sealed record YuhuaActiveQuota(int OwnerSeat,int ActualTurnNumber,int Count);
public sealed record YuhuaDelayedDebtIssuedEvent(YuhuaDelayDebt Debt):IGameEvent;
public sealed record YuhuaActualTurnQuotaStartedEvent(int OwnerSeat,int ActualTurnNumber,int Count):IGameEvent;
public sealed record YuhuaPrivatePeekStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,int Requested,int ActualCount):IGameEvent;
public sealed record YuhuaPrivatePeekPlacedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,int TopCount,int BottomCount):IGameEvent;
internal sealed class InspectYuhuaTopDescriptor:ZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InspectYuhuaTop;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.TurnEnding])];
    public override ISkillProgramEffectHandler Handler {get;}=new ZhugeGuoHandler(SkillProgramEffectOp.InspectYuhuaTop);
}
