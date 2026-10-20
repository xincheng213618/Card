using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class ResolveDianjunActualTurnEndDescriptor() : ZhangYiDescriptor(SkillProgramEffectOp.ResolveDianjunActualTurnEnd)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
public sealed record ZhangYiDianjunCleanupStartedEvent(long FrameId, int OwnerSeat, int ActualTurnNumber, int SequenceBefore) : IGameEvent;
public sealed record ZhangYiDianjunCleanupIssuedEvent(long FrameId, int OwnerSeat, int ActualTurnNumber, int SequenceBefore, int SequenceAfter) : IGameEvent;
