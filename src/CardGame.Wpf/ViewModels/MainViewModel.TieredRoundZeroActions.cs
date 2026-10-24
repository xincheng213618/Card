using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // This is a UI draft of a published action, never a physical card or an
    // engine resolution. Every confirmation resolves it against the current view.
    private LegalAction? _selectedTieredRoundZeroAction;

    private static bool IsTieredRoundZeroAction(LegalAction action) =>
        action.CardId == 0 && (action.TieredRoundZeroUse is not null || action.DrawFundedDistinctBasicUse is not null || action.ChainedStateBasicUse.HasValue || action.RoundOnceCardlessBasicUse is not null);

    private LegalAction? SelectedTieredRoundZeroAction() =>
        _selectedTieredRoundZeroAction is { } selected
            ? HumanActiveSkillActions.FirstOrDefault(action => SameTieredRoundZeroFamily(action, selected) &&
                action.TargetSeats.Count == _selectedActiveSkillTargetSeats.Count &&
                action.TargetSeats.ToHashSet().SetEquals(_selectedActiveSkillTargetSeats)) ??
              HumanActiveSkillActions.FirstOrDefault(action => SameTieredRoundZeroFamily(action, selected))
            : null;

    private static bool SameTieredRoundZeroFamily(LegalAction left, LegalAction right) =>
        left.TargetSeats.Count == 1 && right.TargetSeats.Count == 1
            ? SameTieredRoundZeroAction(left with { TargetSeat = right.TargetSeat, TargetSeats = right.TargetSeats }, right)
            : SameTieredRoundZeroAction(left, right);

    private static bool SameTieredRoundZeroAction(LegalAction left, LegalAction right) =>
        IsTieredRoundZeroAction(left) && IsTieredRoundZeroAction(right) &&
        left.Kind == right.Kind && left.TargetSeat == right.TargetSeat && left.PlayedCardKind == right.PlayedCardKind &&
        left.TargetCardId == right.TargetCardId && left.TargetSeats.SequenceEqual(right.TargetSeats) &&
        left.ConversionSource == right.ConversionSource && left.TieredRoundZeroUse == right.TieredRoundZeroUse &&
        left.DrawFundedDistinctBasicUse == right.DrawFundedDistinctBasicUse &&
        left.ChainedStateBasicUse == right.ChainedStateBasicUse &&
        left.RoundOnceCardlessBasicUse == right.RoundOnceCardlessBasicUse &&
        (left.AdditionalConversionSources ?? []).SequenceEqual(right.AdditionalConversionSources ?? []);

    private static int ActiveSkillMaximumTargets(LegalAction action) =>
        IsTieredRoundZeroAction(action) ? action.TargetSeats.Count : action.MaxTargetCount;

    private IReadOnlyList<int> ActiveSkillSelectableTargets(LegalAction action) =>
        IsTieredRoundZeroAction(action) ? HumanActiveSkillActions.Where(candidate => SameTieredRoundZeroFamily(candidate, action))
            .Where(candidate => _selectedActiveSkillTargetSeats.All(candidate.TargetSeats.Contains))
            .SelectMany(candidate => candidate.TargetSeats).Distinct().ToArray() : action.SelectableTargetSeats;

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
        var action = beginSelection || !_isSelectingActiveSkillCards
            ? HumanActiveSkillActions.FirstOrDefault(candidate => SameTieredRoundZeroAction(candidate, selected))
            : SelectedTieredRoundZeroAction();
        if (action is null) return;

        if (beginSelection || !_isSelectingActiveSkillCards)
        {
            ClearTieredRoundZeroSelection();
            _selectedTieredRoundZeroAction = action;
            _selectedCardId = null;
            _selectedConversionSource = null;
            _playConversionSelection = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _selectedActiveSkillIdentity = null;
            _isSelectingActiveSkillCards = action.TargetSeats.Count > 0;
            if (_isSelectingActiveSkillCards)
            {
                SelectOnlyActiveSkillTarget();
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
