using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record GuolunRevealedEvent(long FrameId,CardConversionSource Source,int TargetSeat,int CardId):IGameEvent;
internal sealed class ResolveGuolunRevealOptionalAtomicExchangeDescriptor():SpPangTongDescriptor(SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];}
