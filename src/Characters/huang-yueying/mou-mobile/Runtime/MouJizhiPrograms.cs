using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record MouHuangYueyingJizhiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, int ActualTurn, int TurnOwnerSeat) : IGameEvent;
public sealed record MouHuangYueyingJizhiExemptEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwnerSeat, int CardId, long MovementSequence) : IGameEvent;
public sealed record MouHuangYueyingJizhiDrawReturnedEvent(long FrameId, long? AttemptFrameId, int ActualCount, int ReplacementCount) : IGameEvent;
internal sealed class ResolveMouHuangYueyingJizhiDescriptor : MouHuangYueyingMobileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingJizhi;
  public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase); }
