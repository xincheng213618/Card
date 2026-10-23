using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GameEngine? _legalActionGame;
    private HumanActionView? _viewAction;

    // A card/target preview changes no rules. Share this expensive query across
    // bindings until the engine commits a new revision (or a new game is loaded).
    private HumanActionView GetViewAction()
    {
        var view = _viewAction;
        if (!ReferenceEquals(_legalActionGame, _game) || view is null || view.Revision != _game.Revision)
        {
            view = _game.GetHumanActionView();
            _viewAction = view;
            _legalActionGame = _game;
        }
        return view;
    }

    private IReadOnlyList<LegalAction> GetViewLegalActions() => GetViewAction().LegalActions;

    private IReadOnlyList<HandCardGuidance> GetViewHandGuidance() => GetViewAction().HandGuidance;

    private bool IsHandCardSelected(int cardId) =>
        IsCardPaymentSelectionPending ? _paymentCardIds.Contains(cardId)
        : IsOwnedCardSelectionPending ? _selectedOwnedCardIds.Contains(cardId)
        : IsDiscardSelectionPending ? _discardCardIds.Contains(cardId)
        : IsActiveSkillCardSelectionPending ? _selectedActiveSkillCardIds.Contains(cardId)
        : cardId == _selectedCardId;

    private void RefreshCommandResult(GameSnapshot snapshot)
    {
        // The automatic dispatcher slice will present its last committed state.
        if (_automaticTableSnapshot?.Revision == snapshot.Revision) return;
        // Committed StateChanged has already projected this revision. Command
        // callers still handle rejected commands and engines without a new event.
        if (_snapshot.Revision != snapshot.Revision) Refresh(snapshot);
        else
        {
            // Some command handlers clear their local draft after Submit returns.
            // Reconcile those highlights without rebuilding the whole table.
            foreach (var card in Hand)
                card.IsSelected = IsHandCardSelected(card.Id);
            RebuildPublicTargetChoices();
            RebuildTargetCombinationChoices();
            RefreshTargetHighlights();
            RefreshDiscardPresentation();
        }
        // A terminal continuation may publish its final view only after Submit
        // returns. Capture from that presented result; repeated calls are inert.
        if (snapshot.Status == EngineStatus.Completed) CaptureCompletedHistory();
    }
}
