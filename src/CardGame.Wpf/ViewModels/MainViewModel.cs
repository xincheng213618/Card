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
    private CardConversionSource? _selectedConversionSource;
    private readonly HashSet<int> _selectedActiveSkillCardIds = [];
    private readonly List<int> _selectedActiveSkillTargetSeats = [];
    private CardKind? _selectedEquipmentEffectKind;
    private string? _selectedProgramSkillId;
    private string? _selectedProgramActivationId;
    private int? _selectedProgramSkillOwnerSeat;
    private IReadOnlyList<int>? _selectedActiveSkillTargetContract;
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
        IPlayerPreferencesStore? preferencesStore = null,
        ContentRegistry? contentRegistry = null)
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
        SelectEquipmentPlayChoiceCommand = new RelayCommand<PromptChoice>(SelectEquipmentPlayChoice);
        SelectPublicTargetChoiceCommand = new RelayCommand<PromptChoice>(SelectPublicTargetChoice);
        SelectTargetCombinationChoiceCommand = new RelayCommand<PromptChoice>(SelectTargetCombinationChoice);
        PlaySelectedCardCommand = new RelayCommand(PlaySelectedCard, () => CanPlaySelected);
        PlaySelectedAsSlashCommand = new RelayCommand(PlaySelectedAsSlash, () => CanPlaySelectedAsSlash);
        UseActiveSkillCommand = new RelayCommand(UseActiveSkill, () => CanUseActiveSkill);
        SelectActiveSkillCommand = new RelayCommand<LegalAction>(UseActiveSkill);
        EndTurnCommand = new RelayCommand(EndTurn, () => CanEndTurn);
        RespondDodgeCommand = new RelayCommand(() => RespondToSlash(true), () => CanRespondDodge);
        DeclineResponseCommand = new RelayCommand(() => RespondToSlash(false), () => CanDeclineResponse);

        _seedOverride = seed;
        _contentRegistry = contentRegistry ??
            (useExpandedContent
                ? ComposedSkillContentRegistry.CreateShowcase()
                : StandardContentRegistry.CreateWithTeamModes());
        TableModes = useExpandedContent
            ? [
                new TableModeOption(8, "八人经典身份", "1 主公 · 2 忠臣\n4 反贼 · 1 内奸", "identity:classic-8"),
                new TableModeOption(5, "五人经典身份", "1 主公 · 1 忠臣\n2 反贼 · 1 内奸", "identity:classic-5"),
                .. (_contentRegistry.Modes.ContainsKey("identity:classic-boundary-8")
                    ? new[]
                    {
                        new TableModeOption(8, "八人界限突破身份", "经典池替换界张角 · 1 主公\n2 忠臣 · 4 反贼 · 1 内奸", "identity:classic-boundary-8"),
                        new TableModeOption(5, "五人界限突破身份", "经典池替换界张角 · 1 主公\n1 忠臣 · 2 反贼 · 1 内奸", "identity:classic-boundary-5")
                    }
                    : Array.Empty<TableModeOption>()),
                new TableModeOption(8, "八人技能演示", "旧演示武将池 · 用于机制验证", "identity:active-skills-8"),
                new TableModeOption(5, "五人技能演示", "旧演示武将池 · 用于机制验证", "identity:active-skills-5"),
                new TableModeOption(5, "技能组合体验", "配置文件组合技能 · 修正、转化与主动效果", "identity:composed-skills-5"),
                new TableModeOption(4, "2v2阵营", "青队 2 · 赤队 2\n公开阵营，协作对抗", "team:standard-2v2"),
                new TableModeOption(6, "国战 M3", "魏 3 · 蜀 2 · 野心家 1\n六人独立势力试验", "national:ambitious-6"),
                .. (_contentRegistry.Modes.ContainsKey("national:zhang-jiao-4")
                    ? new[]
                    {
                        new TableModeOption(4, "国战张角", "魏 1 · 蜀 2 · 群 1\n标准国战张角试验", "national:zhang-jiao-4")
                    }
                    : Array.Empty<TableModeOption>()),
                new TableModeOption(4, "国战 Lite", "魏蜀双将 · 暗置明置\n四人简化国战", "national:lite-4")
            ]
            : [
                new TableModeOption(8, "八人身份", "1 主公 · 2 忠臣 · 4 反贼 · 1 内奸", "identity:standard-8"),
                new TableModeOption(5, "五人身份", "1 主公 · 1 忠臣 · 2 反贼 · 1 内奸", "identity:standard-5"),
                new TableModeOption(4, "2v2公开阵营", "青队 2 · 赤队 2 · 阵营公开 · 击败另一队获胜", "team:standard-2v2")
            ];
        DeckOptions =
        [
            .. (_contentRegistry.Decks.ContainsKey("classic:standard-deck")
                ? new[] { new DeckOption("classic:standard-deck", "军争 160 张", "标准版、EX 与军争篇完整合并牌堆") }
                : []),
            .. (_contentRegistry.Decks.ContainsKey("classic:standard-108")
                ? new[] { new DeckOption("classic:standard-108", "标准 108 张", "标准版 104 张与 4 张 EX，不含军争篇") }
                : [])
        ];
        InitializePlayerGuide();
        InitializePresentation(autoAdvance);
        InitializeOpeningDeal();
        InitializeGeneralSelectionPresentation();
        InitializeGameSetup();
        InitializePersistence(saveStore);
        InitializeHistory(historyStore);
        InitializeTutorial();
        InitializePreferences(preferencesStore);
        InitializeSettings();
        NewGame();
        InitializeGeneralGallery();
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
    public ObservableCollection<PromptChoice> EquipmentPlayChoices { get; } = [];
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
    public ICommand SelectEquipmentPlayChoiceCommand { get; }
    public ICommand SelectPublicTargetChoiceCommand { get; }
    public ICommand SelectTargetCombinationChoiceCommand { get; }
    public ICommand PlaySelectedCardCommand { get; }
    public ICommand PlaySelectedAsSlashCommand { get; }
    public ICommand UseActiveSkillCommand { get; }
    public ICommand SelectActiveSkillCommand { get; }
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
        private set
        {
            if (!SetProperty(ref _isGeneralSelectionPending, value)) return;
            RaisePropertyChanged(nameof(CanConfirmGeneralChoice));
            if (ConfirmGeneralChoiceCommand is RelayCommand command) command.NotifyCanExecuteChanged();
        }
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
                DeckId = IsClassicIdentityModeSelection ? SelectedDeck?.DeckId : null,
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
        DismissOpeningDeal();
        DetachEngine();
        _game = game;
        ResetPresentation();
        Hand.Clear();
        _selectedCardId = null;
        _selectedConversionSource = null;
        _selectedActiveSkillCardIds.Clear();
        _selectedActiveSkillTargetSeats.Clear();
        _selectedEquipmentEffectKind = null;
        _selectedProgramSkillId = null;
        _selectedProgramActivationId = null;
        _selectedProgramSkillOwnerSeat = null;
            _selectedActiveSkillTargetContract = null;
        _activeSkillPromptId = null;
        _isSelectingActiveSkillCards = false;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();
        _discardCardIds.Clear();
        _discardPromptId = null;
        SelectedCardText = "未选择手牌";
        GameLog.Clear();
        ResetBattleLog();
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
        AddBattleLogEntry(entry);
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
            _selectedEquipmentEffectKind = null;
            _selectedProgramSkillId = null;
            _selectedProgramActivationId = null;
            _selectedProgramSkillOwnerSeat = null;
            _selectedActiveSkillTargetContract = null;
            _isSelectingActiveSkillCards = false;
        }

        _activeSkillPromptId = promptId;
        var hasSelectionContract = pending?.Kind == DecisionKind.PlayCard &&
                                   HumanActiveSkillAction is { } selectedAction &&
                                   (selectedAction.MaxCardCount > 0 || selectedAction.MaxTargetCount > 0);
        if (!hasSelectionContract)
        {
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _selectedEquipmentEffectKind = null;
            _selectedProgramSkillId = null;
            _selectedProgramActivationId = null;
            _selectedProgramSkillOwnerSeat = null;
            _selectedActiveSkillTargetContract = null;
            _isSelectingActiveSkillCards = false;
            return;
        }

        var action = HumanActiveSkillAction;
        _selectedActiveSkillCardIds.IntersectWith(action?.SelectableCardIds ?? []);
        _selectedActiveSkillTargetSeats.RemoveAll(seat => action?.SelectableTargetSeats.Contains(seat) != true);
    }

    private void Refresh(GameSnapshot snapshot)
    {
        if (_snapshot?.PendingDecision?.PromptId != snapshot.PendingDecision?.PromptId)
        {
            _selectedCardTargetSeats.Clear();
            _selectedConversionSource = null;
        }
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
        EquipmentPlayChoices.Clear();
        PublicTargetChoices.Clear();
        TargetCombinationChoices.Clear();
        PublicRevealedCards.Clear();
        foreach (var card in _snapshot.PublicRevealedCards)
        {
            var definition = CardCatalog.Get(card.Kind);
            PublicRevealedCards.Add(new CardViewModel
            {
                Id = card.Id,
                Kind = card.Kind,
                Name = card.DisplayName,
                KindLabel = definition.CategoryName,
                SuitGlyph = GetSuitGlyph(card.Suit),
                Rank = card.RankText,
                Description = GetCardDescription(card.Kind),
                IsPlayable = false,
                IsPublicChoice = RevealedCardChoice(card.Id) is not null,
                IsSelected = false
            });
        }
        HasPublicRevealedCards = PublicRevealedCards.Count > 0;
        RebuildDeferredCardViews();
        RebuildDeclaredCardViews();
        RebuildGeneralLibraryViews();
        if (_snapshot.PendingDecision is { Kind: DecisionKind.SelectGeneral } pending)
        {
            foreach (var choice in pending.Choices.Where(choice => choice.ContentIds.Count == 1))
            {
                var general = _game.ContentRegistry!.Generals[choice.ContentIds[0]];
                var skills = general.SkillIds.Select(_game.ContentRegistry.GetSkill).ToArray();
                GeneralChoices.Add(new GeneralChoiceViewModel
                {
                    GeneralId = choice.ContentIds[0],
                    Portrait = GetGeneralPortrait(choice.ContentIds[0]),
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
        SyncGeneralChoicePreview(_snapshot.PendingDecision?.Kind == DecisionKind.SelectGeneral
            ? _snapshot.PendingDecision.PromptId
            : null);
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
        PublicRevealTitle = _snapshot.PendingDecision switch
        {
            { SkillPrompt: { } presentation } => $"{presentation.Name} · 公开牌",
            { Kind: DecisionKind.FireAttackDiscard } => "火攻 · 公开牌",
            { Kind: DecisionKind.SelectHarvestCard } => "五谷丰登 · 公开牌",
            _ => "公开牌"
        };
        if (_snapshot.PendingDecision is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } responsePrompt)
        {
            foreach (var choice in responsePrompt.Choices)
            {
                ResponseChoices.Add(choice);
            }
        }
        IsResponseSelectionPending = _snapshot.PendingDecision?.Kind is
            DecisionKind.RespondDodge or DecisionKind.RespondSlash;
        if (_snapshot.PendingDecision is { SkillPrompt: not null } modulePrompt)
        {
            foreach (var choice in modulePrompt.Choices)
            {
                SkillChoices.Add(choice);
            }
        }
        else if (_snapshot.PendingDecision is
            {
                Kind: DecisionKind.SelectFaction or
                    DecisionKind.SkipDiscardPolicy or
                    DecisionKind.Yingbo or
                    DecisionKind.StoneAxe or
                    DecisionKind.CixiongDoubleSwords or
                    DecisionKind.QinglongCrescentBlade or
                    DecisionKind.IceSword or
                    DecisionKind.QilinBow or
                    DecisionKind.ProgramJudgmentTrigger or
                    DecisionKind.ProgramJudgmentReplacement or
                    DecisionKind.ProgramTopReorder or
                    DecisionKind.ProgramRepeatJudgment or
                    DecisionKind.ZhuqueFan
            } skillPrompt)
        {
            foreach (var choice in skillPrompt.Choices)
            {
                SkillChoices.Add(choice);
            }
        }
        // Keep conditional access separate: Roslyn 4.11 revisits long "or" patterns exponentially.
        var pendingDecisionKind = _snapshot.PendingDecision?.Kind;
        IsSkillSelectionPending = _snapshot.PendingDecision?.SkillPrompt is not null;
        IsSkillSelectionPending |= pendingDecisionKind is
            DecisionKind.SelectFaction or
            DecisionKind.SkipDiscardPolicy or
            DecisionKind.Yingbo or
            DecisionKind.StoneAxe or
            DecisionKind.CixiongDoubleSwords or
            DecisionKind.QinglongCrescentBlade or
            DecisionKind.IceSword or
            DecisionKind.QilinBow or
            DecisionKind.ProgramJudgmentTrigger or
            DecisionKind.ProgramJudgmentReplacement or
                    DecisionKind.ProgramTopReorder or
                    DecisionKind.ProgramRepeatJudgment or
            DecisionKind.ZhuqueFan;
        RaisePropertyChanged(nameof(HasPinnedPublicModuleChoices));

        var legalActions = _game.GetHumanLegalActions();
        var playableCardIds = legalActions
            .Where(action => action.CardId.HasValue)
            .Select(action => action.CardId!.Value)
            .ToHashSet();
        if (IsDiscardSelectionPending) playableCardIds.UnionWith(_snapshot.PendingDecision!.ValidCardIds);
        var activeSkillCardIds = IsActiveSkillCardSelectionPending
            ? (HumanActiveSkillAction?.SelectableCardIds ?? []).ToHashSet()
            : [];

        if (_selectedCardId is { } selectedId && !playableCardIds.Contains(selectedId) && HandResponseChoice(selectedId) is null)
        {
            _selectedCardId = null;
            _selectedConversionSource = null;
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
                Portrait = GetGeneralPortrait(player.GeneralId),
                GeneralName = player.GeneralName,
                IsNationalSeat = IsNationalSnapshot,
                PrimaryGeneral = IsNationalSnapshot ? GeneralSlotViewModel.FromPlayer(player, false, GetGeneralPortrait) : null,
                SecondaryGeneral = IsNationalSnapshot ? GeneralSlotViewModel.FromPlayer(player, true, GetGeneralPortrait) : null,
                RelationshipLabel = IsNationalSnapshot ? NationalRelationship(player) : string.Empty,
                SecondaryGeneralText = IsNationalSnapshot ? $"副将：{player.SecondaryGeneralName ?? (player.IsHuman ? "待选" : "暗将")}" +
                    (player.IsHuman && player.SecondaryGeneralName is not null && !player.IsSecondaryGeneralPublic ? "·暗" : "") : string.Empty,
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                HandCount = player.HandCount,
                IsChained = player.IsChained,
                IsFaceDown = player.IsFaceDown,
                BuquWoundText = player.BuquWounds is { Count: > 0 }
                    ? $"创 {string.Join('/', player.BuquWounds.Select(card => card.Rank))}"
                    : string.Empty,
                AuthorityText = player.AuthorityCount > 0
                    ? $"{player.AuthorityName ?? "权"} ×{player.AuthorityCount}"
                    : string.Empty,
                ChunlaoText = player.ChunlaoCount > 0
                    ? $"醇 ×{player.ChunlaoCount}"
                    : string.Empty,
                ChunlaoTooltip = player.ChunlaoCards is { Count: > 0 }
                    ? $"公开的“醇”：{string.Join("、", player.ChunlaoCards.Select(card => $"{card.DisplayName} {card.Suit}{card.Rank}"))}"
                    : string.Empty,
                PojunHoldText = player.PojunHoldCount > 0
                    ? $"破 ×{player.PojunHoldCount}"
                    : string.Empty,
                PojunHoldTooltip = player.PojunHoldCount > 0
                    ? $"被【破军】扣置的牌 {player.PojunHoldCount} 张（回合结束后回到其手牌）"
                    : string.Empty,
                DeferredPileText = PublicStateBadge(player),
                DeferredPileTooltip = PublicStateTooltip(player),
                HasAlcoholEffect = player.HasAlcoholEffect,
                SkillName = IsNationalSnapshot
                    ? $"{VisibleSkillNames(player.Skills)} / {VisibleSkillNames(player.SecondarySkills)}"
                    : VisibleSkillNames(player.Skills),
                Name = $"{player.GeneralName} · {(player.IsHuman ? "你" : $"AI {player.Seat + 1}")}",
                Kingdom = IsNationalSnapshot
                    ? FactionName(player.FactionId)
                    : player.FactionId is not null
                        ? $"神→{FactionName(player.FactionId)}"
                        : GetKingdom(player.GeneralId),
                RoleLabel = IsNationalSnapshot ? FactionName(player.FactionId) : player.Role is { } role ? GetRoleName(role) : "?",
                TeamId = player.TeamId,
                IsTeammate = IsNationalSnapshot ? NationalRelationship(player) == "同伴" : !player.IsHuman && player.TeamId is not null &&
                    player.TeamId == _snapshot.Players.SingleOrDefault(seat => seat.IsHuman)?.TeamId,
                HpText = $"体力 {player.Hp}/{player.MaxHp}",
                HandText = BuildHandText(player),
                EquipmentText = player.Equipment.Count == 0
                    ? "装备 —"
                    : $"装备 {string.Join(" · ", player.Equipment.Select(card =>
                        card.Kind == CardKind.WoodenOx && player.WoodenOxGrainCount > 0
                            ? $"{card.DisplayName}（粮 {player.WoodenOxGrainCount}）"
                            : card.DisplayName))}",
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
            RebuildEquipmentPlayChoices(human, legalActions);
            var visibleCards = human.Hand.Concat(human.WoodenOxGrain ?? []).ToArray();
            var grainIds = (human.WoodenOxGrain ?? []).Select(card => card.Id).ToHashSet();
            var ids = visibleCards.Select(card => card.Id).ToHashSet();
            for (var i = Hand.Count - 1; i >= 0; i--)
                if (!ids.Contains(Hand[i].Id)) Hand.RemoveAt(i);
            var existing = Hand.ToDictionary(card => card.Id);
            foreach (var card in visibleCards)
            {
                var activeSkillSelectable = activeSkillCardIds.Contains(card.Id);
                var responseChoice = HandResponseChoice(card.Id);
                var normallyPlayable = playableCardIds.Contains(card.Id) || responseChoice is not null;
                var normallySelectable = TutorialAllowsHandCard(card, normallyPlayable);
                var availability = IsActiveSkillSelectionPending
                    ? activeSkillSelectable ? $"可用于{HumanActiveSkillAction?.Description ?? "主动技能"}" : "本次技能不能选择这张牌；可按 Esc 退出技能选择。"
                    : TutorialHandAvailability(card, normallyPlayable, IsHandResponsePending
                        ? responseChoice is not null ? $"{responseChoice.Description}；选中后按 Enter 确认。" : HandResponseUnavailableHint(card.Id)
                        : handGuidance.GetValueOrDefault(card.Id)?.Message ?? string.Empty);
                if (IsPhysicalCardRestricted(human.Seat, card.Id))
                    availability = string.IsNullOrEmpty(availability) ? "本回合不能使用或打出"
                        : availability + "；本回合不能使用或打出";
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
                    Kind = card.Kind,
                    Name = card.DisplayName,
                    KindLabel = grainIds.Contains(card.Id)
                        ? $"粮 · {CardCatalog.Get(card.Kind).CategoryName}"
                        : CardCatalog.Get(card.Kind).CategoryName,
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
                (!IsNationalSnapshot && human.FactionId is not null ? $" · 本局{FactionName(human.FactionId)}势力" : string.Empty) +
                (human.WoodenOxGrainCount > 0 ? $" · 木牛粮 {human.WoodenOxGrainCount}" : string.Empty) +
                (human.AuthorityCount > 0 ? $" · {human.AuthorityName ?? "权"} {human.AuthorityCount}" : string.Empty) +
                (human.ChunlaoCount > 0 ? $" · 醇 {human.ChunlaoCount}" : string.Empty) +
                (human.PojunHoldCount > 0 ? $" · 破 {human.PojunHoldCount}" : string.Empty) +
                (human.PrivateReserveCount > 0 ? $" · 星 {human.PrivateReserveCount}" : string.Empty) +
                (human.PublicDeferredPileCount > 0 ? $" · {human.PublicDeferredPileName ?? "牌堆"} {human.PublicDeferredPileCount}" : string.Empty) +
                (human.PublicPersistentPileCount > 0 ? $" · {human.PublicPersistentPileName ?? "牌堆"} {human.PublicPersistentPileCount}" : string.Empty) +
                (human.Markers is { Count: > 0 } ? " · " + PublicMarkerBadge(human) : string.Empty) +
                (human.DeferredHandAlignments is { Count: > 0 } ? " · " + DeferredHandAlignmentBadge(human) : string.Empty) +
                (HasIssuedUseProhibition(human) ? " · " + IssuedUseProhibitionBadge(human) : string.Empty) +
                (human.IssuedPlayPhaseSuitUseAllowances is { Count: > 0 } ? " · " + PhaseSuitAllowanceBadge(human) : string.Empty) +
                (human.BeneficiarySuitShields is { Count: > 0 } ? " · " + SuitShieldBadge(human) : string.Empty) +
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
        ResponseButtonText = _snapshot.PendingDecision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-defense-dodge") == true
            ? "护驾出闪"
            : _snapshot.PendingDecision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash") == true
                ? "激将出杀"
            : _snapshot.PendingDecision?.Kind == DecisionKind.RespondSlash
                ? "快速响应杀"
                : "快速响应闪";
        CanStepAi = _snapshot.Status == EngineStatus.Running && _snapshot.PendingDecision is null;

        RebuildAiThoughts();
        RebuildEventStack();
        RefreshTargetHighlights();
        RefreshPresentation();
        RefreshOpeningDeal();
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
        _selectedConversionSource = null;
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
        RebuildEquipmentPlayChoices(
            _snapshot.Players.Single(player => player.IsHuman),
            _game.GetHumanLegalActions());
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
            HumanActiveSkillAction?.SelectableCardIds.Contains(cardId) != true)
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
            action.SelectableCardIds.ToHashSet());

        var skillName = ActiveSkillName(action);
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

    private void SelectEquipmentPlayChoice(PromptChoice choice)
    {
        if (choice.Cards.Count != 1 || _snapshot.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            return;
        }

        var cardId = choice.Cards[0];
        if (TryReadConversionSource(choice.Parameters, out var conversionSource))
        {
            _selectedCardId = cardId;
            _selectedConversionSource = _selectedConversionSource == conversionSource ? null : conversionSource;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            foreach (var item in Hand) item.IsSelected = item.Id == cardId;
            SelectedCardText = _selectedConversionSource is null
                ? $"已选择：{Hand.FirstOrDefault(card => card.Id == cardId)?.Name ?? "牌"}"
                : choice.Description;
            RebuildEquipmentPlayChoices(
                _snapshot.Players.Single(player => player.IsHuman),
                _game.GetHumanLegalActions());
            RebuildPublicTargetChoices();
            RebuildTargetCombinationChoices();
            RefreshTargetHighlights();
            return;
        }

        var equipment = _snapshot.Players.Single(player => player.IsHuman).Equipment
            .SingleOrDefault(card => card.Id == cardId);
        if (equipment is null)
        {
            return;
        }

        _selectedCardId = _selectedCardId == cardId ? null : cardId;
        _selectedConversionSource = null;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();
        SelectedCardText = _selectedCardId is null
            ? "未选择手牌或装备"
            : $"已选择装备：{equipment.DisplayName}";
        RebuildEquipmentPlayChoices(
            _snapshot.Players.Single(player => player.IsHuman),
            _game.GetHumanLegalActions());
        RebuildPublicTargetChoices();
        RefreshTargetHighlights();
    }

    private void RebuildEquipmentPlayChoices(
        PlayerSnapshot human,
        IReadOnlyList<LegalAction> legalActions)
    {
        EquipmentPlayChoices.Clear();
        if (_snapshot.PendingDecision?.Kind != DecisionKind.PlayCard || IsActiveSkillSelectionPending)
        {
            return;
        }

        var playableEquipmentIds = legalActions
            .Where(action => action.CardId.HasValue && action.PlayedCardKind is not null)
            .Select(action => action.CardId!.Value)
            .ToHashSet();
        foreach (var equipment in human.Equipment.Where(card => playableEquipmentIds.Contains(card.Id)))
        {
            var selected = _selectedCardId == equipment.Id;
            EquipmentPlayChoices.Add(new PromptChoice(
                new ChoiceId($"equipment-play.card-{equipment.Id}"),
                selected
                    ? $"✓ 已选择装备【{equipment.DisplayName}】；点击取消"
                    : $"将装备【{equipment.DisplayName}】用于牌型转化",
                [equipment.Id],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "select-equipment-play-card"
                }));
        }

        if (_selectedCardId is not { } selectedCardId)
        {
            return;
        }

        foreach (var action in legalActions
                     .Where(action => action.CardId == selectedCardId && action.ConversionSource is not null)
                     .GroupBy(action => (action.ConversionSource, action.PlayedCardKind))
                     .Select(group => group.First()))
        {
            var source = action.ConversionSource!;
            var selected = _selectedConversionSource == source;
            var skillName = _contentRegistry.Skills[source.SkillId].Name;
            var label = $"【{skillName}】当作【{CardCatalog.Get(action.PlayedCardKind ?? CardKind.Slash).DisplayName}】使用";
            EquipmentPlayChoices.Add(new PromptChoice(
                new ChoiceId($"conversion.{source.SkillId}.{source.BindingId}.{source.OwnerSeat}.{source.SkillInstanceId}"),
                selected ? $"✓ {label}" : label,
                [selectedCardId],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "select-card-conversion",
                    ["conversion-skill-id"] = source.SkillId,
                    ["conversion-binding-id"] = source.BindingId,
                    ["conversion-owner-seat"] = source.OwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["conversion-instance-id"] = source.SkillInstanceId
                }));
        }
    }

    private static bool TryReadConversionSource(
        IReadOnlyDictionary<string, string> parameters,
        out CardConversionSource source)
    {
        source = null!;
        if (!parameters.TryGetValue("conversion-skill-id", out var skillId) ||
            !parameters.TryGetValue("conversion-binding-id", out var bindingId) ||
            !parameters.TryGetValue("conversion-owner-seat", out var ownerSeatText) ||
            !int.TryParse(ownerSeatText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ownerSeat) ||
            !parameters.TryGetValue("conversion-instance-id", out var instanceId))
        {
            return false;
        }

        source = new CardConversionSource(skillId, bindingId, ownerSeat, instanceId);
        return true;
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

        var pileOwnerSeat = HumanActiveSkillAction?.ProgramSkillOwnerSeat ?? human.Seat;
        var pileOwner = _snapshot.Players.SingleOrDefault(player => player.Seat == pileOwnerSeat);
        var pileName = pileOwner?.AuthorityName ?? "权";
        var ownerLabel = pileOwnerSeat == human.Seat ? string.Empty : $"{pileOwner?.GeneralName ?? $"角色 {pileOwnerSeat + 1}"}的";
        foreach (var authority in (pileOwner?.AuthorityCards ?? []).Where(card => activeSkillCardIds.Contains(card.Id)))
        {
            var selected = _selectedActiveSkillCardIds.Contains(authority.Id);
            ActiveSkillEquipmentChoices.Add(new PromptChoice(
                new ChoiceId($"active-skill.authority-{authority.Id}"),
                selected
                    ? $"✓ 已选择{ownerLabel}“{pileName}”【{authority.DisplayName}】；点击取消"
                    : $"选择{ownerLabel}“{pileName}”【{authority.DisplayName}】",
                [authority.Id],
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
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } ||
            HumanActiveSkillAction?.SelectableTargetSeats.Contains(seat.Seat) != true)
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

        IsIdentityRevealOpen = false;
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
            candidate.TargetCardId == targetCardId &&
            ConversionSourceMatchesChoice(candidate.ConversionSource, choice.Parameters) &&
            AdditionalConversionSourcesMatchChoice(candidate.AdditionalConversionSources, choice.Parameters));
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
                targetCardId)
            {
                ConversionSource = action.ConversionSource,
                AdditionalConversionSources = action.AdditionalConversionSources,
            });
            if (!result.Accepted)
            {
                PromptText = $"公开目标牌未执行：{result.Error?.Message}";
                return;
            }

            _selectedCardId = null;
            _selectedConversionSource = null;
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
            candidate.TargetSeats.SequenceEqual(choice.Targets) &&
            ConversionSourceMatchesChoice(candidate.ConversionSource, choice.Parameters) &&
            AdditionalConversionSourcesMatchChoice(candidate.AdditionalConversionSources, choice.Parameters));
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
                action.TargetCardId)
            {
                ConversionSource = action.ConversionSource,
                AdditionalConversionSources = action.AdditionalConversionSources,
            });
            if (!result.Accepted)
            {
                PromptText = $"目标组合未执行：{result.Error?.Message}";
                return;
            }

            _selectedCardId = null;
            _selectedConversionSource = null;
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
            var activeLegalTargets = (HumanActiveSkillAction?.SelectableTargetSeats ?? []).ToHashSet();
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
            ? legalActions.Where(action => action.CardId == cardId && action.Kind != LegalActionKind.Recast &&
                action.ConversionSource == _selectedConversionSource).ToArray()
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

        var human = _snapshot.Players.FirstOrDefault(player => player.IsHuman);
        var physicalCard = human?.Hand.Concat(human.WoodenOxGrain ?? []).Concat(human.Equipment).FirstOrDefault(card => card.Id == _selectedCardId);
        CanPlaySelected = selectedActions.Any(action => action.TargetSeats.SequenceEqual(selectedTargets)
            && action.TargetCardId is null && (action.PlayedCardKind is null || action.PlayedCardKind == physicalCard?.Kind));
        var conversionActions = selectedActions.Where(action =>
            action.TargetSeats.SequenceEqual(selectedTargets) && action.TargetCardId is null &&
            action.PlayedCardKind is { } effectiveKind && effectiveKind != physicalCard?.Kind).ToArray();
        CanPlaySelectedAsSlash = conversionActions.Length == 1;
        RefreshSelectionHint();
    }

    private void PlaySelectedCard() => PlaySelectedAction(asSlash: false);

    private void PlaySelectedAsSlash() => PlaySelectedAction(asSlash: true);

    private void PlaySelectedAction(bool asSlash)
    {
        if (_selectedCardId is not { } cardId || _snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt) return;
        var human = _snapshot.Players.Single(player => player.IsHuman);
        var physicalKind = human.Hand.Concat(human.WoodenOxGrain ?? []).Concat(human.Equipment).Single(card => card.Id == cardId).Kind;
        var targets = SelectedPlayTargets();
        var action = SelectPlayAction(_game.GetHumanLegalActions(), cardId, targets, physicalKind,
            asSlash, _selectedConversionSource);
        if (action is null) return;

        ExecuteSafely(() =>
        {
            var result = SubmitCommand(CreatePlayCommand(_snapshot.HumanSeat, cardId, action,
                _snapshot.Revision, prompt.PromptId));
            if (!result.Accepted) return;
            _selectedCardId = null;
            _selectedConversionSource = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private static LegalAction? SelectPlayAction(
        IReadOnlyList<LegalAction> actions,
        int cardId,
        IReadOnlyList<int> targets,
        CardKind physicalKind,
        bool converted,
        CardConversionSource? conversionSource)
    {
        var matches = actions.Where(action => action.CardId == cardId &&
            action.Kind != LegalActionKind.Recast && action.TargetCardId is null &&
            action.TargetSeats.SequenceEqual(targets) &&
            (converted
                ? action.PlayedCardKind is { } effectiveKind && effectiveKind != physicalKind
                : action.PlayedCardKind is null || action.PlayedCardKind == physicalKind) &&
            action.ConversionSource == conversionSource).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static PlayCardCommand CreatePlayCommand(
        int actorSeat,
        int cardId,
        LegalAction action,
        long revision,
        PromptId promptId) => new(actorSeat, cardId, action.TargetSeats, revision, promptId,
            action.PlayedCardKind, action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources,
        };

    private static bool ConversionSourceMatchesChoice(
        CardConversionSource? source,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.ContainsKey("conversion-skill-id")) return source is null;
        return TryReadConversionSource(parameters, out var choiceSource) &&
               source == choiceSource;
    }

    private static bool AdditionalConversionSourcesMatchChoice(
        IReadOnlyList<CardConversionSource>? sources,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("additional-conversion-count", out var countText))
        {
            return sources is null || sources.Count == 0;
        }
        if (!int.TryParse(countText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var count) ||
            sources is null || sources.Count != count)
        {
            return false;
        }
        for (var index = 0; index < count; index++)
        {
            if (!parameters.TryGetValue($"additional-conversion-{index}-skill-id", out var skillId) ||
                !parameters.TryGetValue($"additional-conversion-{index}-binding-id", out var bindingId) ||
                !parameters.TryGetValue($"additional-conversion-{index}-owner-seat", out var ownerSeatText) ||
                !int.TryParse(ownerSeatText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var ownerSeat) ||
                !parameters.TryGetValue($"additional-conversion-{index}-instance-id", out var instanceId) ||
                sources[index] != new CardConversionSource(skillId, bindingId, ownerSeat, instanceId))
            {
                return false;
            }
        }
        return true;
    }

    private void UseActiveSkill()
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt)
        {
            return;
        }

        var action = HumanActiveSkillAction;
        if (action is null)
        {
            return;
        }
        _selectedEquipmentEffectKind = action.EquipmentKind;
        _selectedProgramSkillId = action.ProgramSkillId;
        _selectedProgramActivationId = action.ProgramActivationId;
        _selectedProgramSkillOwnerSeat = action.ProgramSkillOwnerSeat;
        _selectedActiveSkillTargetContract = action.TargetSeats.ToArray();

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
            var selectedCards = _selectedActiveSkillCardIds.Order().ToArray();
            var selectedTargets = action.TargetSeats.Count > 0 &&
                action.TargetSeats.Count == _selectedActiveSkillTargetSeats.Count &&
                action.TargetSeats.ToHashSet().SetEquals(_selectedActiveSkillTargetSeats)
                    ? action.TargetSeats.ToArray() : _selectedActiveSkillTargetSeats.ToArray();
            var result = action.Kind == LegalActionKind.UseEquipmentEffect &&
                         action.EquipmentKind is { } equipment
                ? SubmitCommand(new UseEquipmentEffectCommand(
                    _snapshot.HumanSeat,
                    equipment,
                    selectedCards,
                    selectedTargets,
                    _snapshot.Revision,
                    prompt.PromptId))
                : action.Kind == LegalActionKind.UseProgramSkill &&
                  action.ProgramSkillId is { } programSkillId &&
                  action.ProgramActivationId is { } programActivationId
                    ? SubmitCommand(new UseProgramSkillCommand(
                        _snapshot.HumanSeat,
                        programSkillId,
                        programActivationId,
                        selectedCards,
                        selectedTargets,
                        _snapshot.Revision,
                        prompt.PromptId)
                    {
                        SkillOwnerSeat = action.ProgramSkillOwnerSeat
                    })
                : throw new InvalidOperationException("The selected special action has no command identity.");
            if (!result.Accepted)
            {
                PromptText = $"主动技能未执行：{result.Error?.Message}";
                Refresh(result.State);
                return;
            }

            _isSelectingActiveSkillCards = false;
            _selectedActiveSkillCardIds.Clear();
            _selectedActiveSkillTargetSeats.Clear();
            _selectedEquipmentEffectKind = null;
            _selectedProgramSkillId = null;
            _selectedProgramActivationId = null;
            _selectedProgramSkillOwnerSeat = null;
            _selectedActiveSkillTargetContract = null;
            _selectedCardId = null;
            _selectedTargetSeat = null;
            _selectedCardTargetSeats.Clear();
            SelectedCardText = "未选择手牌";
            Refresh(result.State);
        });
    }

    private void UseActiveSkill(LegalAction action)
    {
        if (action.Kind is not (LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill) ||
            action.EquipmentKind is null &&
            (action.ProgramSkillId is null || action.ProgramActivationId is null))
        {
            return;
        }
        _selectedEquipmentEffectKind = action.EquipmentKind;
        _selectedProgramSkillId = action.ProgramSkillId;
        _selectedProgramActivationId = action.ProgramActivationId;
        _selectedProgramSkillOwnerSeat = action.ProgramSkillOwnerSeat;
        _selectedActiveSkillTargetContract = action.TargetSeats.ToArray();
        UseActiveSkill();
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
            _selectedEquipmentEffectKind = null;
            _selectedProgramSkillId = null;
            _selectedProgramActivationId = null;
            _selectedProgramSkillOwnerSeat = null;
            _selectedActiveSkillTargetContract = null;
            _isSelectingActiveSkillCards = false;
            SelectedCardText = "未选择手牌";
            var result = SubmitCommand(new EndPlayPhaseCommand(_snapshot.HumanSeat, _snapshot.Revision, _snapshot.PendingDecision?.PromptId));
            Refresh(result.State);
        });
    }

    private void RespondToSlash(bool useDodge)
    {
        if (_snapshot.PendingDecision is not { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } prompt) return;
        var isFactionDefenseProvider = prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("response") is "faction-defense-dodge" or "faction-defense-bagua");
        var isFactionSlashProvider = prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash");
        var expected = isFactionDefenseProvider
            ? useDodge ? "faction-defense-dodge" : "faction-defense-decline"
            : isFactionSlashProvider
                ? useDodge ? "faction-slash-slash" : "faction-slash-decline"
            : useDodge ? prompt.Kind == DecisionKind.RespondSlash ? "slash" : "dodge" : "take-damage";
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
            if (pending.SkillPrompt is { } skillPrompt)
            {
                EventStack.Add($"      Skill({skillPrompt.Name}, id: {skillPrompt.SkillId})");
                EventStack.Add($"        AskForActivation({pending.Kind})");
            }
            else if (pending.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "faction-defense-request" or "faction-defense-dodge" or "faction-defense-bagua"))
            {
                EventStack.Add("      Skill(FactionDefense)");
                EventStack.Add(pending.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "faction-defense-request")
                    ? "        AskLordForFactionDefense()"
                    : $"        AskWeiForDodge(owner: seat {pending.TargetSeat.GetValueOrDefault() + 1})");
            }
            else if (pending.Choices.Any(choice =>
                         choice.Parameters.GetValueOrDefault("response") is "faction-slash-request" or "faction-slash-slash"))
            {
                EventStack.Add("      Skill(FactionSlash)");
                EventStack.Add(pending.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "faction-slash-request")
                    ? "        AskLordForFactionSlash()"
                    : $"        AskShuForSlash(owner: seat {pending.TargetSeat.GetValueOrDefault() + 1})");
            }
            else if (pending.Kind == DecisionKind.SelectHarvestCard)
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
            else if (pending.Kind == DecisionKind.SkipDiscardPolicy)
            {
                EventStack.Add("      Phase(Discard)");
                EventStack.Add("        AskForSkill(Keji)");
            }
            else if (pending.Kind == DecisionKind.Yingbo)
            {
                EventStack.Add("      ResolveCard");
                EventStack.Add("        AskForSkill(YingboGift)");
            }
            else if (pending.Kind == DecisionKind.CixiongDoubleSwords)
            {
                EventStack.Add("      UseCard(Slash)");
                EventStack.Add("        AskForEquipment(CixiongDoubleSwords)");
            }
            else if (pending.Kind == DecisionKind.QinglongCrescentBlade)
            {
                EventStack.Add("      UseCard(Slash)");
                EventStack.Add("        AskForEquipment(QinglongCrescentBlade)");
            }
            else if (pending.Kind == DecisionKind.IceSword)
            {
                EventStack.Add("      Damage(Slash)");
                EventStack.Add("        AskForEquipment(IceSword)");
            }
            else if (pending.Kind == DecisionKind.QilinBow)
            {
                EventStack.Add("      Damage(Slash)");
                EventStack.Add("        AskForEquipment(QilinBow)");
            }
            else if (pending.Kind == DecisionKind.ZhuqueFan)
            {
                EventStack.Add("      FactionSlash(Slash)");
                EventStack.Add("        AskForEquipment(ZhuqueFan)");
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

    private static string GetVisibleSkillDescription(ContentSkillDefinition skill) =>
        skill.ImplementationStatus == SkillImplementationStatus.Complete
            ? skill.Description
            : $"{skill.Description}\n\n【{(skill.ImplementationStatus == SkillImplementationStatus.Partial ? "部分已实现" : "待实现")}】{skill.PendingImplementation}";

    private string GetKingdom(string generalId) =>
        _contentRegistry.Generals.TryGetValue(generalId, out var general)
            ? general.FactionId == "god" ? "神" : FactionName(general.FactionId)
            : GetLegacyKingdom(generalId);

    private static string GetLegacyKingdom(string generalId) => generalId switch
    {
        _ when generalId.StartsWith("classic:", StringComparison.Ordinal) => generalId switch
        {
            "classic:liu-bei" or "classic:guan-yu" or "classic:zhang-fei" or "classic:zhao-yun" or
                "classic:zhuge-liang" or "classic:huang-yueying" or "classic:ma-chao" or "classic:huang-zhong" or "classic:wei-yan" => "蜀",
            "classic:sun-quan" or "classic:zhou-yu" or "classic:huang-gai" or "classic:gan-ning" or "classic:lu-meng" => "吴",
            "classic:cao-cao" or "classic:sima-yi" or "classic:xiahou-dun" or "classic:guo-jia" or "classic:zhang-liao" or "classic:xu-chu" or "classic:dian-wei" or "classic:xu-huang" or "classic:zhen-ji" => "魏",
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
        Suit.None => string.Empty,
        _ => "?"
    };

}
