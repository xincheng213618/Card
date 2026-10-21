using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouDcDelayedCause(long OriginalUseId,long OriginalActionId,CardConversionSource Source,string GameplayHash,long AttachmentId,long JudgmentId,long DamageId,int Target);
public sealed record MouDcDelayedReward(MouDcDelayedCause Cause,long DyingId,int Turn,bool Settled=false);
public sealed record MouDcSettlement(long JudgmentId,CardConversionSource OriginalSource,string OriginalHash,bool Reward,long ProtectionId,long DyingId,long OriginalUseId);
public sealed partial record JudgmentFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouDcDelayedReward? MouDongChengReward {get;init;}
 private IReadOnlyList<MouDcClaim>?_mouDcClaims;
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<MouDcClaim>? MouDongChengClaims {get=>_mouDcClaims;init=>_mouDcClaims=value is null?null:Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public MouDcSettlement? MouDongChengSettlement {get;init;}}
public sealed record MouDcLightningRewardEvent(MouDcDelayedCause Cause,long DyingId,int Turn):IGameEvent;
public sealed record MouDcSettlementIssuedEvent(long FrameId,MouDcSettlement Return):IGameEvent;
public sealed record MouDcDelayedClaimSettledEvent(long FrameId,long JudgmentId,long ProtectionId,int CardId,int Actual,long Before,long After):IGameEvent;
internal interface IMouDcLightningFacts:IProgramSelectionQuery
{
 MouDcDelayedCause? DyingCause(PlayerDyingEvent fact);MouDcSettlement? NextSettlement(long judgment);bool SettlementParent(ProgramSkillFrame f);
}
internal interface IMouDcLightningNative:IProgramRuntimeOperations
{
 ProgramSkillFrame BindSettlement(ProgramSkillFrame child,long judgment);void MarkReward(MouDcDelayedCause cause,long dying);void ResumeCleanup(long judgment);void MarkSettlement(MouDcSettlement settlement);
}
internal sealed class ResolveMouDongChengDelayedSettlementDescriptor:MouDongChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouDongChengDelayedSettlement;}
