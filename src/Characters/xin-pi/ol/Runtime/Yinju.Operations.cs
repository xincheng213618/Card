using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record YinjuTurnRelation(CardConversionSource Source,string GameplayHash,int ActualTurn,int TargetSeat,long ActivationFrameId);
public sealed record YinjuRelationArmedEvent(YinjuTurnRelation Relation):IGameEvent;
public sealed record YinjuRecoveryPaidEvent(long FrameId,YinjuTurnRelation Relation,long WindowId,long DamageAttemptId,int SourceSeat,int TargetSeat,int Prevented,int RequestedRecovery,int HpBefore):IGameEvent;
public sealed record YinjuUseDrawStartedEvent(long FrameId,YinjuTurnRelation Relation,long UseId,long ActionId,long WindowId):IGameEvent;
public sealed record YinjuDrawPaidEvent(long FrameId,YinjuTurnRelation Relation,long UseId,long ActionId,int SubjectSeat,long Before,long After,int Actual):IGameEvent;
internal sealed class ArmYinjuActualTurnRelationDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmYinjuActualTurnRelation;}
internal sealed class PreventYinjuDamageAndRecoverDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PreventYinjuDamageAndRecover;}
internal sealed class DrawBothAfterYinjuActualTargetUseDescriptor:XinPiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawBothAfterYinjuActualTargetUse;}
