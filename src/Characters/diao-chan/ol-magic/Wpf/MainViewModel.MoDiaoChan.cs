using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private ForcedHandSelectionSnapshot? NormalForcedHandSelection
    {
        get
        {
            if (_snapshot?.PendingDecision is not { Kind: DecisionKind.PlayCard, ForcedHandSelection: { } hint } prompt ||
                prompt.PlayerSeat != _snapshot.HumanSeat || hint.RemainingUses <= 0 ||
                IsActiveSkillSelectionPending || IsOwnedCardSelectionPending || IsCardPaymentSelectionPending ||
                IsSkillTargetSelectionPending || IsDiscardSelectionPending || IsHandResponsePending)
                return null;
            return _snapshot.Players.Single(player => player.Seat == _snapshot.HumanSeat).Hand
                .Any(card => card.Id == hint.CardId) ? hint : null;
        }
    }

    // This is a local draft only. The player still chooses targets and confirms,
    // or uses a skill or ends Play through the existing command paths.
    private void SyncForcedHandSelection()
    {
        if (NormalForcedHandSelection is not { } hint) return;
        var changed = _selectedCardId != hint.CardId;
        _selectedCardId = hint.CardId;
        if (changed)
        {
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            if (!IsPlayConversionSelectionPending) _selectedConversionSource = null;
            var card = _snapshot.Players.Single(player => player.Seat == _snapshot.HumanSeat).Hand
                .Single(card => card.Id == hint.CardId);
            SelectedCardText = IsPlayConversionSelectionPending ? PlayConversionSelectionText
                : $"幻惑锁定：{card.DisplayName}（仍需确认使用）";
        }
        foreach (var card in Hand) card.IsSelected = card.Id == hint.CardId;
    }

    private bool KeepForcedHandSelection()
    {
        if (NormalForcedHandSelection is null) return false;
        SyncForcedHandSelection();
        return true;
    }
}
