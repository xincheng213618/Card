using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum FengweiStage { Choice,DrawChildren }
public sealed record FengweiReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int Round,FengweiStage Stage,int Requested=0,int Actual=0,long Before=0,long After=0);
public sealed record FengweiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentId,int Round):IGameEvent;
public sealed record FengweiDrawCompletedEvent(long FrameId,CardConversionSource Source,int Round,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record FengweiAcquiredEvent(long FrameId,CardConversionSource Source,int Round,int CardId,long Sequence):IGameEvent;
public sealed record FengweiDrawSkippedEvent(long FrameId,CardConversionSource Source,int Round,long PhaseToken,int CardId):IGameEvent;
public sealed record FengweiDamageReceipt(long AttackId,int TargetSeat,int Cursor,int InheritedAmount,int Bonus,long InvoiceId,int? TransferredTarget=null,long? TransferWindowId=null);
public sealed record FengweiDamageFrozenEvent(long AttackId,int TargetSeat,int Cursor,int InheritedAmount,int Bonus,long InvoiceId):IGameEvent;
public sealed record FengweiDamageRebasedEvent(long AttackId,int TargetSeat,int Cursor,int InheritedAmount):IGameEvent;
public sealed record FengweiDamageTransferredEvent(long AttackId,int OriginalSeat,int TargetSeat,int Cursor,long WindowId):IGameEvent;
internal sealed class ResolveFengweiRoundStartDrawDescriptor:LiuZhangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFengweiRoundStartDraw;}
internal sealed class ResolveFengweiActualDrawSkipDescriptor:LiuZhangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFengweiActualDrawSkip;}
