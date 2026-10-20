using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OffTurnUsedCards
{
    internal static bool IsOffTurnNativeCompletion(CardMovementRecord m, OffTurnUsedCardKind kind) =>
        m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && (kind switch
        {
            OffTurnUsedCardKind.CardUse => m.Reason == CardMoveReasons.UseFinished || m.Reason == CardMoveReasons.IronChainFinished,
            OffTurnUsedCardKind.DodgeUse => m.Reason == CardMoveReasons.ResponseFinished,
            OffTurnUsedCardKind.NullificationUse => m.Reason == CardMoveReasons.NullificationFinished,
            _ => false
        });
}
