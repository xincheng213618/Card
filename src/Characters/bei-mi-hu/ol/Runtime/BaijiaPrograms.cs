using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BeiMiHuAwakeningMaximumPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int CumulativeActualCards,int MaximumBefore,int MaximumAfter):IGameEvent;
public sealed record BeiMiHuRecoveryIssuedEvent(long FrameId,int OwnerSeat,int Requested):IGameEvent;
public sealed record BeiMiHuAwakeningCompletedEvent(long FrameId,int OwnerSeat,IReadOnlyList<int> MarkedSeats,string LostSkillId,string GrantedSkillId):IGameEvent;
internal sealed class AwakenFromActualGujuCardsDescriptor():BeiMiHuDescriptor(SkillProgramEffectOp.AwakenFromActualGujuCards)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];}
