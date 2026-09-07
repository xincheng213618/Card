using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows.Input;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private GameEngine _game = null!;
    private GameSnapshot _snapshot = null!;
    private int? _selectedCardId;
    private int? _selectedTargetSeat;
    private bool _isDeveloperView;
    private string _roundText = "回合 0";
    private string _phaseText = "尚未开始";
    private string _currentPlayerText = string.Empty;
    private string _centerTitle = "准备开始";
    private string _centerMessage = string.Empty;
    private string _deckText = "牌堆 0";
    private string _discardText = "弃牌 0";
    private string _humanSummary = string.Empty;
    private string _selectedCardText = "未选择手牌";
    private string _promptText = string.Empty;
    private string _responseButtonText = "打出闪";
    private string _gameOverText = string.Empty;
    private bool _hasGameOver;
    private bool _canPlaySelected;
    private bool _canPlaySelectedAsSlash;
    private bool _canEndTurn;
    private bool _canRespondDodge;
    private bool _canDeclineResponse;
    private bool _canStepAi;
    private bool _isGeneralSelectionPending;
    private bool _isDyingSelectionPending;
    private bool _isHarvestSelectionPending;
    private bool _isFireAttackSelectionPending;
    private bool _isResponseSelectionPending;
    private bool _isSkillSelectionPending;
    private bool _hasPublicRevealedCards;
    private string _publicRevealTitle = "公开牌";

    public MainViewModel()
    {
        NewGameCommand = new RelayCommand(NewGame);
        StepAiCommand = new RelayCommand(StepAi, () => CanStepAi);
        RunToHumanCommand = new RelayCommand(RunToHuman, () => CanStepAi);
        SelectCardCommand = new RelayCommand<CardViewModel>(SelectCard);
        SelectTargetCommand = new RelayCommand<SeatViewModel>(SelectTarget);
        SelectGeneralChoiceCommand = new RelayCommand<GeneralChoiceViewModel>(SelectGeneral);
        SelectDyingChoiceCommand = new RelayCommand<PromptChoice>(SelectDyingChoice);
        SelectHarvestChoiceCommand = new RelayCommand<PromptChoice>(SelectHarvestChoice);
        SelectFireAttackChoiceCommand = new RelayCommand<PromptChoice>(SelectFireAttackChoice);
        SelectResponseChoiceCommand = new RelayCommand<PromptChoice>(SelectResponseChoice);
        SelectSkillChoiceCommand = new RelayCommand<PromptChoice>(SelectSkillChoice);
        PlaySelectedCardCommand = new RelayCommand(PlaySelectedCard, () => CanPlaySelected);
        PlaySelectedAsSlashCommand = new RelayCommand(PlaySelectedAsSlash, () => CanPlaySelectedAsSlash);
        EndTurnCommand = new RelayCommand(EndTurn, () => CanEndTurn);
        RespondDodgeCommand = new RelayCommand(() => RespondToSlash(true), () => CanRespondDodge);
        DeclineResponseCommand = new RelayCommand(() => RespondToSlash(false), () => CanDeclineResponse);

        NewGame();
    }

    public ObservableCollection<SeatViewModel> Seats { get; } = [];
    public ObservableCollection<CardViewModel> Hand { get; } = [];
    public ObservableCollection<string> GameLog { get; } = [];
    public ObservableCollection<string> AiThoughts { get; } = [];
    public ObservableCollection<string> EventStack { get; } = [];
    public ObservableCollection<GeneralChoiceViewModel> GeneralChoices { get; } = [];
    public ObservableCollection<PromptChoice> DyingChoices { get; } = [];
    public ObservableCollection<PromptChoice> HarvestChoices { get; } = [];
    public ObservableCollection<PromptChoice> FireAttackChoices { get; } = [];
    public ObservableCollection<PromptChoice> ResponseChoices { get; } = [];
    public ObservableCollection<PromptChoice> SkillChoices { get; } = [];
    public ObservableCollection<CardViewModel> PublicRevealedCards { get; } = [];

    public ICommand NewGameCommand { get; }
    public ICommand StepAiCommand { get; }
    public ICommand RunToHumanCommand { get; }
    public ICommand SelectCardCommand { get; }
    public ICommand SelectTargetCommand { get; }
    public ICommand SelectGeneralChoiceCommand { get; }
    public ICommand SelectDyingChoiceCommand { get; }
    public ICommand SelectHarvestChoiceCommand { get; }
    public ICommand SelectFireAttackChoiceCommand { get; }
    public ICommand SelectResponseChoiceCommand { get; }
    public ICommand SelectSkillChoiceCommand { get; }
    public ICommand PlaySelectedCardCommand { get; }
    public ICommand PlaySelectedAsSlashCommand { get; }
    public ICommand EndTurnCommand { get; }
    public ICommand RespondDodgeCommand { get; }
    public ICommand DeclineResponseCommand { get; }

    public bool IsDeveloperView
    {
        get => _isDeveloperView;
        set
        {
            if (SetProperty(ref _isDeveloperView, value) && _game is not null)
            {
                Refresh(_game.CreateSnapshot(_game.State.HumanSeat, value));
            }
        }
    }

    public string RoundText { get => _roundText; private set => SetProperty(ref _roundText, value); }
    public string PhaseText { get => _phaseText; private set => SetProperty(ref _phaseText, value); }
    public string CurrentPlayerText { get => _currentPlayerText; private set => SetProperty(ref _currentPlayerText, value); }
    public string CenterTitle { get => _centerTitle; private set => SetProperty(ref _centerTitle, value); }
    public string CenterMessage { get => _centerMessage; private set => SetProperty(ref _centerMessage, value); }
    public string DeckText { get => _deckText; private set => SetProperty(ref _deckText, value); }
    public string DiscardText { get => _discardText; private set => SetProperty(ref _discardText, value); }
    public string HumanSummary { get => _humanSummary; private set => SetProperty(ref _humanSummary, value); }
    public string SelectedCardText { get => _selectedCardText; private set => SetProperty(ref _selectedCardText, value); }
    public string PromptText { get => _promptText; private set => SetProperty(ref _promptText, value); }
    public string ResponseButtonText { get => _responseButtonText; private set => SetProperty(ref _responseButtonText, value); }
    public string GameOverText { get => _gameOverText; private set => SetProperty(ref _gameOverText, value); }
    public bool HasGameOver { get => _hasGameOver; private set => SetProperty(ref _hasGameOver, value); }
    public bool IsResponseSelectionPending
    {
        get => _isResponseSelectionPending;
        private set => SetProperty(ref _isResponseSelectionPending, value);
    }

    public bool CanPlaySelected
    {
        get => _canPlaySelected;
        private set
        {
            if (SetProperty(ref _canPlaySelected, value))
            {
                ((RelayCommand)PlaySelectedCardCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanPlaySelectedAsSlash
    {
        get => _canPlaySelectedAsSlash;
        private set
        {
            if (SetProperty(ref _canPlaySelectedAsSlash, value))
            {
                ((RelayCommand)PlaySelectedAsSlashCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanEndTurn
    {
        get => _canEndTurn;
        private set
        {
            if (SetProperty(ref _canEndTurn, value))
            {
                ((RelayCommand)EndTurnCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanRespondDodge
    {
        get => _canRespondDodge;
        private set
        {
            if (SetProperty(ref _canRespondDodge, value))
            {
                ((RelayCommand)RespondDodgeCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanDeclineResponse
    {
        get => _canDeclineResponse;
        private set
        {
            if (SetProperty(ref _canDeclineResponse, value))
            {
                ((RelayCommand)DeclineResponseCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanStepAi
    {
        get => _canStepAi;
        private set
        {
            if (SetProperty(ref _canStepAi, value))
            {
                ((RelayCommand)StepAiCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RunToHumanCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsGeneralSelectionPending
    {
        get => _isGeneralSelectionPending;
        private set => SetProperty(ref _isGeneralSelectionPending, value);
    }

    public bool IsDyingSelectionPending
    {
        get => _isDyingSelectionPending;
        private set => SetProperty(ref _isDyingSelectionPending, value);
    }

    public bool IsHarvestSelectionPending
    {
        get => _isHarvestSelectionPending;
        private set => SetProperty(ref _isHarvestSelectionPending, value);
    }

    public bool IsFireAttackSelectionPending
    {
        get => _isFireAttackSelectionPending;
        private set => SetProperty(ref _isFireAttackSelectionPending, value);
    }

    public bool IsSkillSelectionPending
    {
        get => _isSkillSelectionPending;
        private set => SetProperty(ref _isSkillSelectionPending, value);
    }

    public bool HasPublicRevealedCards
    {
        get => _hasPublicRevealedCards;
        private set => SetProperty(ref _hasPublicRevealedCards, value);
    }

    public string PublicRevealTitle
    {
        get => _publicRevealTitle;
        private set => SetProperty(ref _publicRevealTitle, value);
    }

    private void NewGame()
    {
        _selectedCardId = null;
        _selectedTargetSeat = null;
        SelectedCardText = "未选择手牌";
        GameLog.Clear();
        AiThoughts.Clear();
        EventStack.Clear();

        // A predictable seed would let a modified client reconstruct every hidden
        // role and card. Tests inject fixed seeds; ordinary local games use entropy.
        var seed = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000);
        _game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 400,
                UseInteractiveSetup = true
            },
            StandardContentRegistry.Create());
        _game.LogAdded += OnLogAdded;
        _game.AiThoughtAdded += OnAiThoughtAdded;
        _game.AiGeneralThoughtAdded += OnAiGeneralThoughtAdded;
        _game.StateChanged += OnStateChanged;

        ExecuteSafely(() =>
        {
            var result = _game.Start();
            Refresh(result.State);
        });
    }

    private void OnStateChanged(GameSnapshot _) => RefreshCurrentView();

    private void OnLogAdded(GameLogEntry entry)
    {
        GameLog.Insert(0, $"#{entry.Sequence:000} · T{entry.TurnNumber:000}  {entry.Message}");
        while (GameLog.Count > 400)
        {
            GameLog.RemoveAt(GameLog.Count - 1);
        }
    }

    private void OnAiThoughtAdded(AiThoughtRecord thought)
    {
        if (IsDeveloperView)
        {
            RebuildAiThoughts();
        }
    }

    private void OnAiGeneralThoughtAdded(AiGeneralThought thought)
    {
        if (IsDeveloperView)
        {
            RebuildAiThoughts();
        }
    }

    private void RefreshCurrentView()
    {
        var snapshot = _game.CreateSnapshot(_game.State.HumanSeat, IsDeveloperView);
        Refresh(snapshot);
    }

    private void Refresh(GameSnapshot snapshot)
    {
        _snapshot = IsDeveloperView
            ? _game.CreateSnapshot(snapshot.HumanSeat, revealAll: true)
            : snapshot;

        GeneralChoices.Clear();
        DyingChoices.Clear();
        HarvestChoices.Clear();
        FireAttackChoices.Clear();
        ResponseChoices.Clear();
        SkillChoices.Clear();
        PublicRevealedCards.Clear();
        foreach (var card in _snapshot.PublicRevealedCards)
        {
            var definition = CardCatalog.Get(card.Kind);
            PublicRevealedCards.Add(new CardViewModel
            {
                Id = card.Id,
                Name = card.DisplayName,
                KindLabel = definition.CategoryName,
                SuitGlyph = GetSuitGlyph(card.Suit),
                Rank = card.RankText,
                Description = definition.Description,
                IsPlayable = false,
                IsSelected = false
            });
        }
        HasPublicRevealedCards = PublicRevealedCards.Count > 0;
        if (_snapshot.PendingDecision is { Kind: DecisionKind.SelectGeneral } pending)
        {
            foreach (var choice in pending.Choices.Where(choice => choice.ContentIds.Count == 1))
            {
                GeneralChoices.Add(new GeneralChoiceViewModel
                {
                    GeneralId = choice.ContentIds[0],
                    ChoiceId = choice.Id,
                    Text = choice.Description
                });
            }
        }
        IsGeneralSelectionPending = _snapshot.PendingDecision?.Kind == DecisionKind.SelectGeneral;
        if (_snapshot.PendingDecision is { Kind: DecisionKind.RescueDying } dyingPrompt)
        {
            foreach (var choice in dyingPrompt.Choices)
            {
                DyingChoices.Add(choice);
            }
        }
        IsDyingSelectionPending = _snapshot.PendingDecision?.Kind == DecisionKind.RescueDying;
        if (_snapshot.PendingDecision is { Kind: DecisionKind.SelectHarvestCard } harvestPrompt)
        {
            foreach (var choice in harvestPrompt.Choices)
            {
                HarvestChoices.Add(choice);
            }
        }
        IsHarvestSelectionPending = _snapshot.PendingDecision?.Kind == DecisionKind.SelectHarvestCard;
        if (_snapshot.PendingDecision is
            { Kind: DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard } fireAttackPrompt)
        {
            foreach (var choice in fireAttackPrompt.Choices)
            {
                FireAttackChoices.Add(choice);
            }
        }
        IsFireAttackSelectionPending = _snapshot.PendingDecision?.Kind is
            DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard;
        PublicRevealTitle = _snapshot.PendingDecision?.Kind == DecisionKind.FireAttackDiscard
            ? "火攻 · 公开牌"
            : "五谷丰登 · 公开牌";
        if (_snapshot.PendingDecision is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } responsePrompt)
        {
            foreach (var choice in responsePrompt.Choices)
            {
                ResponseChoices.Add(choice);
            }
        }
        IsResponseSelectionPending = _snapshot.PendingDecision?.Kind is
            DecisionKind.RespondDodge or DecisionKind.RespondSlash;
        if (_snapshot.PendingDecision is { Kind: DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu } skillPrompt)
        {
            foreach (var choice in skillPrompt.Choices)
            {
                SkillChoices.Add(choice);
            }
        }
        IsSkillSelectionPending = _snapshot.PendingDecision?.Kind is
            DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu;

        var legalActions = _game.GetHumanLegalActions();
        var playableCardIds = legalActions
            .Where(action => action.CardId.HasValue)
            .Select(action => action.CardId!.Value)
            .ToHashSet();

        if (_selectedCardId is { } selectedId && !playableCardIds.Contains(selectedId))
        {
            _selectedCardId = null;
            _selectedTargetSeat = null;
            SelectedCardText = "未选择手牌";
        }

        var humanSeat = _snapshot.HumanSeat;
        var humanAttackRange = humanSeat >= 0 ? _game.GetAttackRange(humanSeat) : 0;
        Seats.Clear();
        foreach (var player in _snapshot.Players.OrderBy(player => player.Seat))
        {
            Seats.Add(new SeatViewModel
            {
                Seat = player.Seat,
                Name = $"{player.GeneralName} · {(player.IsHuman ? "你" : $"AI {player.Seat + 1}")}",
                Kingdom = GetKingdom(player.GeneralId),
                RoleLabel = player.Role is { } role ? GetRoleName(role) : "?",
                HpText = $"体力 {player.Hp}/{player.MaxHp}",
                HandText = player.HasAlcoholEffect
                    ? $"手牌 {player.HandCount} · 酒效生效"
                    : $"手牌 {player.HandCount}",
                EquipmentText = player.Equipment.Count == 0
                    ? "装备 —"
                    : $"装备 {string.Join(" · ", player.Equipment.Select(card => card.DisplayName))}",
                DistanceText = humanSeat < 0
                    ? string.Empty
                    : player.Seat == humanSeat
                        ? $"攻击范围 {humanAttackRange}"
                        : $"距你 {_game.GetCombatDistance(humanSeat, player.Seat)}",
                SkillText = $"{player.SkillName}：{player.SkillDescription}",
                IsAlive = player.IsAlive,
                IsCurrent = player.Seat == _snapshot.CurrentSeat && _snapshot.Status != EngineStatus.Completed,
                IsHuman = player.IsHuman,
                IsLegalTarget = false,
                IsSelectedTarget = player.Seat == _selectedTargetSeat
            });
        }

        Hand.Clear();
        var human = _snapshot.Players.SingleOrDefault(player => player.IsHuman);
        if (human is not null)
        {
            foreach (var card in human.Hand)
            {
                Hand.Add(new CardViewModel
                {
                    Id = card.Id,
                    Name = card.DisplayName,
                    KindLabel = CardCatalog.Get(card.Kind).CategoryName,
                    SuitGlyph = GetSuitGlyph(card.Suit),
                    Rank = card.RankText,
                    Description = CardCatalog.Get(card.Kind).Description,
                    IsPlayable = playableCardIds.Contains(card.Id),
                    IsSelected = card.Id == _selectedCardId
                });
            }

            HumanSummary = $"{GetRoleName(human.Role ?? Role.Lord)} · {human.GeneralName} · {human.Hp}/{human.MaxHp} 体力" +
                (human.HasAlcoholEffect ? " · 酒效待下一张杀" : string.Empty);
        }
        else
        {
            HumanSummary = "全 AI 演示";
        }

        RoundText = $"回合 {_snapshot.TurnNumber}";
        PhaseText = GetPhaseName(_snapshot.Phase);
        var current = _snapshot.Players.FirstOrDefault(player => player.Seat == _snapshot.CurrentSeat);
        CurrentPlayerText = current is null ? string.Empty : $"当前：{current.GeneralName}";
        CenterTitle = _snapshot.Status == EngineStatus.Completed
            ? "对局结束"
            : current?.GeneralName ?? "准备开始";
        CenterMessage = _snapshot.PendingDecision?.Prompt
                        ?? GameLog.FirstOrDefault()
                        ?? "等待规则引擎推进。";
        DeckText = $"摸牌堆 {_snapshot.DrawPileCount}";
        DiscardText = $"弃牌堆 {_snapshot.DiscardPileCount}";
        PromptText = GetPrompt(_snapshot);

        HasGameOver = _snapshot.Status == EngineStatus.Completed;
        GameOverText = HasGameOver ? $"{GetWinnerName(_snapshot.Winner)}获胜" : string.Empty;
        CanEndTurn = _snapshot.PendingDecision?.Kind == DecisionKind.PlayCard;
        CanRespondDodge = _snapshot.PendingDecision is
        {
            Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash,
            ValidCardIds.Count: > 0
        };
        CanDeclineResponse = _snapshot.PendingDecision?.Kind is
            DecisionKind.RespondDodge or DecisionKind.RespondSlash;
        ResponseButtonText = _snapshot.PendingDecision?.Kind == DecisionKind.RespondSlash
            ? "快速响应杀"
            : "快速响应闪";
        CanStepAi = _snapshot.Status == EngineStatus.Running && _snapshot.PendingDecision is null;

        RebuildAiThoughts();
        RebuildEventStack();
        RefreshTargetHighlights();
    }

    private void SelectCard(CardViewModel card)
    {
        if (_snapshot.PendingDecision?.Kind != DecisionKind.PlayCard || !card.IsPlayable)
        {
            return;
        }

        _selectedCardId = _selectedCardId == card.Id ? null : card.Id;
        _selectedTargetSeat = null;

        var matching = _game.GetHumanLegalActions()
            .Where(action => action.CardId == _selectedCardId)
            .ToArray();
        if (matching.Length == 1 && matching[0].Kind == LegalActionKind.Peach)
        {
            _selectedTargetSeat = matching[0].TargetSeat;
        }

        foreach (var item in Hand)
        {
            item.IsSelected = item.Id == _selectedCardId;
        }

        SelectedCardText = _selectedCardId is null
            ? "未选择手牌"
            : $"已选择：{card.Name}";
        RefreshTargetHighlights();
    }

    private void SelectTarget(SeatViewModel seat)
    {
        if (!seat.IsLegalTarget)
        {
            return;
        }

        _selectedTargetSeat = seat.Seat;
        RefreshTargetHighlights();
    }

    private void SelectGeneral(GeneralChoiceViewModel choice)
    {
        if (!IsGeneralSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.Submit(new CardGame.Core.SelectGeneralCommand(
                _snapshot.HumanSeat,
                choice.GeneralId,
                _game.Revision,
                pending.PromptId));
            if (!result.Accepted)
            {
                PromptText = $"选将未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectDyingChoice(PromptChoice choice)
    {
        if (!IsDyingSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.Submit(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _game.Revision));
            if (!result.Accepted)
            {
                PromptText = $"濒死响应未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectHarvestChoice(PromptChoice choice)
    {
        if (!IsHarvestSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.Submit(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _game.Revision));
            if (!result.Accepted)
            {
                PromptText = $"五谷丰登选牌未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectFireAttackChoice(PromptChoice choice)
    {
        if (!IsFireAttackSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.Submit(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _game.Revision));
            if (!result.Accepted)
            {
                PromptText = $"火攻选牌未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectResponseChoice(PromptChoice choice)
    {
        if (!IsResponseSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.Submit(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _game.Revision));
            if (!result.Accepted)
            {
                PromptText = $"响应未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectSkillChoice(PromptChoice choice)
    {
        if (!IsSkillSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.Submit(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _game.Revision));
            if (!result.Accepted)
            {
                PromptText = $"技能触发未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void RefreshTargetHighlights()
    {
        var legalActions = _snapshot.PendingDecision?.Kind == DecisionKind.PlayCard
            ? _game.GetHumanLegalActions()
            : [];
        var selectedActions = _selectedCardId is { } cardId
            ? legalActions.Where(action => action.CardId == cardId).ToArray()
            : [];
        var legalTargets = selectedActions
                .Where(action => action.TargetSeat.HasValue)
                .Select(action => action.TargetSeat!.Value)
                .ToHashSet();

        foreach (var seat in Seats)
        {
            seat.IsLegalTarget = legalTargets.Contains(seat.Seat);
            seat.IsSelectedTarget = seat.Seat == _selectedTargetSeat;
        }

        CanPlaySelected = selectedActions.Any(action => action.TargetSeat == _selectedTargetSeat);
        CanPlaySelectedAsSlash = selectedActions.Any(action =>
            action.TargetSeat == _selectedTargetSeat && action.PlayedCardKind == CardKind.Slash);
    }

    private void PlaySelectedCard()
    {
        if (_selectedCardId is not { } cardId)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.HumanPlay(cardId, _selectedTargetSeat);
            _selectedCardId = null;
            _selectedTargetSeat = null;
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private void PlaySelectedAsSlash()
    {
        if (_selectedCardId is not { } cardId)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = _game.HumanPlay(
                cardId,
                _selectedTargetSeat,
                playedCardKind: CardKind.Slash);
            _selectedCardId = null;
            _selectedTargetSeat = null;
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private void EndTurn()
    {
        ExecuteSafely(() =>
        {
            _selectedCardId = null;
            _selectedTargetSeat = null;
            SelectedCardText = "未选择手牌";
            var result = _game.HumanEndPlay(advanceToHumanBoundary: false);
            Refresh(result.State);
        });
    }

    private void RespondToSlash(bool useDodge)
    {
        ExecuteSafely(() =>
        {
            var result = _snapshot.PendingDecision?.Kind == DecisionKind.RespondSlash
                ? _game.HumanRespondSlash(useSlash: useDodge, advanceToHumanBoundary: false)
                : _game.HumanRespond(useDodge, advanceToHumanBoundary: false);
            Refresh(result.State);
        });
    }

    private void StepAi()
    {
        ExecuteSafely(() =>
        {
            var result = _game.AdvanceOneStep();
            Refresh(result.State);
        });
    }

    private void RunToHuman()
    {
        ExecuteSafely(() =>
        {
            var result = _game.Advance();
            Refresh(result.State);
        });
    }

    private void RebuildEventStack()
    {
        EventStack.Clear();
        EventStack.Add(IsDeveloperView || _snapshot.Status == EngineStatus.Completed
            ? $"Game(seed: {_game.Seed})"
            : "Game(seed: hidden)");
        if (_snapshot.TurnNumber > 0)
        {
            EventStack.Add($"  Turn({_snapshot.TurnNumber}, seat: {_snapshot.CurrentSeat + 1})");
            EventStack.Add($"    Phase.{_snapshot.Phase}");
        }

        if (_snapshot.PendingDecision is { } pending)
        {
            if (pending.Kind == DecisionKind.SelectHarvestCard)
            {
                EventStack.Add("      UseCard(FiveGrains)");
                EventStack.Add($"        SelectPublicCard({pending.ValidCardIds.Count})");
            }
            else if (pending.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard)
            {
                EventStack.Add("      UseCard(FireAttack)");
                EventStack.Add(pending.Kind == DecisionKind.FireAttackReveal
                    ? "        AskForPrivateReveal(FireAttack)"
                    : "        AskForSameSuitDiscard(FireAttack)");
            }
            else if (pending.Kind == DecisionKind.RescueDying)
            {
                EventStack.Add($"      Dying(target: seat {pending.TargetSeat.GetValueOrDefault() + 1})");
                EventStack.Add(pending.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "alcohol")
                    ? "        AskForResponse(Peach/Alcohol)"
                    : "        AskForResponse(Peach)");
            }
            else if (pending.Kind is DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu)
            {
                EventStack.Add($"      DamageSkill({pending.Kind}, target: seat {pending.TargetSeat.GetValueOrDefault() + 1})");
                EventStack.Add($"        AskForSkill({pending.Kind})");
            }
            else if (pending.IncomingCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            {
                EventStack.Add($"      UseCard({pending.IncomingCard})");
                EventStack.Add("        AskForResponse(Dodge)");
            }
            else if (pending.IncomingCard == CardKind.Duel)
            {
                EventStack.Add("      UseCard(Duel)");
                EventStack.Add("        AskForResponse(Slash)");
            }
            else if (pending.IncomingCard == CardKind.BarbarianAssault)
            {
                EventStack.Add("      UseCard(BarbarianAssault)");
                EventStack.Add("        AskForResponse(Slash)");
            }
            else if (pending.IncomingCard == CardKind.ArrowBarrage)
            {
                EventStack.Add("      UseCard(ArrowBarrage)");
                EventStack.Add("        AskForResponse(Dodge)");
            }
            else
            {
                EventStack.Add($"      Pending.{pending.Kind}");
            }
        }
        else if (_game.ResolutionStack.OfType<CardUseFrame>().LastOrDefault() is
        { CardKind: CardKind.FiveGrains } fiveGrains)
        {
            EventStack.Add("      UseCard(FiveGrains)");
            EventStack.Add($"        SelectPublicCard({fiveGrains.TargetIndex + 1}/{fiveGrains.TargetSeats.Count})");
            EventStack.Add($"        PublicOptions({_snapshot.PublicRevealedCards.Count})");
        }
        else if (_game.ResolutionStack.OfType<CardUseFrame>().LastOrDefault() is
        { CardKind: CardKind.PeachGarden } peachGarden)
        {
            EventStack.Add("      UseCard(PeachGarden)");
            EventStack.Add($"        ResolveTarget({peachGarden.TargetIndex + 1}/{peachGarden.TargetSeats.Count})");
        }

        if (_snapshot.Status == EngineStatus.Completed)
        {
            EventStack.Add($"  Completed({_snapshot.Winner})");
        }
    }

    private void RebuildAiThoughts()
    {
        AiThoughts.Clear();
        if (!IsDeveloperView)
        {
            AiThoughts.Add("为避免从评分反推出暗身份和暗牌，详细 AI 候选只在“开发者视图”开启后显示。");
            return;
        }

        foreach (var thought in _game.AiThoughts.Reverse().Take(200))
        {
            var actor = GetPlayerLabel(thought.ActorSeat);
            var candidates = thought.Candidates
                .GroupBy(candidate => candidate.Action.Description)
                .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
                .OrderByDescending(candidate => candidate.Score)
                .Take(3)
                .Select(candidate => $"  {candidate.Score,6:0.0}  {candidate.Action.Description} · {candidate.Reason}");
            AiThoughts.Add(
                $"T{thought.TurnNumber:000} {actor}\n{thought.Summary}\n{string.Join("\n", candidates)}");
        }

        foreach (var thought in _game.AiGeneralThoughts.Reverse().Take(100))
        {
            var actor = GetPlayerLabel(thought.ActorSeat);
            var candidates = thought.Candidates
                .Take(3)
                .Select(candidate =>
                    $"  {candidate.Score,6:0.0}  {candidate.GeneralName} · {candidate.SkillName} · {candidate.Reason}");
            AiThoughts.Add(
                $"选将 T{thought.TurnNumber:000} {actor}\n{thought.Summary}\n{string.Join("\n", candidates)}");
        }
    }

    private void ExecuteSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            PromptText = $"操作未执行：{exception.Message}";
        }
    }

    private string GetPlayerLabel(int seat) =>
        _game.State.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? $"座位 {seat + 1}";

    private static string GetPrompt(GameSnapshot snapshot)
    {
        if (snapshot.Status == EngineStatus.Completed)
        {
            return "本局已经结束，可以点击“新对局”再次开始。";
        }

        if (snapshot.PendingDecision is { } decision)
        {
            return decision.Prompt;
        }

        return snapshot.Status == EngineStatus.Running
            ? "AI 已暂停：点击“AI 单步”观察一次决策，或运行至你的下一次行动。"
            : "等待规则引擎。";
    }

    private static string GetRoleName(Role role) => role switch
    {
        Role.Lord => "主公",
        Role.Loyalist => "忠臣",
        Role.Rebel => "反贼",
        Role.Renegade => "内奸",
        _ => role.ToString()
    };

    private static string GetWinnerName(Winner winner) => winner switch
    {
        Winner.LordAndLoyalists => "主公与忠臣",
        Winner.Rebels => "反贼",
        Winner.Renegade => "内奸",
        Winner.Draw => "平局",
        _ => "尚未决出胜负"
    };

    private static string GetPhaseName(TurnPhase phase) => phase switch
    {
        TurnPhase.NotStarted => "准备阶段",
        TurnPhase.Draw => "摸牌阶段",
        TurnPhase.Play => "出牌阶段",
        TurnPhase.Discard => "弃牌阶段",
        TurnPhase.Finished => "回合结束",
        _ => phase.ToString()
    };

    private static string GetKingdom(string generalId) => generalId switch
    {
        "cao-cao" or "standard:cao-cao" or "standard:guo-jia" or "guo-jia" => "魏",
        "liu-bei" or "guan-yu" or "zhang-fei" or "zhuge-liang" or "zhao-yun"
            or "standard:liu-bei" or "standard:guan-yu" or "standard:zhang-fei"
            or "standard:zhuge-liang" or "standard:zhao-yun" => "蜀",
        "sun-quan" or "zhou-yu" or "standard:sun-quan" or "standard:zhou-yu" => "吴",
        _ => "群"
    };

    private static string GetSuitGlyph(Suit suit) => suit switch
    {
        Suit.Spade => "♠",
        Suit.Heart => "♥",
        Suit.Club => "♣",
        Suit.Diamond => "♦",
        _ => "?"
    };

}
