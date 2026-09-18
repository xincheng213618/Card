using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows.Input;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private GameEngine _game = null!;
    private readonly int? _seedOverride;
    private readonly ContentRegistry _contentRegistry;
    private GameSnapshot _snapshot = null!;
    private int? _selectedCardId;
    private readonly HashSet<int> _selectedActiveSkillCardIds = [];
    private readonly HashSet<int> _selectedActiveSkillTargetSeats = [];
    private PromptId? _activeSkillPromptId;
    private bool _isSelectingActiveSkillCards;
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
    private bool _isTargetCardSelectionPending;
    private bool _isFireAttackSelectionPending;
    private bool _isNullificationSelectionPending;
    private bool _isResponseSelectionPending;
    private bool _isSkillSelectionPending;
    private bool _hasPublicRevealedCards;
    private bool _hasPublicTargetChoices;
    private bool _hasTargetCombinationChoices;
    private string _publicRevealTitle = "公开牌";

    public MainViewModel(
        bool autoAdvance = true,
        int? seed = null,
        bool showSetup = true,
        IGameSaveStore? saveStore = null,
        bool useExpandedContent = false,
        IMatchHistoryStore? historyStore = null,
        IPlayerPreferencesStore? preferencesStore = null)
    {
        NewGameCommand = new RelayCommand(() => { if (!IsTutorialActive) OpenGameSetup(); }, () => !IsTutorialActive);
        StepAiCommand = new RelayCommand(StepAi, () => CanStepAi);
        RunToHumanCommand = new RelayCommand(RunToHuman, () => CanStepAi);
        SelectCardCommand = new RelayCommand<CardViewModel>(SelectCard);
        SelectTargetCommand = new RelayCommand<SeatViewModel>(SelectTarget);
        SelectGeneralChoiceCommand = new RelayCommand<GeneralChoiceViewModel>(SelectGeneral);
        SelectDyingChoiceCommand = new RelayCommand<PromptChoice>(SelectDyingChoice);
        SelectHarvestChoiceCommand = new RelayCommand<PromptChoice>(SelectHarvestChoice);
        SelectTargetCardChoiceCommand = new RelayCommand<PromptChoice>(SelectTargetCardChoice);
        SelectFireAttackChoiceCommand = new RelayCommand<PromptChoice>(SelectFireAttackChoice);
        SelectNullificationChoiceCommand = new RelayCommand<PromptChoice>(SelectNullificationChoice);
        SelectResponseChoiceCommand = new RelayCommand<PromptChoice>(SelectResponseChoice);
        SelectSkillChoiceCommand = new RelayCommand<PromptChoice>(SelectSkillChoice);
        SelectActiveSkillEquipmentChoiceCommand = new RelayCommand<PromptChoice>(SelectActiveSkillEquipmentChoice);
        SelectPublicTargetChoiceCommand = new RelayCommand<PromptChoice>(SelectPublicTargetChoice);
        SelectTargetCombinationChoiceCommand = new RelayCommand<PromptChoice>(SelectTargetCombinationChoice);
        PlaySelectedCardCommand = new RelayCommand(PlaySelectedCard, () => CanPlaySelected);
        PlaySelectedAsSlashCommand = new RelayCommand(PlaySelectedAsSlash, () => CanPlaySelectedAsSlash);
        UseActiveSkillCommand = new RelayCommand(UseActiveSkill, () => CanUseActiveSkill);
        EndTurnCommand = new RelayCommand(EndTurn, () => CanEndTurn);
        RespondDodgeCommand = new RelayCommand(() => RespondToSlash(true), () => CanRespondDodge);
        DeclineResponseCommand = new RelayCommand(() => RespondToSlash(false), () => CanDeclineResponse);

        _seedOverride = seed;
        _contentRegistry = useExpandedContent
            ? StandardContentRegistry.CreateWithClassicGeneralsAndTeamModesAndNationalWarAmbitious()
            : StandardContentRegistry.CreateWithTeamModes();
        TableModes = useExpandedContent
            ? [
                new TableModeOption(8, "八人经典身份", "1 主公 · 2 忠臣\n4 反贼 · 1 内奸", "identity:classic-8"),
                new TableModeOption(5, "五人经典身份", "1 主公 · 1 忠臣\n2 反贼 · 1 内奸", "identity:classic-5"),
                new TableModeOption(8, "八人技能演示", "旧演示武将池 · 用于机制验证", "identity:active-skills-8"),
                new TableModeOption(5, "五人技能演示", "旧演示武将池 · 用于机制验证", "identity:active-skills-5"),
                new TableModeOption(4, "2v2阵营", "青队 2 · 赤队 2\n公开阵营，协作对抗", "team:standard-2v2"),
                new TableModeOption(6, "国战 M3", "魏 3 · 蜀 2 · 野心家 1\n六人独立势力试验", "national:ambitious-6"),
                new TableModeOption(4, "国战 Lite", "魏蜀双将 · 暗置明置\n四人简化国战", "national:lite-4")
            ]
            : [
                new TableModeOption(8, "八人身份", "1 主公 · 2 忠臣 · 4 反贼 · 1 内奸", "identity:standard-8"),
                new TableModeOption(5, "五人身份", "1 主公 · 1 忠臣 · 2 反贼 · 1 内奸", "identity:standard-5"),
                new TableModeOption(4, "2v2公开阵营", "青队 2 · 赤队 2 · 阵营公开 · 击败另一队获胜", "team:standard-2v2")
            ];
        InitializePlayerGuide();
        InitializePresentation(autoAdvance);
        InitializeGameSetup();
        InitializePersistence(saveStore);
        InitializeHistory(historyStore);
        InitializeTutorial();
        InitializePreferences(preferencesStore);
        NewGame();
        IsNewGameSetupOpen = showSetup;
        _initializing = false;
    }

    public ObservableCollection<SeatViewModel> Seats { get; } = [];
    public ObservableCollection<CardViewModel> Hand { get; } = [];
    public ObservableCollection<string> GameLog { get; } = [];
    public ObservableCollection<string> AiThoughts { get; } = [];
    public ObservableCollection<string> EventStack { get; } = [];
    public ObservableCollection<GeneralChoiceViewModel> GeneralChoices { get; } = [];
    public ObservableCollection<PromptChoice> DyingChoices { get; } = [];
    public ObservableCollection<PromptChoice> HarvestChoices { get; } = [];
    public ObservableCollection<PromptChoice> TargetCardChoices { get; } = [];
    public ObservableCollection<PromptChoice> FireAttackChoices { get; } = [];
    public ObservableCollection<PromptChoice> NullificationChoices { get; } = [];
    public ObservableCollection<PromptChoice> ResponseChoices { get; } = [];
    public ObservableCollection<PromptChoice> SkillChoices { get; } = [];
    public ObservableCollection<PromptChoice> ActiveSkillEquipmentChoices { get; } = [];
    public ObservableCollection<PromptChoice> PublicTargetChoices { get; } = [];
    public ObservableCollection<PromptChoice> TargetCombinationChoices { get; } = [];
    public ObservableCollection<CardViewModel> PublicRevealedCards { get; } = [];

    public ICommand NewGameCommand { get; }
    public ICommand StepAiCommand { get; }
    public ICommand RunToHumanCommand { get; }
    public ICommand SelectCardCommand { get; }
    public ICommand SelectTargetCommand { get; }
    public ICommand SelectGeneralChoiceCommand { get; }
    public ICommand SelectDyingChoiceCommand { get; }
    public ICommand SelectHarvestChoiceCommand { get; }
    public ICommand SelectTargetCardChoiceCommand { get; }
    public ICommand SelectFireAttackChoiceCommand { get; }
    public ICommand SelectNullificationChoiceCommand { get; }
    public ICommand SelectResponseChoiceCommand { get; }
    public ICommand SelectSkillChoiceCommand { get; }
    public ICommand SelectActiveSkillEquipmentChoiceCommand { get; }
    public ICommand SelectPublicTargetChoiceCommand { get; }
    public ICommand SelectTargetCombinationChoiceCommand { get; }
    public ICommand PlaySelectedCardCommand { get; }
    public ICommand PlaySelectedAsSlashCommand { get; }
    public ICommand UseActiveSkillCommand { get; }
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

    public bool IsTargetCardSelectionPending
    {
        get => _isTargetCardSelectionPending;
        private set => SetProperty(ref _isTargetCardSelectionPending, value);
    }

    public bool IsFireAttackSelectionPending
    {
        get => _isFireAttackSelectionPending;
        private set => SetProperty(ref _isFireAttackSelectionPending, value);
    }

    public bool IsNullificationSelectionPending
    {
        get => _isNullificationSelectionPending;
        private set => SetProperty(ref _isNullificationSelectionPending, value);
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

    public bool HasPublicTargetChoices
    {
        get => _hasPublicTargetChoices;
        private set => SetProperty(ref _hasPublicTargetChoices, value);
    }

    public bool HasTargetCombinationChoices
    {
        get => _hasTargetCombinationChoices;
        private set => SetProperty(ref _hasTargetCombinationChoices, value);
    }

    public string PublicRevealTitle
    {
        get => _publicRevealTitle;
        private set => SetProperty(ref _publicRevealTitle, value);
    }

    private void NewGame()
    {
        if (!_initializing) ResetPersistenceMessage();
        // A predictable seed would let a modified client reconstruct every hidden
        // role and card. Tests inject fixed seeds; ordinary local games use entropy.
        var seed = _seedOverride ?? RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000);
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = IsSelectedTeamMode || IsNationalModeSelection ? null : SelectedStartingRole.Role,
                HumanTeamId = IsSelectedTeamMode ? SelectedStartingTeam.TeamId : null,
                PlayerCount = SelectedTableMode.PlayerCount,
                ModeId = SelectedTableMode.ModeId,
                MaxTurns = 400,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = ManualDiscardEnabled,
                AiPolicyVersion = 3,
                AdvanceAfterHumanCommands = false
            },
            _contentRegistry);
        ReplaceEngine(game);
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new StartGameCommand());
            Refresh(result.State);
        });
    }

    private bool IsSelectedTeamMode =>
        SelectedTableMode is not null &&
        _contentRegistry.Modes.TryGetValue(SelectedTableMode.ModeId, out var mode) &&
        mode.ModeKind == ContentModeKind.Team;

    private void ReplaceEngine(GameEngine game)
    {
        DetachEngine();
        _game = game;
        ResetPresentation();
        Hand.Clear();
        _selectedCardId = null;
        _selectedActiveSkillCardIds.Clear();
        _selectedActiveSkillTargetSeats.Clear();
        _activeSkillPromptId = null;
        _isSelectingActiveSkillCards = false;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();
        _discardCardIds.Clear();
        _discardPromptId = null;
        SelectedCardText = "未选择手牌";
        GameLog.Clear();
        AiThoughts.Clear();
        EventStack.Clear();
        _game.LogAdded += OnLogAdded;
        _game.AiThoughtAdded += OnAiThoughtAdded;
        _game.AiGeneralThoughtAdded += OnAiGeneralThoughtAdded;
        _game.StateChanged += OnStateChanged;
        RefreshCurrentView();
        foreach (var entry in _game.Log.TakeLast(400)) OnLogAdded(entry);
        RecordPublicPlays(_game.Events);
    }

    private void DetachEngine()
    {
        if (_game is null) return;
        _game.LogAdded -= OnLogAdded;
        _game.AiThoughtAdded -= OnAiThoughtAdded;
        _game.AiGeneralThoughtAdded -= OnAiGeneralThoughtAdded;
        _game.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameSnapshot _) => RefreshCurrentView();

    private void OnLogAdded(GameLogEntry entry)
    {
        RecordPublicActivity(entry);
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

    private void SyncActiveSkillSelection()
    {
        var pending = _snapshot?.PendingDecision;
        var promptId = pending?.Kind == DecisionKind.PlayCard ? pending.PromptId : (PromptId?)null;
        if (promptId != _activeSkillPromptId)
        {
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _isSelectingActiveSkillCards = false;
        }

        _activeSkillPromptId = promptId;
        var hasSelectionContract = pending is
        {
            Kind: DecisionKind.PlayCard,
            ActiveSkillKind: not null
        } && (pending.ActiveSkillMaxCardCount > 0 || pending.ActiveSkillMaxTargetCount > 0);
        if (!hasSelectionContract)
        {
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _isSelectingActiveSkillCards = false;
            return;
        }

        _selectedActiveSkillCardIds.IntersectWith(pending!.ActiveSkillValidCardIds ?? []);
        _selectedActiveSkillTargetSeats.IntersectWith(pending.ActiveSkillValidTargetSeats ?? []);
    }

    private void Refresh(GameSnapshot snapshot)
    {
        if (_snapshot?.PendingDecision?.PromptId != snapshot.PendingDecision?.PromptId)
            _selectedCardTargetSeats.Clear();
        if (_snapshot?.PendingDecision?.PromptId != snapshot.PendingDecision?.PromptId &&
            (SupportsHandResponse(_snapshot?.PendingDecision?.Kind) || SupportsHandResponse(snapshot.PendingDecision?.Kind)))
        {
            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
        }
        _snapshot = IsDeveloperView
            ? _game.CreateSnapshot(snapshot.HumanSeat, revealAll: true)
            : snapshot;
        SyncDiscardSelection();
        SyncActiveSkillSelection();
        RefreshDecisionContext();

        GeneralChoices.Clear();
        DyingChoices.Clear();
        HarvestChoices.Clear();
        TargetCardChoices.Clear();
        FireAttackChoices.Clear();
        NullificationChoices.Clear();
        ResponseChoices.Clear();
        SkillChoices.Clear();
        ActiveSkillEquipmentChoices.Clear();
        PublicTargetChoices.Clear();
        TargetCombinationChoices.Clear();
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
                Description = GetCardDescription(card.Kind),
                IsPlayable = false,
                IsPublicChoice = _snapshot.PendingDecision?.Kind == DecisionKind.SelectHarvestCard,
                IsSelected = false
            });
        }
        HasPublicRevealedCards = PublicRevealedCards.Count > 0;
        if (_snapshot.PendingDecision is { Kind: DecisionKind.SelectGeneral } pending)
        {
            foreach (var choice in pending.Choices.Where(choice => choice.ContentIds.Count == 1))
            {
                var general = _game.ContentRegistry!.Generals[choice.ContentIds[0]];
                var skills = general.SkillIds.Select(_game.ContentRegistry.GetSkill).ToArray();
                GeneralChoices.Add(new GeneralChoiceViewModel
                {
                    GeneralId = choice.ContentIds[0],
                    ChoiceId = choice.Id,
                    Text = choice.Description,
                    Name = general.Name,
                    Kingdom = FactionName(general.FactionId),
                    HealthText = !IsNationalSnapshot
                        ? choice.Parameters.TryGetValue("identity-max-hp", out var identityHp) ? $"体力上限 {identityHp}" : string.Empty
                        : choice.Parameters.TryGetValue("combined-max-hp", out var combinedHp) ? $"组合体力 {combinedHp}" :
                        choice.Parameters.TryGetValue("base-hp", out var baseHp) ? $"基础体力 {baseHp}" : "旧规则体力 4",
                    HealthDescription = choice.Parameters.GetValueOrDefault("health-preview", IsNationalSnapshot ? "此存档沿用固定 4 点体力上限。" : string.Empty),
                    SkillName = string.Join(" / ", skills.Select(skill => skill.Name)),
                    SkillDescription = string.Join("\n", skills.Select(skill =>
                        $"{skill.Name}：{GetVisibleSkillDescription(skill)}"))
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
        if (_snapshot.PendingDecision is { Kind: DecisionKind.SelectTargetCard } targetCardPrompt)
        {
            foreach (var choice in targetCardPrompt.Choices)
            {
                TargetCardChoices.Add(choice);
            }
        }
        IsTargetCardSelectionPending = _snapshot.PendingDecision?.Kind == DecisionKind.SelectTargetCard;
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
        if (_snapshot.PendingDecision is { Kind: DecisionKind.Nullification } nullificationPrompt)
        {
            foreach (var choice in nullificationPrompt.Choices)
            {
                NullificationChoices.Add(choice);
            }
        }
        IsNullificationSelectionPending = _snapshot.PendingDecision?.Kind == DecisionKind.Nullification;
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
        if (_snapshot.PendingDecision is
            {
                Kind: DecisionKind.Feedback or
                    DecisionKind.Yiji or
                    DecisionKind.Jieming or
                    DecisionKind.Yuanhu or
                    DecisionKind.Ganglie or
                    DecisionKind.GangliePunish or
                    DecisionKind.Guicai or
                    DecisionKind.Yingzi or
                    DecisionKind.Tiandu or
                    DecisionKind.Fanjian or
                    DecisionKind.Guanxing
            } skillPrompt)
        {
            foreach (var choice in skillPrompt.Choices)
            {
                SkillChoices.Add(choice);
            }
        }
        IsSkillSelectionPending = _snapshot.PendingDecision?.Kind is
            DecisionKind.Feedback or
            DecisionKind.Yiji or
            DecisionKind.Jieming or
            DecisionKind.Yuanhu or
            DecisionKind.Ganglie or
            DecisionKind.GangliePunish or
            DecisionKind.Guicai or
            DecisionKind.Yingzi or
            DecisionKind.Tiandu or
            DecisionKind.Fanjian or
            DecisionKind.Guanxing;

        var legalActions = _game.GetHumanLegalActions();
        var playableCardIds = legalActions
            .Where(action => action.CardId.HasValue)
            .Select(action => action.CardId!.Value)
            .ToHashSet();
        if (IsDiscardSelectionPending) playableCardIds.UnionWith(_snapshot.PendingDecision!.ValidCardIds);
        var activeSkillAction = legalActions.FirstOrDefault(action => action.Kind == LegalActionKind.UseSkill);
        var activeSkillCardIds = IsActiveSkillCardSelectionPending
            ? (_snapshot.PendingDecision!.ActiveSkillValidCardIds ?? []).ToHashSet()
            : [];

        if (_selectedCardId is { } selectedId && !playableCardIds.Contains(selectedId) && HandResponseChoice(selectedId) is null)
        {
            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
        }

        RebuildPublicTargetChoices();
        RebuildTargetCombinationChoices();

        var humanSeat = _snapshot.HumanSeat;
        var humanAttackRange = humanSeat >= 0 ? _game.GetAttackRange(humanSeat) : 0;
        var seatPlayers = IsNationalSnapshot && IsDeveloperView ? _game.CreateSnapshot(humanSeat, revealAll: false).Players : _snapshot.Players;
        Seats.Clear();
        foreach (var player in seatPlayers.OrderBy(player => player.Seat))
        {
            Seats.Add(new SeatViewModel
            {
                Seat = player.Seat,
                GeneralId = player.GeneralId,
                GeneralName = player.GeneralName,
                IsNationalSeat = IsNationalSnapshot,
                PrimaryGeneral = IsNationalSnapshot ? GeneralSlotViewModel.FromPlayer(player, false, _game.RulesVersion) : null,
                SecondaryGeneral = IsNationalSnapshot ? GeneralSlotViewModel.FromPlayer(player, true, _game.RulesVersion) : null,
                RelationshipLabel = IsNationalSnapshot ? NationalRelationship(player) : string.Empty,
                SecondaryGeneralText = IsNationalSnapshot ? $"副将：{player.SecondaryGeneralName ?? (player.IsHuman ? "待选" : "暗将")}" +
                    (player.IsHuman && player.SecondaryGeneralName is not null && !player.IsSecondaryGeneralPublic ? "·暗" : "") : string.Empty,
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                HandCount = player.HandCount,
                IsChained = player.IsChained,
                HasAlcoholEffect = player.HasAlcoholEffect,
                SkillName = IsNationalSnapshot
                    ? $"{player.SkillName} / {player.SecondarySkillName ?? "未知"}"
                    : string.Join(" / ", (player.Skills ?? [new(player.Skill, player.SkillName, player.SkillDescription)])
                        .Select(skill => skill.Name)),
                Name = $"{player.GeneralName} · {(player.IsHuman ? "你" : $"AI {player.Seat + 1}")}",
                Kingdom = IsNationalSnapshot ? FactionName(player.FactionId) : GetKingdom(player.GeneralId),
                RoleLabel = IsNationalSnapshot ? FactionName(player.FactionId) : player.Role is { } role ? GetRoleName(role) : "?",
                TeamId = player.TeamId,
                IsTeammate = IsNationalSnapshot ? NationalRelationship(player) == "同伴" : !player.IsHuman && player.TeamId is not null &&
                    player.TeamId == _snapshot.Players.SingleOrDefault(seat => seat.IsHuman)?.TeamId,
                HpText = $"体力 {player.Hp}/{player.MaxHp}",
                HandText = BuildHandText(player),
                EquipmentText = player.Equipment.Count == 0
                    ? "装备 —"
                    : $"装备 {string.Join(" · ", player.Equipment.Select(card => card.DisplayName))}",
                JudgmentText = player.Judgment.Count == 0
                    ? "判定区 —"
                    : $"判定区 {string.Join(" · ", player.Judgment.Select(card => card.DisplayName))}",
                DistanceText = humanSeat < 0
                    ? string.Empty
                    : player.Seat == humanSeat
                        ? $"攻击范围 {humanAttackRange}"
                        : $"距你 {_game.GetCombatDistance(humanSeat, player.Seat)}",
                SkillText = DisplaySkillText(player),
                IsAlive = player.IsAlive,
                IsCurrent = player.Seat == _snapshot.CurrentSeat && _snapshot.Status != EngineStatus.Completed,
                IsHuman = player.IsHuman,
                DecisionRoleLabel = CurrentDecisionContext?.TargetSeat == player.Seat ? CurrentDecisionContext.TargetLabel
                    : CurrentDecisionContext?.SourceSeat == player.Seat ? "效果来源" : string.Empty,
                IsLegalTarget = false,
                IsSelectedTarget = player.Seat == _selectedTargetSeat ||
                                   _selectedActiveSkillTargetSeats.Contains(player.Seat)
            });
        }

        var human = _snapshot.Players.SingleOrDefault(player => player.IsHuman);
        var handGuidance = _game.GetHumanHandGuidance().ToDictionary(item => item.CardId);
        if (human is not null)
        {
            RebuildActiveSkillEquipmentChoices(human, activeSkillCardIds);
            var ids = human.Hand.Select(card => card.Id).ToHashSet();
            for (var i = Hand.Count - 1; i >= 0; i--)
                if (!ids.Contains(Hand[i].Id)) Hand.RemoveAt(i);
            var existing = Hand.ToDictionary(card => card.Id);
            foreach (var card in human.Hand)
            {
                var activeSkillSelectable = activeSkillCardIds.Contains(card.Id);
                var responseChoice = HandResponseChoice(card.Id);
                var normallyPlayable = playableCardIds.Contains(card.Id) || responseChoice is not null;
                var normallySelectable = TutorialAllowsHandCard(card, normallyPlayable);
                var availability = IsActiveSkillSelectionPending
                    ? activeSkillSelectable ? $"可用于{activeSkillAction?.Description ?? "主动技能"}" : "本次技能不能选择这张牌；可按 Esc 退出技能选择。"
                    : TutorialHandAvailability(card, normallyPlayable, IsHandResponsePending
                        ? responseChoice is not null ? $"{responseChoice.Description}；选中后按 Enter 确认。" : HandResponseUnavailableHint(card.Id)
                        : handGuidance.GetValueOrDefault(card.Id)?.Message ?? string.Empty);
                var selectable = IsActiveSkillSelectionPending ? activeSkillSelectable : normallySelectable;
                if (existing.TryGetValue(card.Id, out var displayed))
                {
                    displayed.IsPlayable = selectable;
                    displayed.AvailabilityText = availability;
                    displayed.IsSelected = IsDiscardSelectionPending
                        ? _discardCardIds.Contains(card.Id)
                        : IsActiveSkillCardSelectionPending
                            ? _selectedActiveSkillCardIds.Contains(card.Id)
                            : card.Id == _selectedCardId;
                    continue;
                }
                Hand.Add(new CardViewModel
                {
                    Id = card.Id,
                    Name = card.DisplayName,
                    KindLabel = CardCatalog.Get(card.Kind).CategoryName,
                    SuitGlyph = GetSuitGlyph(card.Suit),
                    Rank = card.RankText,
                    Description = GetCardDescription(card.Kind),
                    AvailabilityText = availability,
                    IsPlayable = selectable,
                    IsSelected = IsDiscardSelectionPending
                        ? _discardCardIds.Contains(card.Id)
                        : IsActiveSkillCardSelectionPending
                            ? _selectedActiveSkillCardIds.Contains(card.Id)
                            : card.Id == _selectedCardId
                });
            }

            HumanSummary = $"{(IsNationalSnapshot ? FactionName(human.FactionId) : GetRoleName(human.Role ?? Role.Lord))} · {human.GeneralName} · {human.Hp}/{human.MaxHp} 体力" +
                (human.HasAlcoholEffect ? " · 酒效待下一张杀" : string.Empty);
        }
        else
        {
            Hand.Clear();
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
        RefreshMatchSummary();
        GameOverText = HasGameOver ? _snapshot.Winner == Winner.Draw ? "本局平局结束" :
            $"{(IsNationalSnapshot ? FactionName(_snapshot.WinnerFactionId) + "势力" : GetWinnerName(_snapshot.Winner))}获胜" : string.Empty;
        RaisePropertyChanged(nameof(GameOutcomeTitle));
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
        RefreshPresentation();
        RaisePropertyChanged(nameof(TableDecisionTitle));
    }

    private void SelectCard(CardViewModel card)
    {
        if (IsHandResponsePending)
        {
            SelectHandResponse(card);
            return;
        }
        if (IsDiscardSelectionPending)
        {
            ToggleDiscardCard(card);
            return;
        }
        if (IsActiveSkillCardSelectionPending)
        {
            ToggleActiveSkillCard(card);
            return;
        }
        if (_snapshot.PendingDecision?.Kind != DecisionKind.PlayCard || !card.IsPlayable)
        {
            return;
        }

        _selectedCardId = _selectedCardId == card.Id ? null : card.Id;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();

        var matching = _game.GetHumanLegalActions()
            .Where(action => action.CardId == _selectedCardId)
            .ToArray();
        if (matching.Length == 1 &&
            matching[0].TargetSeats.Count == 1 &&
            matching[0].Kind == LegalActionKind.Peach)
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
        RebuildPublicTargetChoices();
        RebuildTargetCombinationChoices();
        RefreshTargetHighlights();
    }

    private void ToggleActiveSkillCard(CardViewModel card)
    {
        ToggleActiveSkillCard(card.Id);
    }

    private void ToggleActiveSkillCard(int cardId)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } pending ||
            pending.ActiveSkillValidCardIds?.Contains(cardId) != true)
        {
            return;
        }

        var action = HumanActiveSkillAction;
        if (action is null)
        {
            return;
        }

        if (!_selectedActiveSkillCardIds.Remove(cardId))
        {
            if (_selectedActiveSkillCardIds.Count >= action.MaxCardCount)
            {
                return;
            }

            _selectedActiveSkillCardIds.Add(cardId);
        }

        foreach (var item in Hand)
        {
            item.IsSelected = _selectedActiveSkillCardIds.Contains(item.Id);
        }

        RebuildActiveSkillEquipmentChoices(
            _snapshot.Players.Single(player => player.IsHuman),
            pending.ActiveSkillValidCardIds.ToHashSet());

        var skillName = _snapshot.Players.Single(player => player.IsHuman).SkillName;
        SelectedCardText = _selectedActiveSkillCardIds.Count == 0
            ? $"未选择用于【{skillName}】的牌"
            : $"已选择 {_selectedActiveSkillCardIds.Count} 张牌用于【{skillName}】";
        RefreshSelectionHint();
    }

    private void SelectActiveSkillEquipmentChoice(PromptChoice choice)
    {
        if (choice.Cards.Count != 1)
        {
            return;
        }

        ToggleActiveSkillCard(choice.Cards[0]);
    }

    private void RebuildActiveSkillEquipmentChoices(
        PlayerSnapshot human,
        IReadOnlySet<int> activeSkillCardIds)
    {
        ActiveSkillEquipmentChoices.Clear();
        if (!IsActiveSkillCardSelectionPending)
        {
            return;
        }

        foreach (var equipment in human.Equipment.Where(card => activeSkillCardIds.Contains(card.Id)))
        {
            var selected = _selectedActiveSkillCardIds.Contains(equipment.Id);
            ActiveSkillEquipmentChoices.Add(new PromptChoice(
                new ChoiceId($"active-skill.equipment-{equipment.Id}"),
                selected
                    ? $"✓ 已选择装备【{equipment.DisplayName}】；点击取消"
                    : $"选择装备【{equipment.DisplayName}】",
                [equipment.Id],
                [],
                new Dictionary<string, string>()));
        }
    }

    private void SelectTarget(SeatViewModel seat)
    {
        if (IsActiveSkillTargetSelectionPending)
        {
            ToggleActiveSkillTarget(seat);
            return;
        }

        if (IsMultiTargetCardSelected)
        {
            ToggleCardTarget(seat);
            return;
        }

        if (!seat.IsLegalTarget)
        {
            return;
        }

        _selectedTargetSeat = _selectedTargetSeat == seat.Seat ? null : seat.Seat;
        RebuildPublicTargetChoices();
        RefreshTargetHighlights();
    }

    private void ToggleActiveSkillTarget(SeatViewModel seat)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } pending ||
            pending.ActiveSkillValidTargetSeats?.Contains(seat.Seat) != true)
        {
            return;
        }

        var action = HumanActiveSkillAction;
        if (action is null)
        {
            return;
        }

        if (!_selectedActiveSkillTargetSeats.Remove(seat.Seat))
        {
            if (_selectedActiveSkillTargetSeats.Count >= action.MaxTargetCount)
            {
                return;
            }

            _selectedActiveSkillTargetSeats.Add(seat.Seat);
        }

        foreach (var item in Seats)
        {
            item.IsSelectedTarget = _selectedActiveSkillTargetSeats.Contains(item.Seat);
        }

        RefreshSelectionHint();
    }

    private void SelectGeneral(GeneralChoiceViewModel choice)
    {
        if (!IsGeneralSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new CardGame.Core.SelectGeneralCommand(
                _snapshot.HumanSeat,
                choice.GeneralId,
                _snapshot.Revision,
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
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
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
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
            if (!result.Accepted)
            {
                PromptText = $"五谷丰登选牌未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectTargetCardChoice(PromptChoice choice)
    {
        if (!IsTargetCardSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
            if (!result.Accepted)
            {
                PromptText = $"暗牌位选择未执行：{result.Error?.Message}";
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
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
            if (!result.Accepted)
            {
                PromptText = $"火攻选牌未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectNullificationChoice(PromptChoice choice)
    {
        if (!IsNullificationSelectionPending || _snapshot.PendingDecision is not { } pending)
        {
            return;
        }

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
            if (!result.Accepted)
            {
                PromptText = $"无懈响应未执行：{result.Error?.Message}";
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
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
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
            var result = SubmitCommand(new AnswerPromptCommand(
                _snapshot.HumanSeat,
                pending.PromptId,
                choice.Id,
                _snapshot.Revision));
            if (!result.Accepted)
            {
                PromptText = $"技能触发未执行：{result.Error?.Message}";
                return;
            }

            Refresh(result.State);
        });
    }

    private void SelectPublicTargetChoice(PromptChoice choice)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } pending ||
            _selectedCardId is not { } selectedCardId ||
            choice.Cards.Count != 1 ||
            choice.Cards[0] != selectedCardId ||
            choice.Targets.Count != 1 ||
            !choice.Parameters.TryGetValue("target-card-id", out var targetCardIdText) ||
            !int.TryParse(targetCardIdText, out var targetCardId))
        {
            return;
        }

        var action = _game.GetHumanLegalActions().SingleOrDefault(candidate =>
            candidate.CardId == selectedCardId &&
            candidate.TargetSeat == choice.Targets[0] &&
            candidate.TargetCardId == targetCardId);
        if (action is null)
        {
            PromptText = "公开目标牌已不再合法，请重新选择。";
            RebuildPublicTargetChoices();
            RefreshTargetHighlights();
            return;
        }

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new PlayCardCommand(
                _snapshot.HumanSeat,
                selectedCardId,
                choice.Targets,
                _snapshot.Revision,
                pending.PromptId,
                action.PlayedCardKind,
                targetCardId));
            if (!result.Accepted)
            {
                PromptText = $"公开目标牌未执行：{result.Error?.Message}";
                return;
            }

            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private void SelectTargetCombinationChoice(PromptChoice choice)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } pending ||
            _selectedCardId is not { } selectedCardId ||
            choice.Cards.Count != 1 ||
            choice.Cards[0] != selectedCardId ||
            choice.Targets.Count < 2)
        {
            return;
        }

        var action = _game.GetHumanLegalActions().SingleOrDefault(candidate =>
            candidate.CardId == selectedCardId &&
            candidate.TargetSeats.SequenceEqual(choice.Targets));
        if (action is null)
        {
            PromptText = "目标组合已不再合法，请重新选择。";
            RebuildTargetCombinationChoices();
            RefreshTargetHighlights();
            return;
        }

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new PlayCardCommand(
                _snapshot.HumanSeat,
                selectedCardId,
                choice.Targets,
                _snapshot.Revision,
                pending.PromptId,
                action.PlayedCardKind,
                action.TargetCardId));
            if (!result.Accepted)
            {
                PromptText = $"目标组合未执行：{result.Error?.Message}";
                return;
            }

            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private void RebuildPublicTargetChoices()
    {
        PublicTargetChoices.Clear();
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } pending ||
            _selectedCardId is not { } selectedCardId)
        {
            HasPublicTargetChoices = false;
            return;
        }

        foreach (var choice in pending.Choices.Where(choice =>
                     choice.Cards.Count == 1 &&
                     choice.Cards[0] == selectedCardId &&
                     choice.Targets.Count == 1 &&
                     (!_selectedTargetSeat.HasValue || choice.Targets[0] == _selectedTargetSeat) &&
                     choice.Parameters.ContainsKey("target-card-id")))
        {
            PublicTargetChoices.Add(choice);
        }

        HasPublicTargetChoices = PublicTargetChoices.Count > 0;
    }

    private void RebuildTargetCombinationChoices()
    {
        TargetCombinationChoices.Clear();
        if (IsMultiTargetCardSelected || _snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } pending ||
            _selectedCardId is not { } selectedCardId)
        {
            HasTargetCombinationChoices = false;
            return;
        }

        foreach (var choice in pending.Choices.Where(choice =>
                     choice.Cards.Count == 1 &&
                     choice.Cards[0] == selectedCardId &&
                     choice.Targets.Count > 1))
        {
            TargetCombinationChoices.Add(choice);
        }

        HasTargetCombinationChoices = TargetCombinationChoices.Count > 0;
    }

    private void RefreshTargetHighlights()
    {
        if (IsActiveSkillTargetSelectionPending)
        {
            var pending = _snapshot.PendingDecision!;
            var activeLegalTargets = (pending.ActiveSkillValidTargetSeats ?? []).ToHashSet();
            foreach (var seat in Seats)
            {
                seat.IsLegalTarget = activeLegalTargets.Contains(seat.Seat);
                seat.IsSelectedTarget = _selectedActiveSkillTargetSeats.Contains(seat.Seat);
            }

            CanPlaySelected = false;
            CanPlaySelectedAsSlash = false;
            RefreshSelectionHint();
            return;
        }

        var legalActions = _snapshot.PendingDecision?.Kind == DecisionKind.PlayCard
            ? _game.GetHumanLegalActions()
            : [];
        var selectedActions = _selectedCardId is { } cardId
            ? legalActions.Where(action => action.CardId == cardId && action.Kind != LegalActionKind.Recast).ToArray()
            : [];
        var legalTargets = selectedActions
            .SelectMany(action => action.TargetSeats)
            .ToHashSet();
        if (!IsMultiTargetCardSelected) _selectedCardTargetSeats.Clear();
        else _selectedCardTargetSeats.IntersectWith(MultiTargetCardActions.SelectMany(action => action.TargetSeats));
        var selectedTargets = SelectedPlayTargets();

        foreach (var seat in Seats)
        {
            seat.IsLegalTarget = IsMultiTargetCardSelected
                ? _selectedCardTargetSeats.Contains(seat.Seat) || MultiTargetCardActions.Any(action =>
                    action.TargetSeats.Contains(seat.Seat) && _selectedCardTargetSeats.All(action.TargetSeats.Contains))
                : legalTargets.Contains(seat.Seat);
            seat.IsSelectedTarget = selectedTargets.Contains(seat.Seat);
        }

        var physicalCard = _snapshot.Players.FirstOrDefault(player => player.IsHuman)?.Hand.FirstOrDefault(card => card.Id == _selectedCardId);
        CanPlaySelected = selectedActions.Any(action => action.TargetSeats.SequenceEqual(selectedTargets)
            && action.TargetCardId is null && (action.PlayedCardKind is null || action.PlayedCardKind == physicalCard?.Kind));
        CanPlaySelectedAsSlash = selectedActions.Any(action =>
            action.TargetSeats.SequenceEqual(selectedTargets) && action.PlayedCardKind == CardKind.Slash && physicalCard?.Kind != CardKind.Slash);
        RefreshSelectionHint();
    }

    private void PlaySelectedCard() => PlaySelectedAction(asSlash: false);

    private void PlaySelectedAsSlash() => PlaySelectedAction(asSlash: true);

    private void PlaySelectedAction(bool asSlash)
    {
        if (_selectedCardId is not { } cardId || _snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt) return;
        var physicalKind = _snapshot.Players.Single(player => player.IsHuman).Hand.Single(card => card.Id == cardId).Kind;
        var targets = SelectedPlayTargets();
        var action = _game.GetHumanLegalActions().SingleOrDefault(action => action.CardId == cardId &&
            action.Kind != LegalActionKind.Recast && action.TargetCardId is null && action.TargetSeats.SequenceEqual(targets) &&
            (asSlash ? action.PlayedCardKind == CardKind.Slash : action.PlayedCardKind is null || action.PlayedCardKind == physicalKind));
        if (action is null) return;

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new PlayCardCommand(_snapshot.HumanSeat, cardId, action.TargetSeats,
                _snapshot.Revision, prompt.PromptId, action.PlayedCardKind, action.TargetCardId));
            if (!result.Accepted) return;
            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private void UseActiveSkill()
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt)
        {
            return;
        }

        var action = HumanActiveSkillAction;
        if (action?.Skill is not { } skill)
        {
            return;
        }

        var requiresCardSelection = action.MinCardCount > 0 || action.MaxCardCount > 0;
        var requiresTargetSelection = action.MinTargetCount > 0 || action.MaxTargetCount > 0;
        var requiresSelection = requiresCardSelection || requiresTargetSelection;
        if (requiresSelection && !_isSelectingActiveSkillCards)
        {
            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _isSelectingActiveSkillCards = true;
            RefreshCurrentView();
            return;
        }

        if (requiresSelection &&
            _selectedActiveSkillCardIds.Count == 0 &&
            _selectedActiveSkillTargetSeats.Count == 0)
        {
            _isSelectingActiveSkillCards = false;
            RefreshCurrentView();
            return;
        }

        if (requiresCardSelection &&
            (_selectedActiveSkillCardIds.Count < action.MinCardCount ||
             _selectedActiveSkillCardIds.Count > action.MaxCardCount))
        {
            SelectedCardText = $"至少选择 {action.MinCardCount} 张牌。";
            RefreshSelectionHint();
            return;
        }

        if (requiresTargetSelection &&
            (_selectedActiveSkillTargetSeats.Count < action.MinTargetCount ||
             _selectedActiveSkillTargetSeats.Count > action.MaxTargetCount))
        {
            SelectedCardText = $"至少选择 {action.MinTargetCount} 个目标。";
            RefreshSelectionHint();
            return;
        }

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new UseSkillCommand(
                _snapshot.HumanSeat,
                skill,
                _selectedActiveSkillCardIds.Order().ToArray(),
                _selectedActiveSkillTargetSeats.Order().ToArray(),
                _snapshot.Revision,
                prompt.PromptId));
            if (!result.Accepted)
            {
                PromptText = $"主动技能未执行：{result.Error?.Message}";
                Refresh(result.State);
                return;
            }

            _isSelectingActiveSkillCards = false;
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
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
            _selectedCardTargetSeats.Clear();
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _isSelectingActiveSkillCards = false;
            SelectedCardText = "未选择手牌";
            var result = SubmitCommand(new EndPlayPhaseCommand(_snapshot.HumanSeat, _snapshot.Revision, _snapshot.PendingDecision?.PromptId));
            Refresh(result.State);
        });
    }

    private void RespondToSlash(bool useDodge)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } prompt) return;
        var expected = useDodge ? prompt.Kind == DecisionKind.RespondSlash ? "slash" : "dodge" : "take-damage";
        var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.TryGetValue("response", out var response) && response == expected);
        if (choice is null) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AnswerPromptCommand(_snapshot.HumanSeat, prompt.PromptId, choice.Id, _snapshot.Revision));
            Refresh(result.State);
        });
    }

    private void StepAi()
    {
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AdvanceOneStepCommand(_snapshot.Revision));
            Refresh(result.State);
        });
    }

    private void RunToHuman()
    {
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new AdvanceCommand(_snapshot.Revision));
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
            else if (pending.Kind == DecisionKind.Nullification)
            {
                EventStack.Add($"      Nullification({pending.IncomingCard})");
                EventStack.Add($"        AskForResponse(Nullification, {pending.ValidCardIds.Count} cards)");
            }
            else if (pending.Kind == DecisionKind.RescueDying)
            {
                EventStack.Add($"      Dying(target: seat {pending.TargetSeat.GetValueOrDefault() + 1})");
                EventStack.Add(pending.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "alcohol")
                    ? "        AskForResponse(Peach/Alcohol)"
                    : "        AskForResponse(Peach)");
            }
            else if (pending.Kind == DecisionKind.Guicai)
            {
                EventStack.Add($"      Judgment(target: seat {pending.TargetSeat.GetValueOrDefault() + 1})");
                EventStack.Add("        AskForSkill(Guicai)");
            }
            else if (pending.Kind is
                DecisionKind.Feedback or
                DecisionKind.Yiji or
                DecisionKind.Jieming or
                DecisionKind.Yuanhu or
                DecisionKind.Ganglie or
                DecisionKind.GangliePunish or
                DecisionKind.Guicai or
                DecisionKind.Yingzi or
                DecisionKind.Tiandu or
                DecisionKind.Fanjian or
                DecisionKind.Guanxing)
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
        else if (_game.ResolutionStack.OfType<CardUseFrame>().LastOrDefault() is
        { CardKind: CardKind.IronChain } ironChain)
        {
            EventStack.Add("      UseCard(IronChain)");
            EventStack.Add($"        ToggleTargets({ironChain.TargetSeats.Count})");
        }
        else if (_game.ResolutionStack.OfType<CardUseFrame>().LastOrDefault() is
        {
            CardKind:
            CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning
        } delayedCard)
        {
            EventStack.Add($"      UseCard({delayedCard.CardKind})");
            if (delayedCard.CardKind == CardKind.Lightning)
            {
                EventStack.Add($"        PlaceJudgment(self: seat {delayedCard.TargetSeats.Single() + 1})");
            }
            else
            {
                EventStack.Add($"        PlaceJudgment({delayedCard.TargetSeats.Single()})");
            }
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
            IsAutoAdvance = false;
            PromptText = $"操作未执行：{exception.Message}";
            ActionHint = PromptText;
        }
    }

    private string GetPlayerLabel(int seat) =>
        _game.State.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? $"座位 {seat + 1}";

    private static string BuildHandText(PlayerSnapshot player)
    {
        var text = $"手牌 {player.HandCount}";
        if (player.HasAlcoholEffect)
        {
            text += " · 酒效生效";
        }

        if (player.IsChained)
        {
            text += " · 连环";
        }

        return text;
    }

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
        Role.TeamA => "青队",
        Role.TeamB => "赤队",
        _ => role.ToString()
    };

    private static string GetWinnerName(Winner winner) => winner switch
    {
        Winner.LordAndLoyalists => "主公与忠臣",
        Winner.Rebels => "反贼",
        Winner.Renegade => "内奸",
        Winner.TeamA => "青队",
        Winner.TeamB => "赤队",
        Winner.NationalFactionA => "魏势力",
        Winner.NationalFactionB => "蜀势力",
        Winner.NationalFactionC => "独立势力",
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

    private string GetVisibleSkillDescription(ContentSkillDefinition skill)
    {
        if (!_game.ModeId.StartsWith("identity:classic-", StringComparison.Ordinal))
        {
            return skill.Description;
        }

        return skill.LegacyKind switch
        {
            SkillKind.Kongcheng when _game.RulesVersion >= 15 =>
                "锁定技，若你没有手牌，你不能成为【杀】或【决斗】的目标。",
            SkillKind.Jianxiong when _game.RulesVersion >= 16 =>
                "当你受到伤害后，你可以获得造成此伤害的牌。",
            SkillKind.Zhiheng when _game.RulesVersion >= 17 =>
                "出牌阶段限一次，你可以弃置任意张牌，然后摸等量张牌。",
            SkillKind.Yingzi when _game.RulesVersion >= 21 =>
                "摸牌阶段，你可以多摸一张牌。",
            _ => skill.Description
        };
    }

    private static string GetKingdom(string generalId) => generalId switch
    {
        _ when generalId.StartsWith("classic:", StringComparison.Ordinal) => generalId switch
        {
            "classic:liu-bei" or "classic:zhuge-liang" => "蜀",
            "classic:sun-quan" or "classic:zhou-yu" => "吴",
            "classic:sima-yi" or "classic:xiahou-dun" or "classic:guo-jia" => "魏",
            _ => "群"
        },
        _ when generalId.StartsWith("national:wei-", StringComparison.Ordinal) => "魏",
        _ when generalId.StartsWith("national:shu-", StringComparison.Ordinal) => "蜀",
        _ when generalId.StartsWith("national:ambitious-", StringComparison.Ordinal) => "野心家",
        "cao-cao" or "standard:cao-cao" or "standard:guo-jia" or "guo-jia"
            or "standard:xun-yu" or "standard:demo-ganglie" or "standard:demo-guicai" => "魏",
        "liu-bei" or "guan-yu" or "zhang-fei" or "zhuge-liang" or "zhao-yun"
            or "standard:liu-bei" or "standard:guan-yu" or "standard:zhang-fei"
            or "standard:zhuge-liang" or "standard:zhao-yun" or "standard:demo-qicai" => "蜀",
        "sun-quan" or "zhou-yu" or "standard:sun-quan" or "standard:zhou-yu" or "standard:demo-kujin" or "standard:demo-zhiheng" => "吴",
        "standard:demo-rende" => "蜀",
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
