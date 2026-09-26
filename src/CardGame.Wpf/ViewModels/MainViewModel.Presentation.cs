using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private DispatcherTimer? _advanceTimer;
    private bool _isAutomaticAdvanceRunning;
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
    public IReadOnlyList<HumanSkillViewModel> HumanSkillCards
    {
        get
        {
            if (_snapshot is null || _snapshot.Players.SingleOrDefault(player => player.IsHuman) is not { } human)
                return [];

            var availablePrograms = HumanActiveSkillActions
                .Where(action => action.ProgramSkillId is not null)
                .Select(action => action.ProgramSkillId!)
                .ToHashSet(StringComparer.Ordinal);
            if (IsNationalSnapshot)
            {
                var slots = new[]
                {
                    (Label: "主", Id: human.GeneralId, Revealed: human.IsGeneralPublic, Skills: human.Skills),
                    (Label: "副", Id: human.SecondaryGeneralId ?? string.Empty, Revealed: human.IsSecondaryGeneralPublic, Skills: human.SecondarySkills)
                };
                return slots
                    .Where(slot => slot.Id.Length > 0 && _contentRegistry.Generals.ContainsKey(slot.Id))
                    .SelectMany(slot =>
                    {
                        var definition = _contentRegistry.Generals[slot.Id];
                        var printedSkillIds = (definition.SkillIds
);
                        return FilterOwnedSkillIds(printedSkillIds, slot.Skills)
                            .Select(_contentRegistry.GetSkill)
                            .Where(skill => skill.LegacyKind != SkillKind.None || skill.Name != "无")
                            .Select(skill =>
                            {
                                var enabled = (slot.Revealed );
                                var active = skill.ActionForms.HasFlag(SkillActionForm.Active) ||
                                             skill.Program?.Activations.Count > 0;
                                var isAvailable = availablePrograms.Contains(skill.Id);
                                return new HumanSkillViewModel(
                                    skill.Name,
                                    GetVisibleSkillDescription(skill),
                                    GetSkillTypeText(active, skill.Tags, skill.ExecutionForms, skill.ActionForms),
                                    !enabled
                                        ? "暗置中 · 尚未启用"
                                        : GetSkillStateText(active, isAvailable, skill.ExecutionForms, "已启用"),
                                    $"{slot.Label}将 · {(slot.Revealed ? "明置" : "暗置")}",
                                    enabled && isAvailable,
                                    !enabled) { ContentId = skill.Id, LegacyKind = skill.LegacyKind };
                            });
                    })
                    .ToArray();
            }

            if (_contentRegistry.Generals.TryGetValue(human.GeneralId, out var general))
            {
                var ownedSkillIds = human.Skills is null
                    ? general.SkillIds
                    : human.Skills
                        .Select(skill => skill.ContentId)
                        .OfType<string>()
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                return ownedSkillIds
                    .Select(_contentRegistry.GetSkill)
                    .Select(skill =>
                    {
                        var runtimeState = human.SkillRuntimeStates?
                            .SingleOrDefault(state => state.SkillId == skill.Id);
                        var active = skill.ActionForms.HasFlag(SkillActionForm.Active) ||
                                     skill.Program?.Activations.Count > 0;
                        var isAvailable = availablePrograms.Contains(skill.Id);
                        var isFuhunGranted = runtimeState?.IsAcquired == true &&
                            human.SkillRuntimeStates?.Any(state =>
                                state.SkillId == "classic:fuhun" &&
                                state.Usages.Any(usage =>
                                    usage.UsageId.StartsWith("grant-parent-skills@", StringComparison.Ordinal) &&
                                    usage.Scope == SkillUsageScope.Turn &&
                                    usage.Count > 0)) == true;
                        return new HumanSkillViewModel(
                            skill.Name,
                            GetVisibleSkillDescription(skill),
                            GetSkillTypeText(active, skill.Tags, skill.ExecutionForms, skill.ActionForms),
                            GetSkillStateText(
                                active,
                                isAvailable,
                                skill.ExecutionForms,
                                "规则自动生效",
                                skill.Tags,
                                runtimeState),
                            runtimeState?.IsAcquired == true
                                ? isFuhunGranted
                                    ? $"{human.GeneralName} · 父魂获得"
                                    : $"{human.GeneralName} · 觉醒获得"
                                : human.GeneralName,
                            isAvailable,
                            false) { ContentId = skill.Id, LegacyKind = skill.LegacyKind };
                    })
                    .ToArray();
            }

            return (human.Skills ?? [])
                .Where(skill => skill.Kind != SkillKind.None)
                .Select(skill =>
                {
                    var active = skill.ActionForms.HasFlag(SkillActionForm.Active);
                    var available = skill.ContentId is { } id && availablePrograms.Contains(id);
                    return new HumanSkillViewModel(
                        skill.Name,
                        skill.Description,
                        GetSkillTypeText(active, skill.Tags, skill.ExecutionForms, skill.ActionForms),
                        GetSkillStateText(
                            active,
                            available,
                            skill.ExecutionForms,
                            "规则自动生效"),
                        human.GeneralName,
                        available,
                        false) { ContentId = skill.ContentId, LegacyKind = skill.Kind };
                })
                 .ToArray();
        }
    }

    private static IReadOnlyList<string> FilterOwnedSkillIds(
        IEnumerable<string> printedSkillIds,
        IReadOnlyList<GeneralSkillDefinition>? ownedSkills)
    {
        var printed = printedSkillIds.ToArray();
        if (ownedSkills is null) return printed;

        var ownedIds = ownedSkills
            .Select(skill => skill.ContentId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return printed.Where(ownedIds.Contains).ToArray();
    }

    private static string GetSkillTypeText(
        bool hasActiveEntry,
        SkillTag tags,
        SkillExecutionForm executionForms,
        SkillActionForm actionForms)
    {
        if (tags == SkillTag.None &&
            executionForms == SkillExecutionForm.None &&
            actionForms == SkillActionForm.None)
            return hasActiveEntry ? "主动技" : "触发 / 锁定";

        var parts = new List<string>();
        if (actionForms.HasFlag(SkillActionForm.Active) ||
            actionForms == SkillActionForm.None &&
            executionForms == SkillExecutionForm.None &&
            hasActiveEntry)
            parts.Add("主动技");
        if (executionForms.HasFlag(SkillExecutionForm.State)) parts.Add("状态技");
        if (executionForms.HasFlag(SkillExecutionForm.Trigger)) parts.Add("触发技");
        if (tags.HasFlag(SkillTag.Lord)) parts.Add("主公技");
        if (tags.HasFlag(SkillTag.Locked)) parts.Add("锁定技");
        if (tags.HasFlag(SkillTag.Limited)) parts.Add("限定技");
        if (tags.HasFlag(SkillTag.Awakening)) parts.Add("觉醒技");
        if (tags.HasFlag(SkillTag.Conversion)) parts.Add("转换技");
        return string.Join(" · ", parts);
    }

    private string GetSkillStateText(
        bool hasActiveEntry,
        bool isAvailable,
        SkillExecutionForm executionForms,
        string automaticText,
        SkillTag tags = SkillTag.None,
        SkillRuntimeStateSnapshot? runtimeState = null)
    {
        if (runtimeState is not null && GetProgramRuntimeStateText(runtimeState) is { Length: > 0 } programState)
            return programState;
        if (runtimeState?.SkillId == "classic:fuhun" &&
            runtimeState.Usages.Any(usage =>
                usage.UsageId.StartsWith("grant-parent-skills@", StringComparison.Ordinal) &&
                usage.Scope == SkillUsageScope.Turn &&
                usage.Count > 0))
        {
            return "本回合已获得武圣／咆哮";
        }
        if (isAvailable) return "当前可发动";
        if (tags.HasFlag(SkillTag.Awakening))
        {
            return runtimeState?.Usages.Any(usage =>
                usage.UsageId == "awakening" &&
                usage.Scope == SkillUsageScope.Game &&
                usage.Count > 0) == true
                ? "已觉醒"
                : "等待觉醒条件";
        }
        if (tags.HasFlag(SkillTag.Limited))
        {
            return runtimeState?.Usages.Any(usage =>
                usage.Scope == SkillUsageScope.Game &&
                usage.Count > 0) == true
                ? "已发动 · 本局不可再用"
                : executionForms.HasFlag(SkillExecutionForm.Trigger)
                    ? "等待触发时机 · 本局限一次"
                    : "本局限一次";
        }
        if (runtimeState?.Polarity is { } polarity)
            return polarity == SkillPolarity.Yang ? "当前：阳" : "当前：阴";
        if (runtimeState?.SkillId == "mou:hengye")
        {
            var growth = runtimeState.Usages.SingleOrDefault(usage =>
                usage.UsageId == "growth" &&
                usage.Scope == SkillUsageScope.Game)?.Count ?? 0;
            return $"成长 {growth}/3 · {automaticText}";
        }
        if (runtimeState?.SkillId == "classic:jiangchi")
        {
            if (runtimeState.Usages.Any(usage =>
                    usage.UsageId == "draw-more" &&
                    usage.Scope == SkillUsageScope.Turn &&
                    usage.Count > 0))
            {
                return "本回合：额外摸牌 · 禁止杀";
            }
            if (runtimeState.Usages.Any(usage =>
                    usage.UsageId == "assault" &&
                    usage.Scope == SkillUsageScope.Turn &&
                    usage.Count > 0))
            {
                return "本回合：杀次数 +1 · 无距离限制";
            }
        }
        if (runtimeState?.SkillId == "classic:zishou" &&
            runtimeState.Usages.Any(usage =>
                usage.UsageId == "active" &&
                usage.Scope == SkillUsageScope.Turn &&
                usage.Count > 0))
        {
            return "本回合：额外摸牌 · 牌仅指定自己";
        }
        if (hasActiveEntry) return "当前不可发动";
        return executionForms.HasFlag(SkillExecutionForm.Trigger)
            ? "等待触发时机"
            : automaticText;
    }

    private static string GetProgramRuntimeStateText(SkillRuntimeStateSnapshot state)
    {
        var parts = new List<string>();
        parts.AddRange((state.BooleanStates ?? []).Select(item => item.Text).Distinct());
        foreach (var policy in state.DirectedPolicies ?? [])
        {
            var effects = new List<string>();
            if (policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.ForbidTarget)) effects.Add("不能对其用牌");
            if (policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreDistance)) effects.Add("无距");
            if (policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.BypassSlashLimit)) effects.Add("杀不限次");
            if (policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreArmor)) effects.Add("无视防具");
            parts.Add($"本回合：{policy.ActorSeat + 1:D2}→{policy.TargetSeat + 1:D2}号位 · {string.Join(" · ", effects)}");
        }
        foreach (var prohibition in state.ActionProhibitions ?? [])
        {
            var allSlashes = new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash };
            var kinds = prohibition.CardKinds.Order().SequenceEqual(allSlashes.Order())
                ? "杀" : string.Join("／", prohibition.CardKinds.Select(kind => new Card(0, kind, Suit.Spade, 1).DisplayName));
            parts.Add($"本回合：不能{string.Join("／", prohibition.ActionTypes.Select(kind => kind == CardActionType.Use ? "使用" : "打出"))}{kinds}");
        }
        return string.Join(" · ", parts.Distinct());
    }
    public string AliveText => IsTeamSnapshot
        ? $"青队 {Seats.Count(seat => seat.IsAlive && seat.TeamId == "team:blue")} · 赤队 {Seats.Count(seat => seat.IsAlive && seat.TeamId == "team:red")} 存活"
        : $"{Seats.Count(seat => seat.IsAlive)} / {Seats.Count} 人存活";
    public string PlayButtonText => IsDiscardSelectionPending ? $"弃置 {SelectedDiscardCount} / {RequiredDiscardCount} 张"
        : IsHandResponsePending ? HandResponseButtonText
        : IsActiveSkillSelectionPending ? $"发动{HumanActiveSkillName}"
        : !CanPlaySelected && CanPlaySelectedAsSlash ? AlternatePlayText
        : Hand.FirstOrDefault(card => card.IsSelected) is { } card ? $"使用 {card.Name}" : "出 牌";
    public IReadOnlyList<LegalAction> HumanActiveSkillActions
    {
        get
        {
            if (_snapshot?.PendingDecision?.Kind != DecisionKind.PlayCard) return [];
            var skillIds = _snapshot.Players.Single(player => player.IsHuman).Skills?
                .Select(skill => skill.ContentId).ToArray() ?? [];
            return _game.GetHumanLegalActions().Where(action =>
                    action.Kind is LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill)
                .OrderBy(action =>
                {
                    var index = Array.IndexOf(skillIds, action.ProgramSkillId);
                    return index < 0 ? int.MaxValue : index;
                }).ToArray();
        }
    }
    public IReadOnlyList<LegalAction> AdditionalActiveSkillActions => HumanActiveSkillActions.Skip(1).ToArray();
    private LegalAction? HumanActiveSkillAction =>
        _selectedEquipmentEffectKind is not null || _selectedProgramSkillId is not null
            ? HumanActiveSkillActions.FirstOrDefault(action =>
                action.EquipmentKind == _selectedEquipmentEffectKind &&
                action.ProgramSkillId == _selectedProgramSkillId &&
                action.ProgramActivationId == _selectedProgramActivationId &&
                action.ProgramSkillOwnerSeat == _selectedProgramSkillOwnerSeat)
            : HumanActiveSkillActions.FirstOrDefault();
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
    private string HumanActiveSkillName => HumanActiveSkillAction is { } action ? ActiveSkillName(action) : "技能";

    private string ActiveSkillName(LegalAction action) => action.ProgramSkillId is { } programSkillId &&
                                                          _contentRegistry.Skills.TryGetValue(programSkillId, out var programSkill)
        ? programSkill.Name
        : action.EquipmentKind is { } equipment
                ? EquipmentCatalog.Get(equipment).DisplayName
                : "技能";
    public bool CanConfirmActiveSkill => IsActiveSkillSelectionPending && HumanActiveSkillAction is { } action &&
        _selectedActiveSkillCardIds.Count >= action.MinCardCount && _selectedActiveSkillCardIds.Count <= action.MaxCardCount &&
        _selectedActiveSkillTargetSeats.Count >= action.MinTargetCount && _selectedActiveSkillTargetSeats.Count <= action.MaxTargetCount &&
        _selectedActiveSkillCardIds.All(id => action.SelectableCardIds.Contains(id)) &&
        _selectedActiveSkillTargetSeats.All(seat => action.SelectableTargetSeats.Contains(seat)) &&
        (!action.SelectedCardsSameSuit || Hand.Where(card => _selectedActiveSkillCardIds.Contains(card.Id))
            .Select(card => card.SuitGlyph).Distinct(StringComparer.Ordinal).Count() == 1);
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
    public string AlternatePlayText => SelectedConversionAction() is { } conversion
        ? $"当作{CardCatalog.Get(conversion.PlayedCardKind!.Value).DisplayName}使用"
        : "转换使用";
    public string TurnHeadline => HasGameOver ? GameOverText : IsGeneralSelectionPending ? "点将出征" : IsDiscardSelectionPending ? "你的弃牌阶段" : CanEndTurn ? "你的出牌阶段" : CanStepAi ? $"{CenterTitle} 正在行动" : "等待你的响应";
    public bool HasChoicePrompt => IsDyingSelectionPending || IsHarvestSelectionPending || IsTargetCardSelectionPending || IsFireAttackSelectionPending || IsNullificationSelectionPending || IsResponseSelectionPending || IsSkillSelectionPending;
    public bool HasCenterChoices => HasChoicePrompt || HasPublicTargetChoices || HasTargetCombinationChoices ||
        HasPublicRevealedCards || ActiveSkillEquipmentChoices.Count > 0 || EquipmentPlayChoices.Count > 0;
    public bool HasPinnedPublicModuleChoices =>
        HasPublicRevealedCards &&
        _snapshot.PendingDecision is { SkillPrompt: not null, Choices.Count: 2 } &&
        SkillChoices.Count == 2;
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
        InitializeBattleLogFilters();
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
        if (_isAutomaticAdvanceRunning) return;
        if (!CanAutomaticallyAdvance)
        {
            if (_advanceTimer is not null)
                _advanceTimer.Interval = TimeSpan.FromMilliseconds(SelectedPlaybackSpeed.IntervalMilliseconds);
            return;
        }
        _isAutomaticAdvanceRunning = true;
        var started = Stopwatch.GetTimestamp();
        var visibleAction = false;
        var progressed = false;
        try
        {
            // Internal phase/frame continuations have no presentation to read.
            // Drain a bounded amount, stopping at public feedback or human input.
            for (var step = 0; step < 32 && CanAutomaticallyAdvance; step++)
            {
                var revision = _snapshot.Revision;
                var lastCue = BattleCues.LastOrDefault()?.Sequence;
                StepAi();
                progressed = _snapshot.Revision != revision;
                visibleAction = BattleCues.LastOrDefault()?.Sequence != lastCue;
                if (!progressed || visibleAction || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 8)
                    break;
            }
        }
        finally
        {
            _isAutomaticAdvanceRunning = false;
            // Yield to WPF when the work budget expires, without another full
            // reading delay for an invisible continuation.
            if (_advanceTimer is not null)
                _advanceTimer.Interval = TimeSpan.FromMilliseconds(
                    progressed && !visibleAction && CanAutomaticallyAdvance
                        ? 1 : SelectedPlaybackSpeed.IntervalMilliseconds);
        }
    }

    private bool CanAutomaticallyAdvance => IsAutoAdvance && CanStepAi &&
        !IsHelpOpen && !IsNewGameSetupOpen && !IsHistoryOpen && !IsGeneralGalleryOpen &&
        !IsSettingsOpen && !IsIdentityRevealOpen && !IsOpeningDealVisible;

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
        RefreshBattleLogSeatOptions();
        RaisePropertyChanged(nameof(HumanEquipmentSlots));
        foreach (var name in new[] { nameof(HumanPlayer), nameof(HumanSkillCards), nameof(LeftPlayer), nameof(RightPlayer), nameof(HandCountText), nameof(AliveText), nameof(TurnHeadline), nameof(HasChoicePrompt), nameof(HasCenterChoices), nameof(IsTableIdle), nameof(IsDrawPhase), nameof(IsPlayPhase), nameof(IsDiscardPhase), nameof(IsFinishedPhase), nameof(CanUseActiveSkill), nameof(HumanActiveSkillActions), nameof(AdditionalActiveSkillActions), nameof(ActiveSkillButtonText) })
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
        var equipment = _snapshot.Players.SingleOrDefault(player => player.IsHuman)?.Equipment
            .FirstOrDefault(item => item.Id == _selectedCardId);
        var selectedName = card?.Name ?? equipment?.DisplayName;
        var target = Seats.FirstOrDefault(item => item.Seat == _selectedTargetSeat);
        ActionHint = HasGameOver ? "对局结束 · 点击「新对局」再战一局"
            : IsSpectating ? SpectatorHint
            : IsDiscardSelectionPending ? $"手牌上限 {HumanPlayer?.Hp} · 请弃置 {RequiredDiscardCount} 张，已选 {SelectedDiscardCount} 张。再次点击可取消。"
            : IsHandResponsePending ? HandResponseHint
            : IsActiveSkillSelectionPending ? GetActiveSkillSelectionHint()
            : IsMultiTargetCardSelected ? MultiTargetSelectionHint
            : selectedName is not null ? target is not null ? CanConfirmSelected
                    ? $"{(!CanPlaySelected && CanPlaySelectedAsSlash ? $"{selectedName}{AlternatePlayText}" : selectedName)} → {target.GeneralName} · 确认后使用"
                    : $"{selectedName} → {target.GeneralName} · 在中央选择具体目标牌"
                : CanConfirmSelected ? $"已选择【{selectedName}】· 点击「确定」使用" : $"已选择【{selectedName}】· 点击亮起的武将选择目标"
            : IsGeneralSelectionPending ? "选择你的武将，准备出征"
            : CanUseActiveSkill && HumanActiveSkillAction is { } activeAction &&
              (activeAction.MinCardCount > 0 || activeAction.MaxCardCount > 0 ||
               activeAction.MinTargetCount > 0 || activeAction.MaxTargetCount > 0)
                ? $"点击【{HumanActiveSkillName}】，再选择{BuildActiveSkillRequirement(activeAction)}"
            : CanUseActiveSkill ? Hand.Any(item => item.IsPlayable)
                ? $"选择一张手牌，或发动 {ActiveSkillButtonText}"
                : $"可发动 {ActiveSkillButtonText}"
            : CanEndTurn ? Hand.Any(item => item.IsPlayable) ? "选择一张手牌，再选择目标" : "当前没有可使用的手牌 · 可以结束出牌"
            : HasChoicePrompt ? _snapshot.PendingDecision!.Prompt
            : CanStepAi ? IsAutoAdvance ? "其他武将正在行动，请稍候…" : "自动推进已暂停 · 可继续推进或观察单步"
            : PromptText;
        RaisePropertyChanged(nameof(PlayButtonText));
        RaisePropertyChanged(nameof(ActionDockConfirmText));
        RaisePropertyChanged(nameof(SupplementalActiveSkillActions));
        RaisePropertyChanged(nameof(HumanSkillColumns));
        RaisePropertyChanged(nameof(HumanSkillRailWidth));
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
        RaisePropertyChanged(nameof(AlternatePlayText));
        ((RelayCommand)ConfirmSelectedCommand).NotifyCanExecuteChanged();
        ((RelayCommand)UseActiveSkillCommand).NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(HasCenterChoices));
        RaisePropertyChanged(nameof(IsTableIdle));
        RefreshPlayerGuide();
    }

    private LegalAction? SelectedConversionAction()
    {
        if (_selectedCardId is not { } cardId || _snapshot.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            return null;
        }

        var human = _snapshot.Players.Single(player => player.IsHuman);
        var physicalKind = human.Hand.Concat(human.WoodenOxGrain ?? []).Concat(human.Equipment)
            .SingleOrDefault(card => card.Id == cardId)?.Kind;
        if (physicalKind is null)
        {
            return null;
        }

        var selectedTargets = SelectedPlayTargets();
        return _game.GetHumanLegalActions().FirstOrDefault(action =>
            action.CardId == cardId &&
            action.TargetSeats.SequenceEqual(selectedTargets) &&
            action.PlayedCardKind is { } effectiveKind &&
            effectiveKind != physicalKind &&
            (_selectedConversionSource is null || action.ConversionSource == _selectedConversionSource));
    }

    private void ClearSelection()
    {
        var wasSelectingActiveSkillCards = _isSelectingActiveSkillCards;
        _selectedCardId = null;
        _selectedConversionSource = null;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();
        _discardCardIds.Clear();
        _selectedActiveSkillCardIds.Clear();
        _selectedActiveSkillTargetSeats.Clear();
        _selectedEquipmentEffectKind = null;
        _selectedProgramSkillId = null;
        _selectedProgramActivationId = null;
        _selectedProgramSkillOwnerSeat = null;
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
        var action = HumanActiveSkillAction;
        if (action is null) return "正在选择主动技能参数。";
        var skillName = ActiveSkillName(action);

        var parts = new List<string>();
        if (action.MaxCardCount > 0)
        {
            parts.Add($"牌 {_selectedActiveSkillCardIds.Count}/{FormatSelectionRange(action.MinCardCount, action.MaxCardCount)}");
        }

        if (action.MaxTargetCount > 0)
        {
            parts.Add($"目标 {_selectedActiveSkillTargetSeats.Count}/{FormatSelectionRange(action.MinTargetCount, action.MaxTargetCount)}");
        }

        var next = CanConfirmActiveSkill
            ? "点击「确定」或按 Enter 确认。"
            : "选够牌和目标后即可确认；Esc 取消。";
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
        if (_openingDealTimer is not null)
        {
            _openingDealTimer.Stop();
            _openingDealTimer.Tick -= OnOpeningDealTick;
        }
        DetachEngine();
    }
}

public sealed record TablePlayViewModel(long Sequence, string Name, string ActorName)
{
    public CardKind? Kind { get; init; }
    public System.Windows.Media.ImageSource? Artwork => CardArt.Get(Kind);
    public bool HasArtwork => Artwork is not null;
    public string VerticalName => string.Join("\n", Name.ToCharArray());
    public double NameSize => Name.Length > 3 ? 18 : Name.Length == 1 ? 33 : 23;
}
