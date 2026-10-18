namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PlayerMarkerSnapshot>? AppendMouLixianProgressMarkers(CharacterState player,
        bool canSeeGeneral, IReadOnlyList<PlayerMarkerSnapshot>? ordinary)
    {
        if (!canSeeGeneral || player.IdentityConcealed || !HasRuntimeSkill(player, "mou-ol:lixian")) return ordinary;
        var added = MouLixianAddedNames(player.Seat);
        var used = MouLixianUsedNames(player.Seat);
        if (added.Count == 0 && used.Count == 0) return ordinary;
        var available = new[] { CardKind.DrawTwo }.Concat(added).Distinct().ToArray();
        var markers = (ordinary ?? []).ToList();
        var names = "理贤牌名：" + string.Join("、", available.Select(kind => CardCatalog.Get(kind).DisplayName));
        if (MouLixianPreparationUnlocked(player.Seat)) names += "（准备阶段已解锁）";
        markers.Add(new(PlayerMarkerKind.MouLixianAvailableNames, names, available.Length));
        if (used.Count > 0)
        {
            var progress = "理贤已用牌名";
            if (MouLixianEndingUnlocked(player.Seat)) progress += "（结束阶段已解锁）";
            markers.Add(new(PlayerMarkerKind.MouLixianUsedNames, progress, used.Count));
        }
        return Array.AsReadOnly(markers.OrderBy(marker => marker.Kind)
            .ThenBy(marker => marker.Name, StringComparer.Ordinal).ToArray());
    }
}
