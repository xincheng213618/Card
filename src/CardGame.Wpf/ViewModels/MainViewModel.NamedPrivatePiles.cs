using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record NamedPrivatePileEntryViewModel(CardViewModel Card, string RecordedName);
public sealed record NamedPrivatePileViewModel(int OwnerSeat, string Title,
    IReadOnlyList<NamedPrivatePileEntryViewModel> Entries)
{
    public bool HasPrivateEntries => Entries.Count > 0;
    public string PrivacyHint => HasPrivateEntries ? "牌面与记录牌名 · 仅你可见" : "牌面与记录牌名仅拥有者可见";
}

public sealed partial class MainViewModel
{
    public ObservableCollection<NamedPrivatePileViewModel> NamedPrivatePiles { get; } = [];
    public bool HasNamedPrivatePiles => NamedPrivatePiles.Count > 0;

    private void RebuildNamedPrivatePileViews()
    {
        NamedPrivatePiles.Clear();
        foreach (var player in _snapshot.Players)
            foreach (var pile in player.NamedAmbushPiles ?? [])
            {
                if (pile.Count <= 0) continue;
                // A developer/retained DTO may contain other players' entries.
                // Persistent table information still exposes private pairings only to this viewer's own seat.
                var entries = player.Seat == _snapshot.HumanSeat && pile.Entries is { } owned
                    ? owned.Select(entry => new NamedPrivatePileEntryViewModel(CreateViewedCard(entry.Card, privateView: true),
                        $"记录：{CardCatalog.Get(entry.RecordedKind).DisplayName}")).ToArray()
                    : [];
                NamedPrivatePiles.Add(new(player.Seat, $"{player.GeneralName} · {pile.PileName} {pile.Count}张",
                    Array.AsReadOnly(entries)));
            }
        RaisePropertyChanged(nameof(HasNamedPrivatePiles));
    }

    private static string NamedPrivatePileBadge(PlayerSnapshot player) => string.Join(" · ",
        (player.NamedAmbushPiles ?? []).Where(pile => pile.Count > 0).Select(pile => $"{pile.PileName} ×{pile.Count}"));

    private static string NamedPrivatePileTooltip(PlayerSnapshot player) => string.Join("\n",
        (player.NamedAmbushPiles ?? []).Where(pile => pile.Count > 0)
            .Select(pile => $"{player.GeneralName}的暗置“{pile.PileName}”：{pile.Count}张，牌面与记录牌名仅拥有者可见。"));
}
