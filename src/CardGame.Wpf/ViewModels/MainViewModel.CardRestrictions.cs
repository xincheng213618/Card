using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static string PublicStateBadge(PlayerSnapshot player) =>
        string.Join(" · ", new[] { PublicPileBadge(player), PublicMarkerBadge(player), DeferredHandAlignmentBadge(player), SuitShieldBadge(player) }
            .Where(text => text.Length > 0));

    private static string PublicStateTooltip(PlayerSnapshot player) =>
        string.Join("\n", new[] { PublicPileTooltip(player), PublicMarkerBadge(player), DeferredHandAlignmentTooltip(player), SuitShieldTooltip(player) }
            .Where(text => text.Length > 0));

    private static string PublicMarkerBadge(PlayerSnapshot player) =>
        string.Join(" · ", (player.Markers ?? []).Where(marker => marker.Count > 0)
            .Select(marker => $"{marker.Name} ×{marker.Count}"));

    private static string DeferredHandAlignmentBadge(PlayerSnapshot player) =>
        player.DeferredHandAlignments is { Count: > 0 } dues ? $"待对齐 ×{dues.Count}" : string.Empty;

    private static string DeferredHandAlignmentTooltip(PlayerSnapshot player) =>
        string.Join("\n", (player.DeferredHandAlignments ?? []).Select(due =>
            $"来自第 {due.Source.OwnerSeat + 1} 席 · " +
            (due.DueKind == DeferredHandAlignmentDueKind.SourceCurrentTurnEnd
                ? "本回合结束时" : "下次实际回合结束时") +
            "按来源当时手牌数对齐；摸牌至多到五张"));

    private bool IsPhysicalCardRestricted(int seat, int cardId) =>
        (_snapshot.TurnProhibitedPhysicalCards ?? []).Any(restriction =>
            restriction.RecipientSeat == seat && restriction.TurnNumber == _snapshot.TurnNumber &&
            restriction.TurnSeat == _snapshot.CurrentSeat && restriction.CardIds.Contains(cardId));

    private static string SuitShieldBadge(PlayerSnapshot player) =>
        string.Join("／", (player.BeneficiarySuitShields ?? []).Select(shield =>
            $"{GetSuitGlyph(shield.Suit)}保护").Distinct());

    private static string SuitShieldTooltip(PlayerSnapshot player) =>
        string.Join("\n", (player.BeneficiarySuitShields ?? []).Select(shield =>
            $"其他角色的{GetSuitGlyph(shield.Suit)}牌不能指定此角色；下个本人回合开始失效").Distinct());
}
