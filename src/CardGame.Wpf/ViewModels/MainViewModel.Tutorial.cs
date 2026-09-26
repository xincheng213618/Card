using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Training;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private SuspendedMatch? _suspendedMatch;
    private int _tutorialIndex;
    private bool _tutorialStepComplete;
    private bool _tutorialAwaitingEffect;
    private string _tutorialFeedback = string.Empty;
    private string _tutorialEntryError = string.Empty;

    public bool IsTutorialActive => _suspendedMatch is not null;
    public bool IsTutorialStepComplete => _tutorialStepComplete;
    public bool IsTutorialCourseComplete => IsTutorialActive && _tutorialStepComplete && _tutorialIndex == TutorialScenario.Lessons.Count - 1;
    public bool IsTutorialBoardEnabled => !IsTutorialActive || !_tutorialStepComplete;
    public string TutorialProgress => $"{_tutorialIndex + 1:00} / {TutorialScenario.Lessons.Count:00}";
    public string TutorialTitle => IsTutorialCourseComplete ? "新手演练完成" : CurrentTutorialLesson.Title;
    public string TutorialInstruction => _tutorialStepComplete ? CurrentTutorialLesson.Success :
        _tutorialFeedback.Length > 0 ? _tutorialFeedback : CurrentTutorialLesson.Instruction;
    public string TutorialNextText => IsTutorialCourseComplete ? TutorialExitText : "下一节";
    public string TutorialExitText => _suspendedMatch?.SetupOpen == true ? "返回开局" : "返回原牌局";
    public string TutorialEntryError { get => _tutorialEntryError; private set { if (SetProperty(ref _tutorialEntryError, value)) RaisePropertyChanged(nameof(HasTutorialEntryError)); } }
    public bool HasTutorialEntryError => TutorialEntryError.Length > 0;
    public string SessionSaveStatus => IsTutorialActive ? "新手演练 · 正式牌局已保留 · 可随时返回" : SaveStatus;
    private TutorialLesson CurrentTutorialLesson => TutorialScenario.Lessons[_tutorialIndex];

    public ICommand StartTutorialCommand { get; private set; } = null!;
    public ICommand RestartTutorialStepCommand { get; private set; } = null!;
    public ICommand NextTutorialStepCommand { get; private set; } = null!;
    public ICommand ExitTutorialCommand { get; private set; } = null!;

    private void InitializeTutorial()
    {
        StartTutorialCommand = new RelayCommand(StartTutorial, () => !IsTutorialActive);
        RestartTutorialStepCommand = new RelayCommand(() => { if (IsTutorialActive) PrepareTutorialStep(_tutorialIndex); }, () => IsTutorialActive);
        NextTutorialStepCommand = new RelayCommand(() =>
        {
            if (!IsTutorialActive || !_tutorialStepComplete) return;
            if (IsTutorialCourseComplete) ExitTutorial(); else PrepareTutorialStep(_tutorialIndex + 1);
        }, () => IsTutorialActive && _tutorialStepComplete);
        ExitTutorialCommand = new RelayCommand(ExitTutorial, () => IsTutorialActive);
    }

    private void StartTutorial()
    {
        if (IsTutorialActive) return;
        // Prepare independently first. A failed setup must leave the current game untouched.
        var practice = TryPrepareTutorialStep(0);
        if (practice is null) return;
        if (!FlushPendingSave())
        {
            TutorialEntryError = "当前牌局保存失败，暂未进入教学；请先处理存档问题后重试。";
            ActionHint = TutorialEntryError;
            return;
        }
        _suspendedMatch = new SuspendedMatch(_game, IsAutoAdvance, IsNewGameSetupOpen, IsLogOpen,
            IsDeveloperView, _selectedCardId, _selectedTargetSeat, _discardCardIds.ToArray(), Hand.Select(card => card.Id).ToArray(), SelectedCardText,
            _isSelectingActiveSkillCards, _activeSkillPromptId, _selectedActiveSkillCardIds.ToArray(), _selectedActiveSkillTargetSeats.ToArray(), _selectedCardTargetSeats.ToArray())
        {
            ConversionSource = _selectedConversionSource,
            EquipmentEffectKind = _selectedEquipmentEffectKind,
            ProgramSkillId = _selectedProgramSkillId,
            ProgramActivationId = _selectedProgramActivationId,
            ProgramSkillOwnerSeat = _selectedProgramSkillOwnerSeat
        };
        _saveTimer?.Stop();
        _isDeveloperView = false;
        RaisePropertyChanged(nameof(IsDeveloperView));
        IsNewGameSetupOpen = false;
        IsLogOpen = false;
        IsHelpOpen = false;
        ActivateTutorialStep(0, practice);
    }

    private GameEngine? TryPrepareTutorialStep(int index)
    {
        try { return TutorialScenario.Create(TutorialScenario.Lessons[index]); }
        catch (InvalidOperationException)
        {
            TutorialEntryError = "教学暂时未能准备，可以重试或返回原牌局。";
            return null;
        }
    }

    private void PrepareTutorialStep(int index)
    {
        var practice = TryPrepareTutorialStep(index);
        if (practice is not null) ActivateTutorialStep(index, practice);
    }

    private void ActivateTutorialStep(int index, GameEngine practice)
    {
        TutorialEntryError = string.Empty;
        _tutorialIndex = index;
        _tutorialStepComplete = false;
        _tutorialAwaitingEffect = false;
        _tutorialFeedback = string.Empty;
        IsHelpOpen = false;
        IsLogOpen = false;
        ReplaceEngine(practice);
        IsAutoAdvance = true;
        RefreshTutorialPresentation();
    }

    private void ExitTutorial()
    {
        if (_suspendedMatch is not { } original) return;
        _tutorialStepComplete = false;
        _tutorialAwaitingEffect = false;
        _tutorialFeedback = string.Empty;
        TutorialEntryError = string.Empty;
        IsHelpOpen = false;
        ReplaceEngine(original.Game);
        _isDeveloperView = original.DeveloperView;
        RaisePropertyChanged(nameof(IsDeveloperView));
        IsAutoAdvance = original.AutoAdvance;
        for (var index = 0; index < original.HandOrder.Length; index++)
        {
            var card = Hand.SingleOrDefault(card => card.Id == original.HandOrder[index]);
            if (card is not null) Hand.Move(Hand.IndexOf(card), index);
        }
        _selectedCardId = original.CardId;
        _selectedConversionSource = original.ConversionSource;
        _selectedTargetSeat = original.TargetSeat;
        _selectedCardTargetSeats.UnionWith(original.CardTargets);
        _discardCardIds.UnionWith(original.DiscardIds);
        _isSelectingActiveSkillCards = original.SelectingActiveSkill;
        _activeSkillPromptId = original.ActiveSkillPromptId;
        _selectedEquipmentEffectKind = original.EquipmentEffectKind;
        _selectedProgramSkillId = original.ProgramSkillId;
        _selectedProgramActivationId = original.ProgramActivationId;
        _selectedProgramSkillOwnerSeat = original.ProgramSkillOwnerSeat;
        _selectedActiveSkillCardIds.UnionWith(original.ActiveSkillCardIds);
        _selectedActiveSkillTargetSeats.AddRange(original.ActiveSkillTargets.Distinct());
        SelectedCardText = original.SelectedCardText;
        RefreshCurrentView();
        IsNewGameSetupOpen = original.SetupOpen;
        IsLogOpen = original.LogOpen;
        _suspendedMatch = null;
        RefreshCurrentView();
        RefreshTutorialPresentation();
    }

    private bool AllowsTutorialCommand(GameCommand command)
    {
        if (!IsTutorialActive) return true;
        if (_tutorialStepComplete) return false;
        if (command.ActorSeat == -1) return _tutorialAwaitingEffect;
        var human = _snapshot.Players.Single(player => player.IsHuman);
        bool Uses(CardKind kind) => command is PlayCardCommand play &&
            (play.PlayedCardKind is null || play.PlayedCardKind == kind) && human.Hand.Any(card => card.Id == play.CardId && card.Kind == kind);
        var answerId = command switch { AnswerPromptCommand answer => answer.Choice, _ => (ChoiceId?)null };
        var answerChoice = _snapshot.PendingDecision?.Choices.SingleOrDefault(choice => choice.Id == answerId);
        var allowed = CurrentTutorialLesson.Action switch
        {
            TutorialAction.Attack => Uses(CardKind.Slash),
            TutorialAction.Recover => Uses(CardKind.Peach),
            TutorialAction.Dodge => answerChoice?.Cards.Count == 1 && human.Hand.Any(card => card.Id == answerChoice.Cards[0] && card.Kind == CardKind.Dodge),
            TutorialAction.Discard => command is DiscardCardsCommand,
            _ => false
        };
        if (!allowed)
        {
            _tutorialFeedback = $"这一节先练习「{CurrentTutorialLesson.Title}」。{CurrentTutorialLesson.Instruction}";
            RefreshTutorialPresentation();
        }
        return allowed;
    }

    private void ObserveTutorialCommand(GameCommand command, int firstEvent)
    {
        if (!IsTutorialActive || _tutorialStepComplete) return;
        if (command.ActorSeat == _snapshot.HumanSeat) _tutorialAwaitingEffect = true;
        var events = _game.Events.Skip(firstEvent).Select(envelope => envelope.Payload);
        _tutorialStepComplete = events.Any(payload => CurrentTutorialLesson.Action switch
        {
            TutorialAction.Attack => payload is CardUseDeclaredEvent { SourceSeat: 0, CardKind: CardKind.Slash },
            TutorialAction.Dodge => payload is CardRespondedEvent { ResponderSeat: 0, EffectiveCardKind: CardKind.Dodge },
            TutorialAction.Recover => payload is RecoveryAppliedEvent { SourceSeat: 0, TargetSeat: 0, Amount: 1 },
            TutorialAction.Discard => payload is HandLimitDiscardedEvent { ActorSeat: 0 },
            _ => false
        });
        _tutorialFeedback = _tutorialAwaitingEffect && !_tutorialStepComplete ? "已提交，正在结算这次操作…" : string.Empty;
        RefreshTutorialPresentation();
    }

    private bool TutorialAllowsHandCard(CardSnapshot card, bool normallyPlayable)
    {
        if (!IsTutorialActive) return normallyPlayable;
        if (_tutorialStepComplete) return false;
        return CurrentTutorialLesson.Action switch
        {
            TutorialAction.Attack => normallyPlayable && card.Kind == CardKind.Slash,
            TutorialAction.Recover => normallyPlayable && card.Kind == CardKind.Peach,
            TutorialAction.Dodge => normallyPlayable && card.Kind == CardKind.Dodge,
            TutorialAction.Discard => normallyPlayable,
            _ => false
        };
    }

    private string TutorialHandAvailability(CardSnapshot card, bool normallyPlayable, string normalMessage)
    {
        if (!IsTutorialActive || TutorialAllowsHandCard(card, normallyPlayable)) return normalMessage;
        if (_tutorialStepComplete) return "本节已经完成，可进入下一节或重来。";
        return CurrentTutorialLesson.Action switch
        {
            TutorialAction.Attack => "本节只练习实体【杀】；其他牌留到正式对局。",
            TutorialAction.Recover => "本节只练习【桃】；其他牌留到正式对局。",
            TutorialAction.Dodge => "本节请选择亮起的【闪】，再确认响应。",
            _ => normalMessage
        };
    }

    private void RefreshTutorialPresentation()
    {
        foreach (var property in new[] { nameof(IsTutorialActive), nameof(IsTutorialStepComplete), nameof(IsTutorialCourseComplete),
            nameof(IsTutorialBoardEnabled), nameof(TutorialProgress), nameof(TutorialTitle), nameof(TutorialInstruction),
            nameof(TutorialNextText), nameof(TutorialExitText), nameof(SessionSaveStatus) }) RaisePropertyChanged(property);
        foreach (var command in new[] { StartTutorialCommand, RestartTutorialStepCommand, NextTutorialStepCommand, ExitTutorialCommand,
            SaveGameCommand, LoadManualGameCommand, ContinueGameCommand, NewGameCommand, StartNewGameCommand,
            ToggleLogCommand }) (command as RelayCommand)?.NotifyCanExecuteChanged();
        RefreshGameSetupPresentation();
        RefreshPlayerGuide();
    }

    private sealed record SuspendedMatch(GameEngine Game, bool AutoAdvance, bool SetupOpen, bool LogOpen, bool DeveloperView,
        int? CardId, int? TargetSeat, int[] DiscardIds, int[] HandOrder, string SelectedCardText,
        bool SelectingActiveSkill, PromptId? ActiveSkillPromptId, int[] ActiveSkillCardIds, int[] ActiveSkillTargets, int[] CardTargets)
    {
        public CardConversionSource? ConversionSource { get; init; }
        public CardKind? EquipmentEffectKind { get; init; }
        public string? ProgramSkillId { get; init; }
        public string? ProgramActivationId { get; init; }
        public int? ProgramSkillOwnerSeat { get; init; }
    }
}
