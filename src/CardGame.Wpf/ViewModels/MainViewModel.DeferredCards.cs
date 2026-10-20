using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GameEngine? _publicCardViewGame;
    private long _publicCardViewRevision = -1;
    private IReadOnlyList<(CardSnapshot Card, bool IsChoice)> _publicCardViewSource = [];

    public ObservableCollection<CardViewModel> PrivatelyViewedCards { get; } = [];
    public bool HasPrivatelyViewedCards => PrivatelyViewedCards.Count > 0;
    public string PrivateRevealTitle => $"{_snapshot.PendingDecision?.SkillPrompt?.Name ?? "观看的牌"} · 仅你可见";

    private PromptChoice? RevealedCardChoice(CardViewModel card) =>
        card.IsPrivateReveal || !card.IsPublicChoice ||
        !PublicRevealedCards.Any(current => ReferenceEquals(current, card))
            ? null : RevealedCardChoice(card.Id);

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

    private void RebuildPublicRevealedCards()
    {
        var game = _game;
        var revision = _snapshot.Revision;
        var source = _snapshot.PublicRevealedCards.Select(card => (Card: card, IsChoice: RevealedCardChoice(card.Id) is not null)).ToArray();
        if (ReferenceEquals(_publicCardViewGame, game) && _publicCardViewRevision == revision &&
            _publicCardViewSource.SequenceEqual(source)) return;

        PublicRevealedCards.Clear();
        foreach (var (card, isChoice) in source)
        {
            var definition = CardCatalog.Get(card.Kind);
            PublicRevealedCards.Add(new CardViewModel
            {
                Id = card.Id,
                Kind = card.Kind,
                Name = card.DisplayName,
                KindLabel = definition.CategoryName,
                SuitGlyph = GetSuitGlyph(card.Suit),
                Rank = card.RankText,
                Description = GetCardDescription(card.Kind),
                OriginalHandTag = card.SkillMarkLabel ?? string.Empty,
                IsPlayable = false,
                IsPublicChoice = isChoice,
                PublicHandOwnerLabel = card.PublicHandOwnerSeat is { } ownerSeat
                    ? _snapshot.Players.Single(player => player.Seat == ownerSeat).Name : string.Empty,
                IsSelected = false
            });
        }
        HasPublicRevealedCards = PublicRevealedCards.Count > 0;
        _publicCardViewGame = game;
        _publicCardViewRevision = revision;
        _publicCardViewSource = source;
    }

    private void RebuildDeferredCardViews()
    {
        RebuildNamedPrivatePileViews();
        PrivatelyViewedCards.Clear();
        foreach (var card in _snapshot.PrivateRevealedCards ?? [])
            PrivatelyViewedCards.Add(CreateViewedCard(card, privateView: true));
        RaisePropertyChanged(nameof(HasPrivatelyViewedCards));
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
        // Issued foreign curses are target-owned public cards, not own-source piles.
        foreach (var curse in player.SourceCurses ?? [])
            yield return ("咒", 1, new[] { curse.Card });
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
        OriginalHandTag = card.SkillMarkLabel ?? string.Empty,
        IsPrivateReveal = privateView,
        IsPublicChoice = false,
        IsSelected = false
    };
}
