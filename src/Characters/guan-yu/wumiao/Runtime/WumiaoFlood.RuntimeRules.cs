using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_guan_yu_wumiao_WumiaoFlood
{
    internal static bool WumiaoFloodDiscardEntryMatches(WumiaoFloodDiscardMovement entry, CardMovementRecord movement) =>
        entry.CardId == movement.CardId && entry.PrintedKind == movement.CardKind && entry.From == movement.From &&
        entry.To == movement.To && entry.Reason == movement.Reason && entry.MovementSequence == movement.Sequence;
}
