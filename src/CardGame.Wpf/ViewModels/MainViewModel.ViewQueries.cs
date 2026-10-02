using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GameEngine? _legalActionGame;
    private long _legalActionRevision = -1;
    private IReadOnlyList<LegalAction> _viewLegalActions = [];
    private IReadOnlyList<HandCardGuidance> _viewHandGuidance = [];

    // A card/target preview changes no rules. Share this expensive query across
    // bindings until the engine commits a new revision (or a new game is loaded).
    private IReadOnlyList<LegalAction> GetViewLegalActions()
    {
        if (!ReferenceEquals(_legalActionGame, _game) || _legalActionRevision != _game.Revision)
        {
            var view = _game.GetHumanActionView();
            _viewLegalActions = view.LegalActions;
            _viewHandGuidance = view.HandGuidance;
            _legalActionGame = _game;
            _legalActionRevision = view.Revision;
        }
        return _viewLegalActions;
    }

    private IReadOnlyList<HandCardGuidance> GetViewHandGuidance()
    {
        GetViewLegalActions();
        return _viewHandGuidance;
    }

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
                card.IsSelected = IsDiscardSelectionPending ? _discardCardIds.Contains(card.Id)
                    : IsActiveSkillCardSelectionPending ? _selectedActiveSkillCardIds.Contains(card.Id)
                    : card.Id == _selectedCardId;
            RebuildPublicTargetChoices();
            RebuildTargetCombinationChoices();
            RefreshTargetHighlights();
            RefreshDiscardPresentation();
        }
    }
}
