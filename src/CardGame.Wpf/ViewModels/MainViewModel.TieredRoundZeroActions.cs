using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // This is a UI draft of a published action, never a physical card or an
    // engine resolution. Every confirmation resolves it against the current view.
    private LegalAction? _selectedTieredRoundZeroAction;

    private static bool IsTieredRoundZeroAction(LegalAction action) =>
        action.CardId == 0 && action.TieredRoundZeroUse is not null;

    private LegalAction? SelectedTieredRoundZeroAction() =>
        _selectedTieredRoundZeroAction is { } selected
            ? HumanActiveSkillActions.FirstOrDefault(action => SameTieredRoundZeroAction(action, selected))
            : null;

    private static bool SameTieredRoundZeroAction(LegalAction left, LegalAction right) =>
        IsTieredRoundZeroAction(left) && IsTieredRoundZeroAction(right) &&
        left.Kind == right.Kind && left.TargetSeat == right.TargetSeat && left.PlayedCardKind == right.PlayedCardKind &&
        left.TargetCardId == right.TargetCardId && left.TargetSeats.SequenceEqual(right.TargetSeats) &&
        left.ConversionSource == right.ConversionSource && left.TieredRoundZeroUse == right.TieredRoundZeroUse &&
        (left.AdditionalConversionSources ?? []).SequenceEqual(right.AdditionalConversionSources ?? []);

    private static int ActiveSkillMaximumTargets(LegalAction action) =>
        IsTieredRoundZeroAction(action) ? action.TargetSeats.Count : action.MaxTargetCount;

    private static IReadOnlyList<int> ActiveSkillSelectableTargets(LegalAction action) =>
        IsTieredRoundZeroAction(action) ? action.TargetSeats : action.SelectableTargetSeats;

    private bool CanConfirmTieredRoundZeroAction(LegalAction action) =>
        _selectedTieredRoundZeroAction is not null && _selectedActiveSkillCardIds.Count == 0 &&
        _selectedActiveSkillTargetSeats.Count == action.TargetSeats.Count &&
        action.TargetSeats.ToHashSet().SetEquals(_selectedActiveSkillTargetSeats);

    private void ClearTieredRoundZeroSelection()
    {
        if (_selectedTieredRoundZeroAction is null) return;
        _selectedTieredRoundZeroAction = null;
        _selectedActiveSkillCardIds.Clear();
        _selectedActiveSkillTargetSeats.Clear();
        _isSelectingActiveSkillCards = false;
    }

    private void UseTieredRoundZeroAction(LegalAction selected, bool beginSelection = false)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt) return;
        var action = HumanActiveSkillActions.FirstOrDefault(candidate => SameTieredRoundZeroAction(candidate, selected));
        if (action is null) return;

        if (beginSelection || !_isSelectingActiveSkillCards)
        {
            ClearTieredRoundZeroSelection();
            _selectedTieredRoundZeroAction = action;
            _selectedCardId = null;
            _selectedConversionSource = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _selectedEquipmentEffectKind = null;
            _selectedProgramSkillId = null;
            _selectedProgramActivationId = null;
            _selectedProgramSkillOwnerSeat = null;
            _selectedActiveSkillTargetContract = null;
            _isSelectingActiveSkillCards = action.TargetSeats.Count > 0;
            if (_isSelectingActiveSkillCards)
            {
                RefreshCurrentView();
                return;
            }
        }
        else if (_selectedActiveSkillTargetSeats.Count == 0 && action.TargetSeats.Count > 0)
        {
            ClearTieredRoundZeroSelection();
            RefreshCurrentView();
            return;
        }

        if (!CanConfirmTieredRoundZeroAction(action)) return;
        ExecuteSafely(() =>
        {
            // The action owns target order and every conversion source. No owned
            // card lookup, forged entity zero, or UseProgramSkill command is used.
            var result = SubmitCommand(CreatePlayCommand(_snapshot.HumanSeat, 0, action,
                _snapshot.Revision, prompt.PromptId));
            if (result.Accepted)
            {
                ClearTieredRoundZeroSelection();
                SelectedCardText = "未选择手牌";
            }
            else PromptText = $"出牌未执行：{result.Error?.Message}";
            RefreshCommandResult(result.State);
        });
    }
}
