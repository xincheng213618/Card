using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record PengbiCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, string SkillId) : IGameEvent;
public sealed record PengbiSkillGrantedEvent(long FrameId, int RecipientSeat, string SkillId, string GrantId, string SkillInstanceId, string SourceId) : IGameEvent;
public sealed record PengbiOtherChosenEvent(long FrameId, int? RecipientSeat) : IGameEvent;
internal sealed class PengbiFirstActualTurnDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolvePengbiFirstActualTurn)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
