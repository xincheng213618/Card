using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record XiliDamageIncreasedEvent(long FrameId,long DamageAttemptResolutionId,long BeforeDamageWindowId,int PayerSeat,int SourceSeat,int TargetSeat,int Before,int After):IGameEvent;
internal sealed class PayXiliActualOtherOwnerDamageDescriptor:HuaManDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PayXiliActualOtherOwnerDamage;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
