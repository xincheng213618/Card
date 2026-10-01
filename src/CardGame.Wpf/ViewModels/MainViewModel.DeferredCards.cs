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
        RaisePropertyChanged(nameof(HasPrivatelyViewedCards));
        RaisePropertyChanged(nameof(HasDeferredPublicPiles));
        RaisePropertyChanged(nameof(PrivateRevealTitle));
    }

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
