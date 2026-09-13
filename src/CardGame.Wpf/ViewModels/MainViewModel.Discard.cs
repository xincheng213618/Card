using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<int> _discardCardIds = [];
    private PromptId? _discardPromptId;

    public bool IsDiscardSelectionPending => _snapshot?.PendingDecision?.Kind == DecisionKind.DiscardCards;
    public int RequiredDiscardCount => IsDiscardSelectionPending ? _snapshot.PendingDecision!.RequiredCardCount : 0;
    public int SelectedDiscardCount => _discardCardIds.Count;
    public ICommand RecommendDiscardCommand { get; private set; } = null!;

    private void SyncDiscardSelection()
    {
        var current = IsDiscardSelectionPending ? _snapshot.PendingDecision!.PromptId : (PromptId?)null;
        if (current != _discardPromptId) _discardCardIds.Clear();
        _discardPromptId = current;
        if (current is not null)
            _discardCardIds.IntersectWith(_snapshot.PendingDecision!.ValidCardIds);
    }

    private void ToggleDiscardCard(CardViewModel card)
    {
        if (!_snapshot.PendingDecision!.ValidCardIds.Contains(card.Id)) return;
        if (!_discardCardIds.Remove(card.Id)) _discardCardIds.Add(card.Id);
        card.IsSelected = _discardCardIds.Contains(card.Id);
        RefreshSelectionHint();
        RefreshDiscardPresentation();
    }

    private void RecommendDiscard()
    {
        if (!IsDiscardSelectionPending) return;
        var human = _snapshot.Players.Single(player => player.IsHuman);
        var recommended = human.Hand.OrderBy(card => CardCatalog.Get(card.Kind).HandKeepValue +
                (card.Kind == CardKind.Peach && human.Hp < human.MaxHp ? 50 : 0))
            .ThenBy(card => card.Id).Take(RequiredDiscardCount);
        _discardCardIds.Clear();
        foreach (var card in recommended) _discardCardIds.Add(card.Id);
        foreach (var card in Hand) card.IsSelected = _discardCardIds.Contains(card.Id);
        RefreshSelectionHint();
        RefreshDiscardPresentation();
    }

    private void ConfirmDiscard()
    {
        if (!IsDiscardSelectionPending || !CanConfirmSelected) return;
        var prompt = _snapshot.PendingDecision!;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new DiscardCardsCommand(_snapshot.HumanSeat,
                _discardCardIds.Order().ToArray(), prompt.PromptId, _snapshot.Revision));
            if (!result.Accepted)
            {
                ActionHint = $"弃牌未执行：{result.Error?.Message}";
                return;
            }
            _discardCardIds.Clear();
            Refresh(result.State);
        });
    }

    private void RefreshDiscardPresentation()
    {
        foreach (var name in new[] { nameof(IsDiscardSelectionPending), nameof(RequiredDiscardCount), nameof(SelectedDiscardCount), nameof(CanActFromHand) })
            RaisePropertyChanged(name);
    }
}
