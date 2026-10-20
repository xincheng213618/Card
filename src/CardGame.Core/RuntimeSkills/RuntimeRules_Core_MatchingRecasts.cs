using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_MatchingRecasts
{
    internal static bool MatchingRecastGroup(IReadOnlyList<MatchingRecastMaterial> materials) => materials.Count >= 2 &&
        materials.Select(m => m.CardId).Distinct().Count() == materials.Count &&
        (materials.All(m => EquipmentCatalog.IsEquipment(m.PrintedKind)) ||
         materials.All(m => CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(m.PrintedKind) == CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(materials[0].PrintedKind)));

    internal static bool MatchingRecastPeerCursor(ProgramMatchingRecastReceipt r) => r.Stage is
        MatchingRecastStage.PeerCostChildren or MatchingRecastStage.PeerDrawChildren;

    internal static MatchingRecastParticipantReceipt CurrentMatchingRecast(ProgramMatchingRecastReceipt r) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_MatchingRecasts.MatchingRecastPeerCursor(r) ? r.Peer : r.Owner;

    internal static ProgramMatchingRecastReceipt WithCurrentMatchingRecast(ProgramMatchingRecastReceipt r, MatchingRecastParticipantReceipt p) =>
        CardGame.Core.RuntimeSkills.RuntimeRules_Core_MatchingRecasts.MatchingRecastPeerCursor(r) ? r with { Peer = p } : r with { Owner = p };
}
