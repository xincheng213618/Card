using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // A pointer gesture may restore the ordinary draft which its initial
    // press toggled off. This token carries UI state, never a rule action.
    internal sealed class PlayCardAimDraft
    {
        private readonly MainViewModel _owner;
        private readonly GameEngine _game;
        private readonly CardViewModel _card;
        private readonly long _revision;
        private readonly int _humanSeat;
        private readonly PromptId _promptId;
        private readonly CardConversionSource? _conversion;
        private readonly PlayConversionDraft? _playConversion;
        private readonly int? _target;
        private readonly int[] _targets;
        private readonly string _text;

        internal PlayCardAimDraft(MainViewModel owner, CardViewModel card)
        {
            _owner = owner;
            _game = owner._game;
            _card = card;
            _revision = owner._snapshot.Revision;
            _humanSeat = owner._snapshot.HumanSeat;
            _promptId = owner._snapshot.PendingDecision!.PromptId;
            _conversion = owner._selectedConversionSource;
            _playConversion = owner._playConversionSelection;
            _target = owner._selectedTargetSeat;
            _targets = owner._selectedCardTargetSeats.ToArray();
            _text = owner.SelectedCardText;
        }

        internal bool Restore(MainViewModel owner)
        {
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(owner._game, _game) ||
                owner._snapshot.Revision != _revision || owner._game.Revision != _revision ||
                owner._snapshot.HumanSeat != _humanSeat || owner._snapshot.PendingDecision?.PromptId != _promptId ||
                !owner.IsCardAimContext(_card) ||
                owner._selectedCardId is { } currentCardId && currentCardId != _card.Id)
                return false;

            // Re-resolve the captured source against this exact current view.
            // An incomplete physical-card draft can legitimately have no
            // source selected yet, so it need not already be confirmable.
            var actions = owner.GetViewLegalActions().Where(action => action.CardId == _card.Id &&
                action.ConversionSource == _conversion && (_playConversion is null || _playConversion.Mode?.Matches(action) == true)).ToArray();
            if (_conversion is not null && actions.Length == 0 ||
                _target is { } target && !actions.Any(action => action.TargetSeats.Contains(target)) ||
                _targets.Length > 0 && !actions.Any(action => _targets.All(action.TargetSeats.Contains)))
                return false;

            owner._selectedCardId = _card.Id;
            var changedPlayConversionMode = owner._playConversionSelection != _playConversion;
            owner._selectedConversionSource = _conversion;
            owner._playConversionSelection = _playConversion;
            owner._selectedTargetSeat = _target;
            owner._selectedCardTargetSeats.Clear();
            owner._selectedCardTargetSeats.UnionWith(_targets);
            foreach (var card in owner.Hand) card.IsSelected = ReferenceEquals(card, _card);
            owner.SelectedCardText = _text;
            if (changedPlayConversionMode)
            {
                owner._humanSkillSnapshot = null;
                owner.RefreshCurrentView();
                return true;
            }
            owner.RebuildEquipmentPlayChoices(owner._snapshot.Players.Single(player => player.IsHuman), owner.GetViewLegalActions());
            owner.RebuildPublicTargetChoices();
            owner.RebuildTargetCombinationChoices();
            owner.RefreshTargetHighlights();
            return true;
        }
    }

    internal PlayCardAimDraft? CapturePlayCardAimDraft(CardViewModel card) =>
        IsCardAimContext(card) && card.IsSelected && _selectedCardId == card.Id
            ? new PlayCardAimDraft(this, card) : null;

    internal bool RestorePlayCardAimDraft(PlayCardAimDraft draft) => draft.Restore(this);

    internal bool IsCardAimContext(CardViewModel card)
    {
        if (_disposed || _snapshot is null || !CanEndTurn || HasGameOver ||
            IsDiscardSelectionPending || IsHandResponsePending || IsSkillTargetSelectionPending ||
            _isSelectingActiveSkillCards || IsActiveSkillSelectionPending || _selectedTieredRoundZeroAction is not null ||
            _snapshot.Revision != _game.Revision ||
            _snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt ||
            prompt.PlayerSeat != _snapshot.HumanSeat || !card.IsPlayable ||
            !Hand.Any(current => ReferenceEquals(current, card)))
            return false;

        var human = _snapshot.Players.FirstOrDefault(player => player.IsHuman && player.Seat == _snapshot.HumanSeat);
        var physical = human?.Hand.Concat(human.WoodenOxGrain ?? []).FirstOrDefault(candidate => candidate.Id == card.Id);
        return physical is not null && physical.Kind == card.Kind &&
            GetViewLegalActions().Any(action => action.CardId == card.Id && action.Kind != LegalActionKind.Recast);
    }

    internal bool CanAimCardWithoutTarget(CardViewModel card)
    {
        if (!IsCardAimContext(card) || !card.IsSelected || _selectedCardId != card.Id ||
            !CanConfirmSelected || IsMultiTargetCardSelected ||
            SelectedPlayTargets().Count != 0 && SelectedHellReturnSoulAction() is null)
            return false;

        // Follow ConfirmSelectedCommand's physical-first, conversion-second
        // branch and its exact unique-action selection, including provenance.
        var converted = !CanPlaySelected && CanPlaySelectedAsSlash;
        if (!CanPlaySelected && !converted) return false;
        var action = SelectPlayAction(GetViewLegalActions().Where(MatchesSelectedPlayConversion).ToArray(), card.Id, SelectedPlayTargets(), (card.IdentityKind ?? card.Kind)!.Value,
            converted, _selectedConversionSource);
        return action is { TargetCardId: null } && action.Kind != LegalActionKind.Recast &&
            (action.TargetSeats.Count == 0 || action.Kind == LegalActionKind.ReturnSoul && SelectedHellReturnSoulAction() is not null);
    }
}
