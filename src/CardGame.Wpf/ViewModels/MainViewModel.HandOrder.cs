using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // Ordering belongs to the local presentation. The ordinary play draft is
    // restored only within the same decision; no command or player view changes.
    private bool IsHandOrderContext() =>
        !_disposed && _snapshot is not null && CanEndTurn && !HasGameOver &&
        !IsDiscardSelectionPending && !IsHandResponsePending && !IsSkillTargetSelectionPending &&
        !_isSelectingActiveSkillCards && !IsActiveSkillSelectionPending && _selectedTieredRoundZeroAction is null &&
        _snapshot.Revision == _game.Revision &&
        _snapshot.PendingDecision is { Kind: DecisionKind.PlayCard } prompt && prompt.PlayerSeat == _snapshot.HumanSeat;

    internal bool CanReorderHandCard(CardViewModel card) =>
        IsHandOrderContext() && Hand.Any(current => ReferenceEquals(current, card));

    // Unlike the aim token, this captures the draft before pressing any card,
    // including an unplayable card or a different conversion material.
    internal PlayCardAimDraft? CaptureHandOrderDraft(CardViewModel card) =>
        CanReorderHandCard(card) ? new PlayCardAimDraft(this, null) : null;

    internal void MoveHandCard(CardViewModel card, CardViewModel target)
    {
        if (!CanReorderHandCard(card) || !CanReorderHandCard(target)) return;
        var from = Hand.IndexOf(card);
        var to = Hand.IndexOf(target);
        if (from != to) Hand.Move(from, to);
    }
}
