using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoTengDamageMaterial(int CardId,CardKind Kind,long EntrySequence);
public sealed record CaoTengDamageMaterialFrozenEvent(long FrameId,int Index,CaoTengDamageMaterial Material):IGameEvent;
public sealed record CaoTengJianxiongDrawIssuedEvent(long FrameId,long Before):IGameEvent;
public sealed record CaoTengJianxiongClaimIssuedEvent(long FrameId,long Before):IGameEvent;
public sealed record CaoTengJianxiongClaimedEvent(long FrameId,int CardId,long Sequence):IGameEvent;
internal sealed class ResolveCaoTengJianxiongDrawAndClaimDescriptor:CaoTengDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveCaoTengJianxiongDrawAndClaim;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
