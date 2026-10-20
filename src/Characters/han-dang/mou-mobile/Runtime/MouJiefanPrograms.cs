using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouHanDangJiefanDisabledEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record MouHanDangJiefanRestoredEvent(int OwnerSeat, int VictimSeat) : IGameEvent;
internal sealed class ResolveMouHanDangJiefanDescriptor : MouHanDangDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHanDangJiefan;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
