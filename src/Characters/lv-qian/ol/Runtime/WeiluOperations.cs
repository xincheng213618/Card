using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WeiluPhaseKey(int ActualTurn,int OwnerSeat,int PhaseInstance);
public sealed record WeiluFutureSource(CardConversionSource Source,string GameplayHash,int DamageSourceSeat,long DamageFrameId,int ArmedTurn,int? DueOwnTurn=null,WeiluPhaseKey? FirstPhase=null);
public sealed record WeiluPaidDebt(CardConversionSource Source,string GameplayHash,long ProducerFrameId,WeiluPhaseKey Phase,int TargetSeat,int ActualHpLost,bool Consumed=false);
public sealed record WeiluNativeHpProof(int TargetSeat,int Amount,int HpBefore,HpChangeKind Kind);
public sealed record WeiluFutureArmedEvent(CardConversionSource Source,string GameplayHash,long DamageFrameId,int DamageSourceSeat,int ActualTurn):IGameEvent;
public sealed record WeiluHpPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,WeiluPhaseKey Phase,int TargetSeat,int ActualHpLost):IGameEvent;
public sealed record WeiluDebtConsumedEvent(long FrameId,long ProducerFrameId,WeiluPhaseKey Phase,int TargetSeat,int Amount,bool TargetAlive):IGameEvent;
internal sealed class RecordWeiluActualDamageSourceDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RecordWeiluActualDamageSource;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
internal sealed class PayWeiluNextActualPlayHpLossDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PayWeiluNextActualPlayHpLoss;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];}
internal sealed class RestoreWeiluExactPaidPlayHpDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RestoreWeiluExactPaidPlayHp;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
