using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private DispatcherTimer? _advanceTimer;
    private bool _isAutoAdvance;
    private bool _isLogOpen;
    private bool _isHelpOpen;
    private string _actionHint = "选择一位武将，加入对局";
    private string _recentEventText = "八人入席，静候开局";

    public ObservableCollection<SeatViewModel> TopSeats { get; } = [];
    public ObservableCollection<TablePlayViewModel> RecentPlays { get; } = [];
    public SeatViewModel? HumanPlayer => Seats.FirstOrDefault(seat => seat.IsHuman);
    public SeatViewModel? LeftPlayer => Seats.FirstOrDefault(seat => seat.Seat == 1);
    public SeatViewModel? RightPlayer => Seats.LastOrDefault(seat => !seat.IsHuman);
    public string HandCountText => $"手牌  {Hand.Count:00}";
    public string AliveText => IsTeamSnapshot
        ? $"青队 {Seats.Count(seat => seat.IsAlive && seat.TeamId == "team:blue")} · 赤队 {Seats.Count(seat => seat.IsAlive && seat.TeamId == "team:red")} 存活"
        : $"{Seats.Count(seat => seat.IsAlive)} / {Seats.Count} 人存活";
    public string PlayButtonText => IsDiscardSelectionPending ? $"弃置 {SelectedDiscardCount} / {RequiredDiscardCount} 张"
        : IsHandResponsePending ? HandResponseButtonText
        : IsActiveSkillSelectionPending ? $"发动{HumanActiveSkillName}"
        : !CanPlaySelected && CanPlaySelectedAsSlash ? "当作杀使用"
        : Hand.FirstOrDefault(card => card.IsSelected) is { } card ? $"使用 {card.Name}" : "出 牌";
    public IReadOnlyList<LegalAction> HumanActiveSkillActions => _snapshot is not null &&
        _snapshot.PendingDecision?.Kind == DecisionKind.PlayCard
            ? _game.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.UseSkill).ToArray()
            : [];
    public IReadOnlyList<LegalAction> AdditionalActiveSkillActions => HumanActiveSkillActions.Skip(1).ToArray();
    private LegalAction? HumanActiveSkillAction => HumanActiveSkillActions.FirstOrDefault(action =>
        action.Skill == _selectedActiveSkillKind) ?? HumanActiveSkillActions.FirstOrDefault();
    private bool IsActiveSkillCardSelectionPending =>
        _isSelectingActiveSkillCards &&
        _snapshot?.PendingDecision?.Kind == DecisionKind.PlayCard &&
        HumanActiveSkillAction is { MaxCardCount: > 0 };
    private bool IsActiveSkillTargetSelectionPending =>
        _isSelectingActiveSkillCards &&
        _snapshot?.PendingDecision?.Kind == DecisionKind.PlayCard &&
        HumanActiveSkillAction is { MaxTargetCount: > 0 };
    public bool IsActiveSkillSelectionPending =>
        IsActiveSkillCardSelectionPending || IsActiveSkillTargetSelectionPending;
    public bool ShowActiveSkillEntry => CanUseActiveSkill && !IsActiveSkillSelectionPending;
    public string ActiveSkillEntryText => HumanActiveSkillAction?.Description ?? "发动技能";
    private string HumanActiveSkillName => HumanActiveSkillAction?.Skill is { } skill
        ? SkillRegistry.Get(skill).Name
        : "技能";
    public bool CanConfirmActiveSkill => IsActiveSkillSelectionPending && HumanActiveSkillAction is { } action &&
        _selectedActiveSkillCardIds.Count >= action.MinCardCount && _selectedActiveSkillCardIds.Count <= action.MaxCardCount &&
        _selectedActiveSkillTargetSeats.Count >= action.MinTargetCount && _selectedActiveSkillTargetSeats.Count <= action.MaxTargetCount &&
        _selectedActiveSkillCardIds.All(id => action.SelectableCardIds.Contains(id)) &&
        _selectedActiveSkillTargetSeats.All(seat => action.SelectableTargetSeats.Contains(seat));
    public bool CanUseActiveSkill => HumanActiveSkillAction is not null;
    public string ActiveSkillButtonText
    {
        get
        {
            var action = HumanActiveSkillAction;
            if (action is null) return "发动技能";
            var requiresSelection = action.MinCardCount > 0 || action.MaxCardCount > 0 ||
                                    action.MinTargetCount > 0 || action.MaxTargetCount > 0;
            if (!requiresSelection) return action.Description;
            if (!IsActiveSkillSelectionPending)
            {
                var selectionLabel = action.MaxCardCount > 0 && action.MaxTargetCount > 0
                    ? "手牌和目标"
                    : action.MaxCardCount > 0
                        ? "牌"
                        : "目标";
                return $"选择{selectionLabel}后{action.Description}";
            }
            return _selectedActiveSkillCardIds.Count == 0 && _selectedActiveSkillTargetSeats.Count == 0
                ? $"取消选择【{HumanActiveSkillName}】"
                : $"{action.Description}（已选{BuildActiveSkillSelectionSummary()}）";
        }
    }
    public bool CanConfirmSelected => IsDiscardSelectionPending ? SelectedDiscardCount == RequiredDiscardCount && RequiredDiscardCount > 0
        : IsHandResponsePending ? SelectedHandResponse is not null && Hand.Any(card => card.Id == _selectedCardId && card.IsPlayable)
        : IsActiveSkillSelectionPending ? CanConfirmActiveSkill : CanPlaySelected || CanPlaySelectedAsSlash;
    public bool CanActFromHand => CanEndTurn || IsDiscardSelectionPending || IsHandResponsePending;
    public bool HasAlternateSlash => CanPlaySelected && CanPlaySelectedAsSlash;
    public string TurnHeadline => HasGameOver ? GameOverText : IsGeneralSelectionPending ? "点将出征" : IsDiscardSelectionPending ? "你的弃牌阶段" : CanEndTurn ? "你的出牌阶段" : CanStepAi ? $"{CenterTitle} 正在行动" : "等待你的响应";
    public bool HasChoicePrompt => IsDyingSelectionPending || IsHarvestSelectionPending || IsTargetCardSelectionPending || IsFireAttackSelectionPending || IsNullificationSelectionPending || IsResponseSelectionPending || IsSkillSelectionPending;
    public bool HasCenterChoices => HasChoicePrompt || HasPublicTargetChoices || HasTargetCombinationChoices ||
        HasPublicRevealedCards || ActiveSkillEquipmentChoices.Count > 0;
    public bool IsTableIdle => !HasCenterChoices;
    public bool HasSelection => _selectedCardId.HasValue || _discardCardIds.Count > 0 ||
        _isSelectingActiveSkillCards || _selectedActiveSkillCardIds.Count > 0 ||
        _selectedActiveSkillTargetSeats.Count > 0;
    public bool IsDrawPhase => _snapshot?.Phase == TurnPhase.Draw;
    public bool IsPlayPhase => _snapshot?.Phase == TurnPhase.Play;
    public bool IsDiscardPhase => _snapshot?.Phase == TurnPhase.Discard;
    public bool IsFinishedPhase => _snapshot?.Phase == TurnPhase.Finished;
    public string ActionHint { get => _actionHint; private set => SetProperty(ref _actionHint, value); }
    public string RecentEventText { get => _recentEventText; private set => SetProperty(ref _recentEventText, value); }
    public bool IsLogOpen { get => _isLogOpen; set => SetProperty(ref _isLogOpen, value); }
    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set { if (SetProperty(ref _isHelpOpen, value) && value) RefreshPlayerGuide(); }
    }

    public bool IsAutoAdvance
    {
        get => _isAutoAdvance;
        set
        {
            if (!SetProperty(ref _isAutoAdvance, value)) return;
            if (value) _advanceTimer?.Start(); else _advanceTimer?.Stop();
            RefreshPlaybackPresentation();
            if (_snapshot is not null) RefreshSelectionHint();
            QueueAutoSave();
        }
    }

    public ICommand ClearSelectionCommand { get; private set; } = null!;
    public ICommand ConfirmSelectedCommand { get; private set; } = null!;
    public ICommand SelectRevealedCardCommand { get; private set; } = null!;
    public ICommand SortHandCommand { get; private set; } = null!;
    public ICommand ToggleLogCommand { get; private set; } = null!;
    public ICommand ToggleHelpCommand { get; private set; } = null!;

    private void InitializePresentation(bool autoAdvance)
    {
        InitializePlayback();
        RecastSelectedCommand = new RelayCommand(RecastSelected, () => CanRecastSelected);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        SelectRevealedCardCommand = new RelayCommand<CardViewModel>(card =>
        {
            var choice = HarvestChoices.FirstOrDefault(option => option.Cards.Contains(card.Id));
            if (choice is not null) SelectHarvestChoice(choice);
        }, card => IsHarvestSelectionPending && HarvestChoices.Any(option => option.Cards.Contains(card.Id)));
        ConfirmSelectedCommand = new RelayCommand(() =>
        {
            if (!CanConfirmSelected) return;
            if (IsDiscardSelectionPending) ConfirmDiscard();
            else if (IsHandResponsePending) ConfirmHandResponse();
            else if (IsActiveSkillSelectionPending) UseActiveSkill();
            else if (CanPlaySelected) PlaySelectedCard();
            else if (CanPlaySelectedAsSlash) PlaySelectedAsSlash();
        }, () => CanConfirmSelected);
        SortHandCommand = new RelayCommand(SortHand);
        ToggleLogCommand = new RelayCommand(() => { if (!IsTutorialActive) IsLogOpen = !IsLogOpen; }, () => !IsTutorialActive);
        ToggleHelpCommand = new RelayCommand(() =>
        {
            if (IsHelpOpen) IsHelpOpen = false;
            else OpenContextGuideCommand.Execute(null);
        });
        _advanceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(SelectedPlaybackSpeed.IntervalMilliseconds)
        };
        _advanceTimer.Tick += OnAdvanceTick;
        IsAutoAdvance = autoAdvance;
    }

    private void OnAdvanceTick(object? sender, EventArgs args)
    {
        // One committed step per tick keeps WPF responsive and pauses at every human decision.
        if (IsAutoAdvance && CanStepAi && !IsHelpOpen && !IsNewGameSetupOpen && !IsHistoryOpen) StepAi();
    }

    private void ResetPresentation()
    {
        ResetBattleFeedback();
        RecentPlays.Clear();
        RecentEventText = "八人入席，静候开局";
    }

    private void RefreshPresentation()
    {
        RefreshPlaybackPresentation();
        TopSeats.Clear();
        foreach (var seat in Seats.Where(seat => seat.Seat >= 2 && seat.Seat < Seats.Count - 1)) TopSeats.Add(seat);
        foreach (var name in new[] { nameof(HumanPlayer), nameof(LeftPlayer), nameof(RightPlayer), nameof(HandCountText), nameof(AliveText), nameof(TurnHeadline), nameof(HasChoicePrompt), nameof(HasCenterChoices), nameof(IsTableIdle), nameof(IsDrawPhase), nameof(IsPlayPhase), nameof(IsDiscardPhase), nameof(IsFinishedPhase), nameof(CanUseActiveSkill), nameof(HumanActiveSkillActions), nameof(AdditionalActiveSkillActions), nameof(ActiveSkillButtonText) })
            RaisePropertyChanged(name);
        RefreshSelectionHint();
        RefreshGameSetupPresentation();
        RefreshDiscardPresentation();
        ((RelayCommand<CardViewModel>)SelectRevealedCardCommand).NotifyCanExecuteChanged();
    }

    private void RefreshSelectionHint()
    {
        if (_snapshot is null) return;
        var card = Hand.FirstOrDefault(item => item.Id == _selectedCardId);
        var target = Seats.FirstOrDefault(item => item.Seat == _selectedTargetSeat);
        ActionHint = HasGameOver ? "对局结束 · 点击「新对局」再战一局"
            : IsSpectating ? SpectatorHint
            : IsDiscardSelectionPending ? $"手牌上限 {HumanPlayer?.Hp} · 请弃置 {RequiredDiscardCount} 张，已选 {SelectedDiscardCount} 张。再次点击可取消。"
            : IsHandResponsePending ? HandResponseHint
            : IsActiveSkillSelectionPending ? GetActiveSkillSelectionHint()
            : IsMultiTargetCardSelected ? MultiTargetSelectionHint
            : card is not null ? target is not null ? CanConfirmSelected
                    ? $"{(!CanPlaySelected && CanPlaySelectedAsSlash ? $"{card.Name}当作杀" : card.Name)} → {target.GeneralName} · 确认后使用"
                    : $"{card.Name} → {target.GeneralName} · 在中央选择具体目标牌"
                : CanConfirmSelected ? $"已选择【{card.Name}】· 点击出牌确认" : $"已选择【{card.Name}】· 点击亮起的武将选择目标"
            : IsGeneralSelectionPending ? "选择你的武将，准备出征"
            : CanUseActiveSkill && HumanActiveSkillAction is { } activeAction &&
              (activeAction.MinCardCount > 0 || activeAction.MaxCardCount > 0 ||
               activeAction.MinTargetCount > 0 || activeAction.MaxTargetCount > 0)
                ? $"点击{ActiveSkillEntryText}，再选择{BuildActiveSkillRequirement(activeAction)}"
            : CanUseActiveSkill ? Hand.Any(item => item.IsPlayable)
                ? $"选择一张手牌，或发动 {ActiveSkillButtonText}"
                : $"可发动 {ActiveSkillButtonText}"
            : CanEndTurn ? Hand.Any(item => item.IsPlayable) ? "选择一张手牌，再选择目标" : "当前没有可使用的手牌 · 可以结束出牌"
            : HasChoicePrompt ? _snapshot.PendingDecision!.Prompt
            : CanStepAi ? IsAutoAdvance ? "其他武将正在行动，请稍候…" : "自动推进已暂停 · 可继续推进或观察单步"
            : PromptText;
        RaisePropertyChanged(nameof(PlayButtonText));
        RaisePropertyChanged(nameof(IsMultiTargetCardSelected));
        RaisePropertyChanged(nameof(ShowRecastAction));
        RaisePropertyChanged(nameof(CanRecastSelected));
        ((RelayCommand)RecastSelectedCommand).NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(IsHandResponsePending));
        RaisePropertyChanged(nameof(CanActFromHand));
        RaisePropertyChanged(nameof(ActiveSkillButtonText));
        RaisePropertyChanged(nameof(ActiveSkillEntryText));
        RaisePropertyChanged(nameof(ShowActiveSkillEntry));
        RaisePropertyChanged(nameof(IsActiveSkillSelectionPending));
        RaisePropertyChanged(nameof(CanConfirmActiveSkill));
        RaisePropertyChanged(nameof(CanConfirmSelected));
        RaisePropertyChanged(nameof(HasAlternateSlash));
        ((RelayCommand)ConfirmSelectedCommand).NotifyCanExecuteChanged();
        ((RelayCommand)UseActiveSkillCommand).NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(HasCenterChoices));
        RaisePropertyChanged(nameof(IsTableIdle));
        RefreshPlayerGuide();
    }

    private void ClearSelection()
    {
        var wasSelectingActiveSkillCards = _isSelectingActiveSkillCards;
        _selectedCardId = null;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();
        _discardCardIds.Clear();
        _selectedActiveSkillCardIds.Clear();
        _selectedActiveSkillTargetSeats.Clear();
        _selectedActiveSkillKind = null;
        _isSelectingActiveSkillCards = false;
        foreach (var card in Hand) card.IsSelected = false;
        SelectedCardText = "未选择手牌";
        if (wasSelectingActiveSkillCards)
        {
            RefreshCurrentView();
            return;
        }
        RebuildPublicTargetChoices();
        RebuildTargetCombinationChoices();
        RefreshTargetHighlights();
        RefreshDiscardPresentation();
    }

    private string BuildActiveSkillSelectionSummary()
    {
        var action = HumanActiveSkillAction;
        if (action is null) return string.Empty;

        var parts = new List<string>();
        if (action.MaxCardCount > 0)
        {
            parts.Add($" {_selectedActiveSkillCardIds.Count} 张牌");
        }

        if (action.MaxTargetCount > 0)
        {
            parts.Add($" {_selectedActiveSkillTargetSeats.Count} 个目标");
        }

        return string.Join("、", parts);
    }

    private static string BuildActiveSkillRequirement(LegalAction action)
    {
        var parts = new List<string>();
        if (action.MaxCardCount > 0)
        {
            parts.Add($"{FormatSelectionRange(action.MinCardCount, action.MaxCardCount)} 张牌");
        }

        if (action.MaxTargetCount > 0)
        {
            parts.Add($"{FormatSelectionRange(action.MinTargetCount, action.MaxTargetCount)} 个目标");
        }

        return string.Join("、", parts);
    }

    private string GetActiveSkillSelectionHint()
    {
        var skillName = _snapshot.Players.Single(player => player.IsHuman).SkillName;
        var action = HumanActiveSkillAction;
        if (action is null) return "正在选择主动技能参数。";

        var parts = new List<string>();
        if (action.MaxCardCount > 0)
        {
            parts.Add($"牌 {_selectedActiveSkillCardIds.Count}/{FormatSelectionRange(action.MinCardCount, action.MaxCardCount)}");
        }

        if (action.MaxTargetCount > 0)
        {
            parts.Add($"目标 {_selectedActiveSkillTargetSeats.Count}/{FormatSelectionRange(action.MinTargetCount, action.MaxTargetCount)}");
        }

        var next = CanConfirmActiveSkill ? $"点击「发动{skillName}」或按 Enter 确认。" : "选够牌和目标后即可确认；Esc 取消。";
        return $"【{skillName}】{string.Join(" · ", parts)} · {next}";
    }

    private static string FormatSelectionRange(int minimum, int maximum) => minimum == maximum ? minimum.ToString() : $"{minimum}–{maximum}";

    private void SortHand()
    {
        var ordered = Hand.OrderBy(card => card.KindLabel).ThenBy(card => card.Name).ThenBy(card => card.SuitGlyph).ThenBy(card => card.Rank).ToArray();
        for (var i = 0; i < ordered.Length; i++) Hand.Move(Hand.IndexOf(ordered[i]), i);
    }

    private void RecordPublicActivity(GameLogEntry entry)
    {
        if (entry.Type is "Rules" or "GeneralSelected") return;
        RecentEventText = entry.Message;
    }

    public void Dispose()
    {
        if (_disposed) return;
        FlushPreferences();
        if (_preferencesTimer is not null)
        {
            _preferencesTimer.Stop();
            _preferencesTimer.Tick -= OnPreferencesTick;
        }
        FlushMatchHistory();
        _disposed = true;
        SoundsReset?.Invoke(this, EventArgs.Empty);
        BattleCues.Clear();
        FlushPendingSave();
        if (_saveTimer is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTick;
        }
        if (_advanceTimer is not null)
        {
            _advanceTimer.Stop();
            _advanceTimer.Tick -= OnAdvanceTick;
        }
        DetachEngine();
    }
}

public sealed record TablePlayViewModel(long Sequence, string Name, string ActorName)
{
    public string VerticalName => string.Join("\n", Name.ToCharArray());
    public double NameSize => Name.Length > 3 ? 18 : Name.Length == 1 ? 33 : 23;
}
