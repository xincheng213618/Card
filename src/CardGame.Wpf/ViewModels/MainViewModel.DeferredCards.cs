using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record DeferredPublicPileViewModel(int OwnerSeat, string Title, IReadOnlyList<CardViewModel> Cards);

public sealed partial class MainViewModel
{
    public ObservableCollection<CardViewModel> PrivatelyViewedCards { get; } = [];
    public ObservableCollection<DeferredPublicPileViewModel> DeferredPublicPiles { get; } = [];
    public bool HasPrivatelyViewedCards => PrivatelyViewedCards.Count > 0;
    public bool HasDeferredPublicPiles => DeferredPublicPiles.Count > 0;
    public string PrivateRevealTitle => $"{_snapshot.PendingDecision?.SkillPrompt?.Name ?? "观看的牌"} · 仅你可见";

    private PromptChoice? RevealedCardChoice(int cardId)
    {
        var prompt = _snapshot.PendingDecision;
        if (prompt is null || prompt.PlayerSeat != _snapshot.HumanSeat ||
            !_snapshot.PublicRevealedCards.Any(card => card.Id == cardId)) return null;
        if (prompt.Kind == DecisionKind.SelectHarvestCard)
            return prompt.Choices.FirstOrDefault(choice => choice.Cards.Contains(cardId));
        if (prompt.SkillPrompt is null) return null;
        var candidates = prompt.Choices.Where(choice => choice.Cards.Count == 1 &&
            choice.Cards[0] == cardId).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private void RebuildDeferredCardViews()
    {
        PrivatelyViewedCards.Clear();
        foreach (var card in _snapshot.PrivateRevealedCards ?? [])
            PrivatelyViewedCards.Add(CreateViewedCard(card, privateView: true));
        DeferredPublicPiles.Clear();
        foreach (var player in _snapshot.Players.Where(player => player.PublicDeferredPileCards is { Count: > 0 }))
            DeferredPublicPiles.Add(new(player.Seat,
                $"{player.GeneralName} · {player.PublicDeferredPileName ?? "牌堆"} {player.PublicDeferredPileCount}张",
                player.PublicDeferredPileCards!.Select(card => CreateViewedCard(card, privateView: false)).ToArray()));
        foreach (var player in _snapshot.Players)
            foreach (var pile in PublicPersistentOwnedPiles(player).Where(pile => pile.Count > 0))
                DeferredPublicPiles.Add(new(player.Seat,
                    $"{player.GeneralName} · {pile.Name} {pile.Count}张",
                    pile.Cards.Select(card => CreateViewedCard(card, privateView: false)).ToArray()));
        RaisePropertyChanged(nameof(HasPrivatelyViewedCards));
        RaisePropertyChanged(nameof(HasDeferredPublicPiles));
        RaisePropertyChanged(nameof(PrivateRevealTitle));
    }

    private static IEnumerable<(string Name, int Count, IReadOnlyList<CardSnapshot> Cards)> PublicOwnedPiles(PlayerSnapshot player)
    {
        if (player.PublicDeferredPileCount > 0)
            yield return (player.PublicDeferredPileName ?? "牌堆", player.PublicDeferredPileCount, player.PublicDeferredPileCards ?? []);
        foreach (var pile in PublicPersistentOwnedPiles(player)) yield return pile;
    }

    private static IEnumerable<(string Name, int Count, IReadOnlyList<CardSnapshot> Cards)> PublicPersistentOwnedPiles(PlayerSnapshot player)
    {
        if (player.PublicPersistentPiles is { Count: > 0 } piles)
        {
            foreach (var pile in piles.Where(pile => pile.Count > 0))
                yield return (pile.Name ?? "牌堆", pile.Count, pile.Cards);
        }
        else if (player.PublicPersistentPileCount > 0)
            yield return (player.PublicPersistentPileName ?? "牌堆", player.PublicPersistentPileCount, player.PublicPersistentPileCards ?? []);
    }

    private static string PublicPileBadge(PlayerSnapshot player) =>
        string.Join(" · ", PublicOwnedPiles(player).Select(pile => $"{pile.Name} ×{pile.Count}"));

    private static string PublicPileTooltip(PlayerSnapshot player) =>
        string.Join("\n", PublicOwnedPiles(player).Select(pile =>
            $"{player.GeneralName}的公开“{pile.Name}”：{string.Join("、", pile.Cards.Select(card => $"{card.DisplayName} {GetSuitGlyph(card.Suit)}{card.RankText}"))}"));

    private CardViewModel CreateViewedCard(CardSnapshot card, bool privateView) => new()
    {
        Id = card.Id,
        Kind = card.Kind,
        Name = card.DisplayName,
        KindLabel = CardCatalog.Get(card.Kind).CategoryName,
        SuitGlyph = GetSuitGlyph(card.Suit),
        Rank = card.RankText,
        Description = GetCardDescription(card.Kind),
        IsPlayable = false,
        IsPrivateReveal = privateView,
        IsPublicChoice = false,
        IsSelected = false
    };
}
