using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouXiahouDunMobileGanglieSpentEvent(long FrameId, int OwnerSeat, int TargetSeat, int Index) : IGameEvent;
internal sealed class ResolveMouXiahouDunGanglieDescriptor : MouXiahouDunMobileDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouDunGanglie; }
