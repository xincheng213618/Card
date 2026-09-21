using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private PlayAdvice? _playAdvice;
    private GameEngine? _adviceGame;
    public PlayAdvice? CurrentPlayAdvice { get => _playAdvice; private set { if (SetProperty(ref _playAdvice, value)) RaisePropertyChanged(nameof(HasPlayAdvice)); } }
    public bool HasPlayAdvice => CurrentPlayAdvice is not null;
    public bool CanRequestPlayAdvice => !IsTutorialActive && !IsNewGameSetupOpen && !IsHistoryOpen &&
        _snapshot?.PendingDecision is { Kind: DecisionKind.PlayCard } prompt && prompt.PlayerSeat == _snapshot.HumanSeat && !HasGameOver;
    public ICommand RequestPlayAdviceCommand { get; private set; } = null!;

    private void RequestPlayAdvice()
    {
        if (!CanRequestPlayAdvice) return;
        // The developer toggle can expose a trusted snapshot elsewhere; advice always requests the ordinary view.
        var view = _game.CreateSnapshot(_snapshot.HumanSeat, revealAll: false);
        var recastIds = _game.Events.Reverse().TakeWhile(item => item.Payload is not TurnStartedEvent)
            .Select(item => item.Payload).OfType<CardRecastEvent>()
            .Where(item => item.ActorSeat == view.HumanSeat).Select(item => item.CardId);
        CurrentPlayAdvice = PlayAdvisor.Recommend(
            view,
            _game.GetHumanLegalActions(),
            recastIds,
            usesFormalRende: _game.UsesFormalRende);
        _adviceGame = _game;
        SelectedGuideSection = GuideSections[0];
        IsHelpOpen = true;
    }

    private void RefreshPlayAdvice()
    {
        if (!CanRequestPlayAdvice || !ReferenceEquals(_adviceGame, _game) || CurrentPlayAdvice?.Revision != _snapshot.Revision)
            CurrentPlayAdvice = null;
        RaisePropertyChanged(nameof(CanRequestPlayAdvice));
    }
}
