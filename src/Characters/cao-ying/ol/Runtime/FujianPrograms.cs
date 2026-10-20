using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoYingInspectionIssuedEvent(long ProgramFrameId, int ViewerSeat, int TargetSeat, int CardCount) : IGameEvent;
internal sealed class RandomEligibleOtherHandInspectionDescriptor : CaoYingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RandomEligibleOtherHandInspection; }
