using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GameStartingNativeMovementInvoices
{
    internal static bool SameGameStartingNativeMovementOrigin(GameStartingNativeMovementOrigin? left, GameStartingNativeMovementOrigin? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null && left.WindowFrameId == right.WindowFrameId &&
        left.ProgramFrameId == right.ProgramFrameId && left.InstructionIndex == right.InstructionIndex && left.GameplayHash == right.GameplayHash &&
        left.Source == right.Source && left.Operation == right.Operation && left.BatchId == right.BatchId && left.RecipientSeat == right.RecipientSeat &&
        left.SequenceBefore == right.SequenceBefore && left.SequenceAfter == right.SequenceAfter && left.Kind == right.Kind &&
        left.ActualTurnOwnerSeat == right.ActualTurnOwnerSeat && left.Materials.SequenceEqual(right.Materials);

    internal static bool SameGameStartingMovementDrawOrigin(GameStartingMovementDrawOrigin? left, GameStartingMovementDrawOrigin? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null && left.DrawChild == right.DrawChild &&
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_GameStartingNativeMovementInvoices.SameGameStartingNativeMovementOrigin(left.MovementRoot, right.MovementRoot);
}
