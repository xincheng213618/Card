using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record ReserveCardViewModel(CardViewModel Card, bool WasReserve, bool IsCommitted);
public sealed record PrivatePileBackViewModel(string Label);

public sealed partial class MainViewModel
{
    private GameEngine? _reserveGame;
    private long _reserveRevision = -1;
    private bool _isReserveDialogOpen;
    private bool _isReserveExchange;
    private bool _submittingReserveExchange;
    private string _reserveMode = string.Empty;
    private int _reserveRequired;
    private string _reserveOwnerName = string.Empty;
    private string _reservePileName = "七星";
    private bool _reserveForeignView;
    private bool _reserveTurnHandHold;
    private readonly List<ReserveCardViewModel> _reserveCards = [];
    private readonly HashSet<int> _reserveSelectedHand = [];
    private readonly HashSet<int> _reserveSelectedStars = [];
    private readonly HashSet<int> _reserveCommittedHand = [];
    private readonly HashSet<int> _reserveCommittedStars = [];

    public ObservableCollection<ReserveCardViewModel> ReserveDialogStars { get; } = [];
    public ObservableCollection<ReserveCardViewModel> ReserveDialogHand { get; } = [];
    public ObservableCollection<PrivatePileBackViewModel> ReserveDialogBacks { get; } = [];
    public bool IsReserveDialogOpen { get => _isReserveDialogOpen; private set => SetProperty(ref _isReserveDialogOpen, value); }
    public bool IsReserveExchange { get => _isReserveExchange; private set => SetProperty(ref _isReserveExchange, value); }
    public string ReserveDialogTitle => $"{_reserveOwnerName} · {_reservePileName}";
    public string ReservePileLabel => string.Join("\n", (_reservePileName + "牌").ToCharArray());
    public string ReserveDialogHint => IsReserveExchange
        ? "点击或拖动牌，在手牌与“星”之间移动；两边等量后确定。"
        : _reserveForeignView ? "暗置的牌 · 仅显示牌背与数量"
        : _reserveTurnHandHold ? "暂存的手牌 · 回合结束时返还 · 仅你可见"
        : "武将牌上的“星” · 仅你可见";
    public string ReserveExchangeSummary => $"手牌 → 星 {_reserveSelectedHand.Count} 张    星 → 手牌 {_reserveSelectedStars.Count} 张";
    public string ReserveCancelText => !IsReserveExchange ? "关闭" : _reserveMode == "offered" ? "取消" : "重选";
    public bool CanConfirmReserveExchange => IsReserveDialogOpen && IsReserveExchange && IsCurrentReserveView &&
        _reserveSelectedHand.Count > 0 && _reserveSelectedHand.Count == _reserveSelectedStars.Count &&
        (_reserveMode != "exchange-stars" || _reserveSelectedHand.Count == _reserveRequired);
    private bool IsCurrentReserveView => ReferenceEquals(_reserveGame, _game) && _reserveRevision == _snapshot.Revision &&
        _reserveRevision == _game.Revision;

    public ICommand OpenPrivateReserveCommand { get; private set; } = null!;
    public ICommand OpenTurnHandHoldCommand { get; private set; } = null!;
    public ICommand MoveReserveCardCommand { get; private set; } = null!;
    public ICommand ConfirmReserveExchangeCommand { get; private set; } = null!;
    public ICommand CloseReserveDialogCommand { get; private set; } = null!;

    private void InitializePrivateReserve()
    {
        OpenPrivateReserveCommand = new RelayCommand<SeatViewModel>(OpenPrivateReserve, seat =>
            IsCurrentPortraitPileOwner(seat) && seat.PrivateReserveCount > 0);
        OpenTurnHandHoldCommand = new RelayCommand<SeatViewModel>(OpenTurnHandHold, seat =>
            IsCurrentPortraitPileOwner(seat) && seat.TurnHandHoldCount > 0);
        MoveReserveCardCommand = new RelayCommand<ReserveCardViewModel>(MoveReserveCard, CanMoveReserveCard);
        ConfirmReserveExchangeCommand = new RelayCommand(ConfirmReserveExchange, () => CanConfirmReserveExchange);
        CloseReserveDialogCommand = new RelayCommand(CloseReserveDialog);
    }

    private bool IsCurrentPortraitPileOwner(SeatViewModel seat) => !IsReserveExchange &&
        _snapshot.Revision == _game.Revision && Seats.Any(current => ReferenceEquals(current, seat));

    private static bool IsReserveExchangePrompt(PendingDecision? prompt, int viewerSeat) =>
        prompt is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true, SkillPrompt.SkillId: "ol:qixing" } &&
        prompt.PlayerSeat == viewerSeat && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") is "activate" or "private-reserve-choice");

    private void SyncPrivateReserveDialog()
    {
        if (_submittingReserveExchange || ReferenceEquals(_reserveGame, _game) && _reserveRevision == _snapshot.Revision) return;
        IsReserveDialogOpen = false;
        IsReserveExchange = false;
        _reserveGame = _game;
        _reserveRevision = _snapshot.Revision;
        ClearReserveCards();
        if (!IsReserveExchangePrompt(_snapshot.PendingDecision, _snapshot.HumanSeat)) return;
        var prompt = _snapshot.PendingDecision!;
        var parameters = prompt.Choices[0].Parameters;
        _reserveMode = parameters.GetValueOrDefault("reserve-mode") ?? "offered";
        _reserveRequired = int.TryParse(parameters.GetValueOrDefault("reserve-required"), CultureInfo.InvariantCulture, out var required) ? required : 0;
        ReadIds("reserve-selected-hand", _reserveCommittedHand);
        ReadIds("reserve-selected-stars", _reserveCommittedStars);
        _reserveSelectedHand.UnionWith(_reserveCommittedHand);
        _reserveSelectedStars.UnionWith(_reserveCommittedStars);
        IsReserveExchange = true;
        var owner = _snapshot.Players.Single(player => player.Seat == _snapshot.HumanSeat);
        SetReserveDialogOwner(owner, "七星");
        PopulateReserveCards(owner);
        IsReserveDialogOpen = true;
        RefreshReservePresentation();

        void ReadIds(string key, HashSet<int> target)
        {
            foreach (var value in (parameters.GetValueOrDefault(key) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var id)) target.Add(id);
        }
    }

    private void ClearReserveCards()
    {
        _reserveCards.Clear();
        _reserveSelectedHand.Clear();
        _reserveSelectedStars.Clear();
        _reserveCommittedHand.Clear();
        _reserveCommittedStars.Clear();
        ReserveDialogStars.Clear();
        ReserveDialogHand.Clear();
        ReserveDialogBacks.Clear();
    }

    private void OpenPrivateReserve(SeatViewModel seat)
    {
        if (!OpenPrivateReserveCommand.CanExecute(seat)) return;
        var owner = _snapshot.Players.Single(player => player.Seat == seat.Seat);
        if (owner.PrivateReserveCount <= 0) return;
        ClearReserveCards();
        _reserveGame = _game;
        _reserveRevision = _snapshot.Revision;
        IsReserveExchange = false;
        SetReserveDialogOwner(owner, "七星");
        if (!_reserveForeignView) PopulateReserveCards(owner);
        AddReserveBacks(owner.PrivateReserveCount - _reserveCards.Count);
        IsReserveDialogOpen = true;
        RefreshReservePresentation();
    }

    private void OpenTurnHandHold(SeatViewModel seat)
    {
        if (!OpenTurnHandHoldCommand.CanExecute(seat)) return;
        var owner = _snapshot.Players.Single(player => player.Seat == seat.Seat);
        var holds = (owner.PrivateTurnHolds ?? []).Where(hold => hold.OwnerSeat == owner.Seat).ToArray();
        if (holds.Length == 0) return;
        ClearReserveCards();
        _reserveGame = _game;
        _reserveRevision = _snapshot.Revision;
        IsReserveExchange = false;
        SetReserveDialogOwner(owner, seat.TurnHandHoldName, turnHandHold: true);
        // The portrait viewer never inherits developer-view access to foreign faces.
        if (!_reserveForeignView)
            foreach (var card in holds.SelectMany(hold => hold.Cards ?? []))
                _reserveCards.Add(new(CreateViewedCard(card, privateView: true), true, false));
        AddReserveBacks(holds.Sum(hold => hold.Count) - _reserveCards.Count);
        IsReserveDialogOpen = true;
        RefreshReservePresentation();
    }

    private void SetReserveDialogOwner(PlayerSnapshot owner, string pileName, bool turnHandHold = false)
    {
        _reserveOwnerName = owner.GeneralName;
        _reservePileName = pileName;
        _reserveForeignView = owner.Seat != _snapshot.HumanSeat;
        _reserveTurnHandHold = turnHandHold;
    }

    private void AddReserveBacks(int count)
    {
        for (var index = 0; index < count; index++) ReserveDialogBacks.Add(new($"暗牌 {index + 1}"));
    }

    private void PopulateReserveCards(PlayerSnapshot owner)
    {
        foreach (var card in owner.PrivateReserveCards ?? [])
            _reserveCards.Add(new(CreateViewedCard(card, privateView: true), true, _reserveCommittedStars.Contains(card.Id)));
        if (IsReserveExchange)
            foreach (var card in owner.Hand)
                _reserveCards.Add(new(CreateViewedCard(card, privateView: true), false,
                    _reserveCommittedHand.Contains(card.Id) || _reserveMode == "exchange-stars"));
    }

    private bool CanMoveReserveCard(ReserveCardViewModel card) => IsReserveDialogOpen && IsReserveExchange &&
        IsCurrentReserveView && !card.IsCommitted && _reserveCards.Any(current => ReferenceEquals(current, card));

    private void MoveReserveCard(ReserveCardViewModel card)
    {
        if (!CanMoveReserveCard(card)) return;
        var selected = card.WasReserve ? _reserveSelectedStars : _reserveSelectedHand;
        if (!selected.Remove(card.Card.Id)) selected.Add(card.Card.Id);
        RefreshReservePresentation();
    }

    private void RefreshReservePresentation()
    {
        ReserveDialogStars.Clear();
        ReserveDialogHand.Clear();
        foreach (var card in _reserveCards)
        {
            var moved = (card.WasReserve ? _reserveSelectedStars : _reserveSelectedHand).Contains(card.Card.Id);
            card.Card.IsSelected = moved;
            (card.WasReserve != moved ? ReserveDialogStars : ReserveDialogHand).Add(card);
        }
        foreach (var property in new[] { nameof(ReserveDialogTitle), nameof(ReservePileLabel), nameof(ReserveDialogHint), nameof(ReserveExchangeSummary),
                     nameof(ReserveCancelText), nameof(CanConfirmReserveExchange) }) RaisePropertyChanged(property);
        (MoveReserveCardCommand as RelayCommand<ReserveCardViewModel>)?.NotifyCanExecuteChanged();
        (OpenPrivateReserveCommand as RelayCommand<SeatViewModel>)?.NotifyCanExecuteChanged();
        (OpenTurnHandHoldCommand as RelayCommand<SeatViewModel>)?.NotifyCanExecuteChanged();
        (ConfirmReserveExchangeCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void CloseReserveDialog()
    {
        if (!IsReserveDialogOpen) return;
        if (IsReserveExchange)
        {
            if (!IsCurrentReserveView) return;
            if (_reserveMode == "offered")
            {
                var skip = _snapshot.PendingDecision?.Choices.SingleOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
                if (skip is not null) SubmitPublishedPromptChoice(skip, IsSkillSelectionPending, "七星取消未执行");
                return;
            }
            _reserveSelectedHand.Clear();
            _reserveSelectedStars.Clear();
            _reserveSelectedHand.UnionWith(_reserveCommittedHand);
            _reserveSelectedStars.UnionWith(_reserveCommittedStars);
            RefreshReservePresentation();
            return;
        }
        IsReserveDialogOpen = false;
        ClearReserveCards();
    }

    private void ConfirmReserveExchange()
    {
        if (!CanConfirmReserveExchange) return;
        var hand = _reserveSelectedHand.Except(_reserveCommittedHand).ToArray();
        var stars = _reserveSelectedStars.Except(_reserveCommittedStars).ToArray();
        ExecuteSafely(() =>
        {
            _submittingReserveExchange = true;
            try
            {
                if (_reserveMode == "offered" && !Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate")) return;
                foreach (var id in hand)
                    if (!Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "private-reserve-choice" && choice.Cards.SequenceEqual([id]))) return;
                if (_game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("finish") == "true") == true &&
                    !Answer(choice => choice.Parameters.GetValueOrDefault("finish") == "true")) return;
                foreach (var id in stars)
                    if (!Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "private-reserve-choice" && choice.Cards.SequenceEqual([id]))) return;
            }
            finally
            {
                _submittingReserveExchange = false;
                _reserveRevision = -1;
                RefreshCurrentView();
            }
        });

        bool Answer(Func<PromptChoice, bool> predicate)
        {
            var view = _game.CreateSnapshot(_snapshot.HumanSeat);
            if (!IsReserveExchangePrompt(view.PendingDecision, view.HumanSeat)) return false;
            var prompt = view.PendingDecision!;
            var choice = prompt.Choices.SingleOrDefault(predicate);
            if (choice is null) return false;
            return SubmitCommand(new AnswerPromptCommand(view.HumanSeat, prompt.PromptId, choice.Id, view.Revision)).Accepted;
        }
    }
}
