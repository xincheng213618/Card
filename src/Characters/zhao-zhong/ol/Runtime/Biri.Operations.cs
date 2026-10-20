using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record BiriPotentialGainEvent(long BatchId, long Sequence, int CardId, int RecipientSeat, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record BiriPotentialHolderEvent(long BatchId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record BiriDrawIssuedEvent(long FrameId, long BatchId, long GainSequence, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record BiriDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
internal sealed class BiriFirstNonDrawGainDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolveBiriFirstNonDrawGain){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
