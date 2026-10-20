using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BeiMiHuGujuDrawPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long DamageWindowId,long DamageParentId,int DamageTarget,int Turn,long Before,long After,int Actual):IGameEvent;
internal sealed class DrawAfterActualKuiDamageDescriptor():BeiMiHuDescriptor(SkillProgramEffectOp.DrawAfterActualKuiDamage)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
