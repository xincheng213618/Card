using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static IReadOnlyList<SeatStatusViewModel> ProjectSeatStatuses(PlayerSnapshot player)
    {
        var statuses = new List<SeatStatusViewModel>();
        if (player.WenheChampionMarkUsed == true)
            statuses.Add(new("志", "已使用「志在必得」。该使用状态已公开。"));
        foreach (var group in (player.ActiveEffects ?? []).GroupBy(effect => effect.SkillId))
        {
            var descriptions = group.Select(effect => $"{effect.Description}\n{ActiveEffectExpiryText(effect)}")
                .GroupBy(text => text).Select(items => items.Count() == 1 ? items.Key : $"{items.Key}（{items.Count()}份）");
            var uses = group.Select(effect => effect.RemainingUses ?? 0).DefaultIfEmpty().Max();
            statuses.Add(new(group.First().SkillName, string.Join("\n\n", descriptions), uses)
                { ShowSingleCount = uses > 0 });
        }
        foreach (var marker in (player.Markers ?? []).Where(marker => marker.Count > 0 && marker.Kind is not (PlayerMarkerKind.Gale or PlayerMarkerKind.Mist))
                     .OrderBy(marker => marker.Kind == PlayerMarkerKind.Lie ? 0 : 1))
        {
            var label = marker.Kind switch
            {
                PlayerMarkerKind.Rage => "怒",
                PlayerMarkerKind.ShenwuPoison => "蛇毒",
                PlayerMarkerKind.QihuiBasic => "诲基",
                PlayerMarkerKind.QihuiTrick => "诲锦",
                PlayerMarkerKind.QihuiEquipment => "诲装",
                _ => marker.Name
            };
            var detail = marker.Kind switch
            {
                PlayerMarkerKind.Lie => "\n武烈：受到伤害时，移除一枚“烈”并防止此次伤害。",
                PlayerMarkerKind.ShenwuPoison => "\n回合开始时弃置等量牌或失去等量体力，然后移去一枚蛇毒。",
                PlayerMarkerKind.Zi => "\n截辎：摸牌阶段结束时移除“辎”，然后执行一个额外的摸牌阶段。",
                _ => string.Empty
            };
            statuses.Add(new(label, $"{marker.Name}标记 ×{marker.Count}{detail}", marker.Count)
                { IsDrawPhaseBonus = marker.Kind == PlayerMarkerKind.Zi, IsTitle = marker.Kind == PlayerMarkerKind.Jue });
        }
        if (player.HasAlcoholEffect) statuses.Add(new("酒", AlcoholEffectTooltip(player)));
        if (player.BuquWounds is { Count: > 0 } wounds)
            statuses.Add(new("创", $"不屈创牌点数：{string.Join('/', wounds.Select(card => card.Rank))}；手牌上限等于创牌数。", wounds.Count));
        if (player.AuthorityCount > 0)
            statuses.Add(new(player.AuthorityName ?? "权", $"公开的“{player.AuthorityName ?? "权"}”牌 ×{player.AuthorityCount}；手牌上限增加其数量。", player.AuthorityCount));
        if (player.ChunlaoCount > 0)
            statuses.Add(new("醇", $"公开的“醇” ×{player.ChunlaoCount}：{string.Join("、", (player.ChunlaoCards ?? []).Select(card => $"{card.DisplayName} {GetSuitGlyph(card.Suit)}{card.RankText}"))}", player.ChunlaoCount));
        if (player.PojunHoldCount > 0)
            statuses.Add(new("破", $"被【破军】扣置的牌 {player.PojunHoldCount} 张；回合结束后回到其手牌。", player.PojunHoldCount));
        if (player.RoundHoldCount > 0)
            statuses.Add(new("撼", $"被【撼国】扣置的牌 {player.RoundHoldCount} 张；本轮结束后回到其手牌。", player.RoundHoldCount));
        foreach (var pile in PublicOwnedPiles(player))
            statuses.Add(new(pile.Name, $"{player.GeneralName}的公开“{pile.Name}” ×{pile.Count}：{string.Join("、", pile.Cards.Select(card => $"{card.DisplayName} {GetSuitGlyph(card.Suit)}{card.RankText}"))}", pile.Count));
        foreach (var pile in player.NamedAmbushPiles ?? [])
            if (pile.Count > 0)
                statuses.Add(new(pile.PileName, $"暗置“{pile.PileName}” ×{pile.Count}；牌面与记录牌名仅拥有者可见。", pile.Count));
        foreach (var pile in player.PrivateWhirlPiles ?? [])
            if (pile.Count > 0)
                statuses.Add(new(pile.PileName, $"暗置“{pile.PileName}” ×{pile.Count}；牌面仅拥有者可见。", pile.Count));
        if (player.DeferredHandAlignments is { Count: > 0 } dues)
            statuses.Add(new("对齐", DeferredHandAlignmentTooltip(player), dues.Count));
        if (HasIssuedUseProhibition(player)) statuses.Add(new("禁用", IssuedUseProhibitionTooltip(player)));
        if (player.IssuedPlayPhaseSuitUseAllowances is { Count: > 0 })
            statuses.Add(new("不限", PhaseSuitAllowanceTooltip(player)));
        foreach (var suit in (player.BeneficiarySuitShields ?? []).Select(shield => shield.Suit).Distinct())
            statuses.Add(new(GetSuitGlyph(suit), $"其他角色的{GetSuitGlyph(suit)}牌不能指定此角色；下个本人回合开始失效。"));
        return player.IsHuman
            ? Array.AsReadOnly(statuses.Select(status => status with { ShowSingleCount = true }).ToArray())
            : statuses.AsReadOnly();
    }

    private static string ActiveEffectExpiryText(ActivePlayerEffectSnapshot effect) => effect.Expiry switch
    {
        ActivePlayerEffectExpiry.CurrentTurnEnd => "本回合结束时失效。",
        ActivePlayerEffectExpiry.NextSourceTurnStart => $"第{effect.SourceSeatDisplayNumber ?? effect.SourceSeat + 1}席下个回合开始时失效。",
        ActivePlayerEffectExpiry.PlayPhaseEnd => "本出牌阶段结束时失效。",
        ActivePlayerEffectExpiry.ConsumedOrTurnEnd => "仅下一次符合条件的用牌；消耗后移除，未消耗则本回合结束时清除。",
        ActivePlayerEffectExpiry.AffectedNextTurnEnd => "本人实际回合结束时清除。",
        ActivePlayerEffectExpiry.AffectedNextTurn => "等待本人的下个回合生效。",
        ActivePlayerEffectExpiry.RuleCleared => "随对应规则状态解除或消耗后移除。",
        _ => string.Empty
    };
}
