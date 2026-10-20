using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhangYanSujiReturn(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseOwner,int OriginalTarget,int CardId,CardKind PrintedKind,CardLocation From);
public sealed record ZhangYanSujiIssuedEvent(ZhangYanSujiReturn Return):IGameEvent;
public sealed record ZhangYanSujiPaidEvent(long FrameId,long UseId,int CardId,CardLocation From,CardLocation To,long Before,long After,long BatchId):IGameEvent;
public sealed record ZhangYanSujiReturnedEvent(ZhangYanSujiReturn Return):IGameEvent;
internal sealed class UseBlackCardAtWoundedPlayStartDescriptor():ZhangYanDescriptor(SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];}
internal sealed class ObtainSujiDamagedPhaseOwnerCardDescriptor():ZhangYanDescriptor(SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
