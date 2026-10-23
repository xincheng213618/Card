using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IShanheCampaignSaveStore? _shanheSaveStore;
    private ShanheCampaignSession? _shanheSession;
    private GameEngine? _shanheBattleGame;
    private GameEngine? _shanheTableGame;
    private ShanheBattleSetup? _shanheTableSetup;
    private int _shanheRunDifficulty = 1;
    private bool _isShanheOpen;
    private bool _shanheWasAutoAdvance;
    private bool _hasShanheProgress;
    private string _shanheStatusText = "征程保存在本机，普通对局存档独立保留。";
    private ShanheDifficultyOption _selectedShanheDifficulty = null!;
    private ShanheGeneralOption? _selectedShanheGeneral;
    private ShanheScenarioOption? _selectedShanheScenario;

    public IReadOnlyList<ShanheScenarioOption> ShanheScenarios { get; } =
        StandardShanheCampaignCatalog.Editions.Select(edition => new ShanheScenarioOption(edition.Id, edition.Name)).ToArray();
    public ShanheScenarioOption SelectedShanheScenario
    {
        get => _selectedShanheScenario ?? ShanheScenarios[0];
        set
        {
            if (IsShanheRunInProgress && value?.Id != StandardShanheCampaignCatalog.EditionIdForCampaign(_shanheSession!.Definition.Id, _shanheRunDifficulty)) return;
            if (value is null || !ShanheScenarios.Contains(value) || !SetProperty(ref _selectedShanheScenario, value)) return;
            RaisePropertyChanged(nameof(SelectedShanheScenarioDescription));
            if (!IsShanheRunInProgress) RefreshShanhePresentation();
        }
    }
    public IReadOnlyList<ShanheDifficultyOption> ShanheDifficulties { get; } =
        [new(1, "初征 · 难度一"), new(2, "进阶 · 难度二"), new(3, "鏖战 · 难度三")];
    public ObservableRangeCollection<ShanheGeneralOption> ShanheGenerals { get; } = [];
    public ObservableCollection<ShanheMapRow> ShanheMapRows { get; } = [];
    public ObservableCollection<ShanheJourneyNode> ShanheMapNodes { get; } = [];
    public ObservableCollection<ShanheJourneyLink> ShanheMapLinks { get; } = [];
    public ObservableCollection<ShanheCampaignChoice> ShanheChoices { get; } = [];
    public ObservableCollection<ShanheBuildCard> ShanheTacticCards { get; } = [];
    public ObservableCollection<ShanheBuildCard> ShanheExtraSkillCards { get; } = [];

    public ShanheDifficultyOption SelectedShanheDifficulty
    {
        get => _selectedShanheDifficulty;
        set
        {
            if (IsShanheRunInProgress && value?.Value != _shanheRunDifficulty) return;
            if (value is null || !SetProperty(ref _selectedShanheDifficulty, value)) return;
            if (!IsShanheRunInProgress) RefreshShanhePresentation();
        }
    }
    public ShanheGeneralOption? SelectedShanheGeneral
    {
        get => _selectedShanheGeneral;
        set
        {
            if (IsShanheRunInProgress && value?.Id != _shanheSession!.Snapshot.PlayerGeneralId) return;
            // Changing a page clears the ListBox selection before it binds the next eight cards.
            // Keep the chosen general until the player explicitly chooses another card.
            if (value is null || !ShanheGenerals.Contains(value) || !SetProperty(ref _selectedShanheGeneral, value)) return;
            RevealSelectedShanheGeneral();
            foreach (var property in new[] { nameof(SelectedShanhePortrait), nameof(SelectedShanheGeneralDescription), nameof(SelectedShanheSkillText), nameof(SelectedShanheGeneralMeta) })
                RaisePropertyChanged(property);
            (StartShanheCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    public Brush SelectedShanhePortrait => SelectedShanheGeneral?.Portrait ?? Brushes.Transparent;
    public string SelectedShanheGeneralDescription => SelectedShanheGeneral?.Description ?? "选择一名武将，查看技能与出征信息。";
    public string SelectedShanheSkillText => SelectedShanheGeneral?.SkillSummary ?? string.Empty;
    public string SelectedShanheGeneralMeta
    {
        get
        {
            if (SelectedShanheGeneral is not { } general) return string.Empty;
            var text = $"{general.Kingdom} · {general.EditionText}";
            var defaultFaction = IsShanheRunInProgress && _shanheSession is { } session ? session.Definition.DefaultGodFactionId :
                StandardShanheCampaignCatalog.Editions.Single(edition => edition.Id == SelectedShanheScenario.Id).DefaultGodFactionId;
            return defaultFaction is not null &&
                StandardShanheCampaignCatalog.PlayerRosterRegistry.Generals[general.Id].FactionId == "god"
                ? text + $" · 出征默认{FactionName(defaultFaction)}" : text;
        }
    }
    public string SelectedShanheScenarioDescription => StandardShanheCampaignCatalog.Editions
        .Single(edition => edition.Id == SelectedShanheScenario.Id).Description;
    public bool HasShanheSession => _shanheSession is not null;
    public bool IsShanheOpen
    {
        get => _isShanheOpen;
        private set
        {
            if (!SetProperty(ref _isShanheOpen, value)) return;
            if (value) { _shanheWasAutoAdvance = IsAutoAdvance; IsAutoAdvance = false; }
            else IsAutoAdvance = _shanheWasAutoAdvance;
        }
    }
    public bool HasShanheProgress { get => _hasShanheProgress; private set => SetProperty(ref _hasShanheProgress, value); }
    public string ShanheStatusText { get => _shanheStatusText; private set => SetProperty(ref _shanheStatusText, value); }
    public string ShanheEditionNotice => "有剧情的单机征程 · 本地起步版 · " +
        (IsShanheRunInProgress && _shanheSession is { } session ? session.Definition.Description :
            StandardShanheCampaignCatalog.Editions.Single(edition => edition.Id == SelectedShanheScenario.Id).Description);
    public string ShanheCampaignTitle => _shanheSession?.Snapshot.CampaignName ?? "诸侯伐董 · 征途将启";
    public bool IsShanheBattleActive => _shanheSession?.Snapshot.Phase == ShanheCampaignPhase.Battle;
    public bool IsShanheBattleSession => _shanheTableGame is not null && ReferenceEquals(_game, _shanheTableGame);
    public string ShanheRouteSummary => _shanheSession is { } session
        ? $"已过 {session.Snapshot.CompletedNodeIds.Count} 处 · 当前：{ShanheCurrentNodeName} · 难度 {_shanheRunDifficulty}\n路线：" +
            string.Join(" → ", session.Snapshot.CompletedNodeIds.Select(id => session.Definition.Nodes.Single(node => node.Id == id).Name)
                .Append(ShanheCurrentNodeName).Distinct(StringComparer.Ordinal))
        : "从军营起程，选择战斗、奇遇、行商与营地，沿自己的路线抵达洛阳。";
    public string ShanheVitalText
    {
        get
        {
            if (_shanheSession is not { } session) return "选择一名武将，踏上征途";
            var live = session.Snapshot.Phase == ShanheCampaignPhase.Battle
                ? _shanheBattleGame?.CreateSnapshot(0).Players.Single(player => player.IsHuman) : null;
            return $"{ShanheGeneralName(session.Snapshot.PlayerGeneralId)}   体力 {live?.Hp ?? session.Snapshot.Hp}/{live?.MaxHp ?? session.Snapshot.MaxHp}";
        }
    }
    public string ShanheGoldText => $"铜币   {_shanheSession?.Snapshot.Gold ?? 0}";
    public string ShanheTacticsText => _shanheSession is { } session
        ? $"{ShanheRankStatusText}\n基础整备：体力上限 +{session.Snapshot.Bonuses.MaxHp} · 起手 +{session.Snapshot.Bonuses.InitialHand} · 摸牌 +{session.Snapshot.Bonuses.Draw} · 出杀 +{session.Snapshot.Bonuses.Slash}\n战法 {session.Snapshot.TacticIds.Count} 项 · 可在「战法构筑」查看完整效果"
        : "战法：征途中获得的成长会带入后续战斗。";
    public string ShanheBuildTabTitle => $"战法构筑 · {ShanheTacticCards.Count}";
    public string ShanheTacticEmptyText => ShanheTacticCards.Count == 0 ? "尚未获得战法。前往奇遇、战后奖励和行商，选择适合自己的组合。" : "战法在每场战斗中作为技能获得，由牌局规则处理触发、费用、次数与失效。";
    public string ShanheStartingLoadoutText => _shanheSession is { } session
        ? "随身牌：" + (session.Snapshot.StartingCards.Count == 0 ? "无" :
            string.Join("、", session.Snapshot.StartingCards.Select(card => CardCatalog.Get(card.Kind).DisplayName))) +
            "\n随身装备：" + (session.Snapshot.StartingEquipment.Count == 0 ? "无" :
                string.Join("、", session.Snapshot.StartingEquipment.Select(card => CardCatalog.Get(card.Kind).DisplayName))) +
            "\n" + ShanheStartingResourceNotice + "普通手牌与装备不会直接跨战继承。"
        : "在征途中获取随身牌和装备，每战加入手牌，完善你的构筑。";
    private bool ShanheStartingResourcesExpireOnUse => _shanheSession?.Definition.StartingResourcesExpireOnUse ?? true;
    private string ShanheStartingResourceNotice => ShanheStartingResourcesExpireOnUse
        ? "随身牌和装备每战加入手牌；使用或响应后本应进入弃牌堆时移出本局，被技能提前取得则保留。"
        : "随身牌每战加入手牌，初始装备每战直接装备。";
    public string ShanheSkillsText => _shanheSession is { } session
        ? $"额外技能 {session.Snapshot.ExtraSkillIds.Count}/{session.Snapshot.ExtraSkillSlots}：" +
            (session.Snapshot.ExtraSkillIds.Count == 0 ? "尚未习得" : string.Join("、", session.Snapshot.ExtraSkillIds.Select(ShanheSkillName))) +
            $"\n随身牌 {session.Snapshot.StartingCards.Count} · 初始装备 {session.Snapshot.StartingEquipment.Count}"
        : "额外技能：在战后奖励、奇遇与行商中寻找新的组合。";
    private string ShanheCurrentNodeName => _shanheSession?.Snapshot.CurrentNodeId is { } id
        ? _shanheSession.Definition.Nodes.FirstOrDefault(node => node.Id == id)?.Name ?? id : "起点";
    private string ShanheBattleNodeName => _shanheTableSetup is { } setup
        ? _shanheSession?.Definition.Nodes.FirstOrDefault(node => node.Id == setup.NodeId)?.Name ?? setup.NodeId : ShanheCurrentNodeName;
    private int ShanheAlliedSeatCount => _snapshot?.Players.Count(player => player.TeamId == ShanheBattleSetup.PlayerTeamId) ?? 0;
    private int ShanheEnemySeatCount => _snapshot?.Players.Count(player => player.TeamId == ShanheBattleSetup.EnemyTeamId) ?? 0;
    private string ShanheBattleRosterText => $"义军 {ShanheAlliedSeatCount} 席 · 敌军 {ShanheEnemySeatCount} 席 · {Math.Max(0, ShanheAlliedSeatCount - 1)} 名人机友军";
    private string ShanheBattleObjective => $"你的目标：击败{ShanheBattleNodeName}的全部敌军。" +
        (ShanheAlliedSeatCount > 1 ? $"本战有 {ShanheAlliedSeatCount - 1} 名人机友军助战。" : "本战由你独自迎敌。") +
        "胜利后保留剩余体力，领取战利品再继续行军；本次征程战败后结束。";
    public string ShanheCarryoverNotice => "胜利后保留剩余体力。战法持续本次征程，每战获得且不占技能槽；额外所学技能单独占槽，满槽时可替换。" + ShanheStartingResourceNotice;
    public string ShanheContextTitle => _shanheSession?.Snapshot.Phase switch
    {
        ShanheCampaignPhase.Battle => "战 鼓 已 起",
        ShanheCampaignPhase.Reward => "战 后 整 备",
        ShanheCampaignPhase.OpeningSkillDraft => "传 承 · 初 始 技 能",
        ShanheCampaignPhase.NodeChoice => ShanheCurrentNodeName,
        ShanheCampaignPhase.Won => "山 河 新 篇",
        ShanheCampaignPhase.Lost => "此 行 未 竟",
        _ => _shanheSession is null ? "诸 侯 会 盟" : "前 路 待 择"
    };
    public string ShanheContextDescription
    {
        get
        {
            if (_shanheSession is not { } session) return StandardShanheCampaignCatalog.CreateCodexDefinition(SelectedShanheDifficulty?.Value ?? 1, SelectedShanheScenario.Id).IntroText;
            var state = session.Snapshot;
            if (state.Phase is ShanheCampaignPhase.Won or ShanheCampaignPhase.Lost) return state.EndingText;
            return state.StoryText + (state.BossRewardSummary is not null ? "\n\n首领已败，分别领取战法与卡牌，确认奖励后继续征程。"
                : state.Phase == ShanheCampaignPhase.Reward ? "\n\n从下方奖励中选择一项，再继续征程。"
                : state.Phase == ShanheCampaignPhase.Battle ? "\n\n此战的体力和收获会影响后续路程。可返回牌桌继续战斗。" : "");
        }
    }

    public ICommand OpenShanheCommand { get; private set; } = null!;
    public ICommand CloseShanheCommand { get; private set; } = null!;
    public ICommand StartShanheCommand { get; private set; } = null!;
    public ICommand ContinueShanheCommand { get; private set; } = null!;
    public ICommand ResumeShanheBattleCommand { get; private set; } = null!;
    public ICommand SelectShanheNodeCommand { get; private set; } = null!;
    public ICommand ChooseShanheOptionCommand { get; private set; } = null!;

    private void InitializeShanheCampaign(IShanheCampaignSaveStore? store)
    {
        _shanheSaveStore = store ?? new FileShanheCampaignSaveStore();
        OpenShanheCommand = new RelayCommand(() =>
        {
            if (IsTutorialActive) return;
            IsHelpOpen = false; IsLogOpen = false; IsHistoryOpen = false; IsGeneralGalleryOpen = false;
            IsShanheOpen = true;
            RefreshShanhePresentation();
        }, () => !IsTutorialActive);
        CloseShanheCommand = new RelayCommand(() => { if (SaveShanheProgress()) IsShanheOpen = false; });
        StartShanheCommand = new RelayCommand(StartShanheCampaign, () => CanPrepareShanheExpedition && SelectedShanheGeneral is not null);
        ContinueShanheCommand = new RelayCommand(ContinueShanheCampaign, () => !IsTutorialActive && HasShanheProgress);
        ResumeShanheBattleCommand = new RelayCommand(ResumeShanheBattle, () => IsShanheBattleActive && !IsTutorialActive);
        SelectShanheNodeCommand = new RelayCommand<ShanheCampaignNode>(SelectShanheNode,
            IsCurrentShanheNode);
        ChooseShanheOptionCommand = new RelayCommand<ShanheCampaignChoice>(ChooseShanheOption,
            IsCurrentShanheChoice);
        _selectedShanheDifficulty = ShanheDifficulties[0];
        InitializeShanheGeneralRoster();
        InitializeShanheJourney();
        InitializeShanheRewards();
        HasShanheProgress = _shanheSaveStore.Exists;
        RefreshShanhePresentation();
    }

    private void StartShanheCampaign()
    {
        if (!CanPrepareShanheExpedition || SelectedShanheGeneral is not { } general) return;
        if (_shanheSession is { } previous && ShanheCodexSettlement.ConfirmedBookAmount(previous, _shanheRunDifficulty) > 0 &&
            !ShanheCodexSettlement.HasClaimed(_shanheCodexProfile, previous) &&
            (!SaveShanheProgress() || !ShanheCodexSettlement.HasClaimed(_shanheCodexProfile, previous)))
        { ShanheStatusText = "上次通关图册尚未保存，请先重试结算，避免覆盖未领取的征程。"; return; }
        try
        {
            var difficulty = SelectedShanheDifficulty.Value;
            var definition = StandardShanheCampaignCatalog.CreateCodexDefinition(difficulty, SelectedShanheScenario.Id);
            var session = ShanheCampaignSession.Create(definition,
                CreateCodexCampaignOptions(_seedOverride ?? RandomNumberGenerator.GetInt32(1, int.MaxValue), general.Id, definition)
                    with { PlayerRank = SelectedShanheRank.Value == 1 ? null : SelectedShanheRank.Value },
                StandardShanheCampaignCatalog.CreateBattleRegistry);
            _shanheSession = session;
            _shanheResumeFailed = false;
            _shanheRunDifficulty = difficulty;
            _shanheBattleGame = null;
            HasShanheProgress = true;
            RefreshShanhePresentation();
            SaveShanheProgress();
        }
        catch (Exception error) when (IsSaveException(error)) { ShanheStatusText = $"征程未能开始：{ReadableSaveError(error)}"; }
    }

    private void ContinueShanheCampaign()
    {
        if (IsTutorialActive || _shanheSaveStore is null) return;
        if (_shanheSession is not null)
        {
            if (_shanheSession.PendingBattleSetup is not null && _shanheBattleGame is null) StartShanheBattle();
            IsShanheOpen = true; RefreshShanhePresentation(); return;
        }
        try
        {
            var save = _shanheSaveStore.Read();
            var session = ShanheCampaignSession.Restore(save.Campaign,
                StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(save.Campaign, save.Difficulty), StandardShanheCampaignCatalog.CreateBattleRegistry);
            GameEngine? battle = null;
            if (session.PendingBattleSetup is { } setup)
            {
                if (save.Battle is not { } checkpoint || checkpoint.Options is not { } battleOptions || battleOptions.HumanSeat != 0 ||
                    battleOptions.Seed != setup.BattleSeed || checkpoint.ModeId != ShanheBattleSetup.BattleModeId ||
                    !ShanheBattleSetup.ValueEquals(battleOptions.ShanheBattle, setup))
                    throw new InvalidDataException("当前战斗与征程节点不一致。");
                battle = GameReplay.Restore(checkpoint, StandardShanheCampaignCatalog.CreateBattleRegistry(setup));
            }
            else if (save.Battle is not null) throw new InvalidDataException("当前征程阶段不应带有未结算的战斗。");
            // Replace both halves only after both journals have fully validated.
            _shanheSession = session; _shanheRunDifficulty = save.Difficulty; _shanheBattleGame = battle;
            _shanheResumeFailed = false;
            SelectedShanheScenario = ShanheScenarios.Single(option => option.Id ==
                StandardShanheCampaignCatalog.EditionIdForCampaign(session.Definition.Id, save.Difficulty));
            SelectedShanheDifficulty = ShanheDifficulties.Single(option => option.Value == save.Difficulty);
            SelectedShanheRank = ShanheRanks.Single(option => option.Value == session.Snapshot.PlayerRank);
            if (battle is not null)
            {
                _shanheTableGame = battle;
                _shanheTableSetup = session.PendingBattleSetup;
                ReplaceEngine(battle);
                IsNewGameSetupOpen = false;
                CaptureShanheBattleResult();
            }
            IsShanheOpen = true;
            ShanheStatusText = $"已恢复本机征程 · {save.SavedAtUtc.ToLocalTime():MM-dd HH:mm}。";
            SettleShanheCodexReward();
            RefreshShanhePresentation();
        }
        catch (Exception error) when (IsSaveException(error))
        {
            _shanheResumeFailed = true;
            ShanheStatusText = $"读取征程失败：{ReadableSaveError(error)} 原文件已保留，可开始新征程。";
            RefreshShanheJourney();
        }
    }

    private void SelectShanheNode(ShanheCampaignNode node)
    {
        if (IsTutorialActive || !IsCurrentShanheNode(node)) return;
        ApplyShanheCommand(new MoveShanheNodeCommand(node.Id, node.Revision));
        if (_shanheSession?.PendingBattleSetup is not null) StartShanheBattle();
    }

    private void ChooseShanheOption(ShanheCampaignChoice choice)
    {
        if (IsTutorialActive || !IsCurrentShanheChoice(choice)) return;
        ApplyShanheCommand(choice.IsReward
            ? new ChooseShanheRewardCommand(choice.Id, choice.ReplaceSkillId, choice.Revision)
            : new ChooseShanheNodeOptionCommand(choice.Id, choice.ReplaceSkillId, choice.Revision));
    }

    private void ApplyShanheCommand(ShanheCampaignCommand command)
    {
        if (_shanheSession is not { } session || IsTutorialActive) return;
        var previousPhase = session.Snapshot.Phase;
        var result = session.Submit(command);
        if (!result.Accepted) { ShanheStatusText = $"选择未执行：{result.Error}"; return; }
        if (previousPhase != ShanheCampaignPhase.Battle && result.State.Phase == ShanheCampaignPhase.Battle)
            _shanheBattleGame = null;
        RefreshShanhePresentation();
        SaveShanheProgress();
    }

    private void StartShanheBattle()
    {
        if (_shanheSession?.PendingBattleSetup is not { } setup) return;
        try
        {
            var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
            _shanheBattleGame = _shanheTableGame = battle;
            _shanheTableSetup = setup;
            IsNewGameSetupOpen = false; IsIdentityRevealOpen = false;
            ReplaceEngine(battle);
            var result = SubmitCommand(new StartGameCommand());
            if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "战斗开局失败。");
            RefreshCommandResult(result.State);
            IsShanheOpen = false;
            RefreshShanhePresentation();
            SaveShanheProgress();
        }
        catch (Exception error) when (IsSaveException(error))
        { ShanheStatusText = $"战斗暂未准备好：{ReadableSaveError(error)} 征程节点已保留，请重试继续征程。"; }
    }

    private void ResumeShanheBattle()
    {
        if (IsTutorialActive || !IsShanheBattleActive) return;
        if (_shanheBattleGame is not { } battle) { StartShanheBattle(); return; }
        if (!ReferenceEquals(_game, battle)) { _shanheTableGame = battle; ReplaceEngine(battle); }
        IsNewGameSetupOpen = false;
        IsShanheOpen = false;
    }

    private void CaptureShanheBattleResult()
    {
        if (_shanheSession is not { } session || session.Snapshot.Phase != ShanheCampaignPhase.Battle ||
            _shanheBattleGame is not { } battle || !ReferenceEquals(_game, battle) || battle.State.Status != EngineStatus.Completed) return;
        var result = session.CompleteBattle(battle, session.Revision);
        if (!result.Accepted) { ShanheStatusText = $"战斗结算未完成：{result.Error}"; return; }
        IsShanheOpen = true;
        RefreshShanhePresentation();
        SaveShanheProgress();
    }

    private void SaveShanheBeforeOrdinaryGame()
    {
        if (IsShanheBattleSession) SaveShanheProgress();
    }

    public bool FlushShanheProgress() => SaveShanheProgress();

    private bool SaveShanheProgress()
    {
        if (_shanheSession is not { } session || _shanheSaveStore is null || IsTutorialActive) return true;
        try
        {
            var checkpoint = session.PendingBattleSetup is not null ? _shanheBattleGame?.CreateCheckpoint() : null;
            // A just-selected encounter may not have been constructed yet. Its first complete save follows StartGame.
            if (session.PendingBattleSetup is { } pending && (checkpoint is null ||
                !ShanheBattleSetup.ValueEquals(checkpoint.Options.ShanheBattle, pending))) return true;
            _shanheSaveStore.Write(new ShanheCampaignSaveFile(ShanheCampaignSaveFile.CurrentFormatVersion,
                DateTimeOffset.UtcNow, _shanheRunDifficulty, session.CreateCheckpoint(), checkpoint));
            SettleShanheCodexReward();
            HasShanheProgress = true;
            ShanheStatusText = $"山河图已保存 · {DateTime.Now:HH:mm:ss} · 普通对局存档独立保留。";
            if (IsShanheBattleSession)
            {
                _autoSavePending = false;
                _saveTimer?.Stop();
                HasSaveError = false;
                SaveStatus = ShanheStatusText;
            }
            return true;
        }
        catch (Exception error) when (IsSaveException(error))
        {
            ShanheStatusText = $"山河图独立征程未保存：{ReadableSaveError(error)} 已有存档保留，当前征程仍可继续，请稍后重试。";
            if (IsShanheBattleSession)
            {
                _autoSavePending = true;
                HasSaveError = true;
                SaveStatus = ShanheStatusText;
            }
            return false;
        }
    }

    private void RefreshShanhePresentation()
    {
        if (_selectedShanheDifficulty is null) return;
        var definition = _shanheSession?.Definition ?? StandardShanheCampaignCatalog.CreateCodexDefinition(SelectedShanheDifficulty.Value, SelectedShanheScenario.Id);
        RefreshShanheGeneralRoster();
        var state = _shanheSession?.Snapshot;
        var available = state?.AvailableNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var completed = state?.CompletedNodeIds.ToHashSet(StringComparer.Ordinal) ?? [];
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = new Queue<(string Id, int Depth)>();
        foreach (var id in definition.StartingNodeIds) pending.Enqueue((id, 0));
        while (pending.TryDequeue(out var item))
        {
            if (depths.ContainsKey(item.Id)) continue;
            depths[item.Id] = item.Depth;
            var current = definition.Nodes.Single(node => node.Id == item.Id);
            foreach (var next in current.NextNodeIds) pending.Enqueue((next, item.Depth + 1));
        }
        ShanheMapRows.Clear();
        foreach (var group in definition.Nodes.GroupBy(node => depths.GetValueOrDefault(node.Id)).OrderBy(group => group.Key))
        {
            var nodes = group.Select(node => new ShanheCampaignNode(node.Id, node.Name, ShanheKindLabel(node.Kind),
                node.Description + (node.NextNodeIds.Count == 0 ? "\n此处是本条路线的终点。" : "\n通往：" +
                    string.Join("、", node.NextNodeIds.Select(id => definition.Nodes.Single(next => next.Id == id).Name))),
                completed.Contains(node.Id) ? "已完成" : state?.CurrentNodeId == node.Id ? "当前所在"
                    : available.Contains(node.Id) ? "可前往 →" : "等待前路开启",
                available.Contains(node.Id), state?.Revision ?? -1)).ToArray();
            ShanheMapRows.Add(new($"第 {group.Key + 1} 程", nodes));
        }
        RefreshShanheJourneyMap(definition, depths, state?.CurrentNodeId, completed);
        ShanheChoices.Clear();
        ShanheTacticCards.Clear();
        ShanheExtraSkillCards.Clear();
        if (state is not null)
        {
            foreach (var id in state.TacticIds)
            {
                var tactic = definition.Rewards.Single(reward => reward.Id == id);
                var ruleText = tactic.TacticSkillId is { } skillId && CurrentPresentationRegistry.Skills.TryGetValue(skillId, out var skill)
                    ? skill.Description : string.Empty;
                ShanheTacticCards.Add(new(id, tactic.Name, tactic.Description, ruleText,
                    tactic.OpeningSkillRewardIds is not null ? "初始战法 · 出征时触发一次 · 获得的技能占额外技能槽"
                        : "战法 · 本次征程持续 · 每战作为技能获得 · 不占额外技能槽"));
            }
            foreach (var id in state.ExtraSkillIds)
            {
                var skill = CurrentPresentationRegistry.Skills.GetValueOrDefault(id);
                ShanheExtraSkillCards.Add(new(id, skill?.Name ?? id, skill?.Description ?? "当前获得的额外技能。",
                    string.Empty, "额外所学技能 · 占用 1 个技能槽 · 满槽时可被替换"));
            }
        }
        if (state?.Phase == ShanheCampaignPhase.Reward)
            foreach (var reward in state.RewardOffers) AddShanheChoice(reward.Id, reward.Name, reward.Description, reward, true, true);
        if (state?.Phase == ShanheCampaignPhase.NodeChoice)
            foreach (var choice in state.NodeChoices)
            {
                var reward = choice.RewardId is { } id ? definition.Rewards.Single(item => item.Id == id) : null;
                var cost = (choice.GoldCost == 0 ? "" : $"{choice.GoldCost} 金币 · ") +
                    (choice.HpCost == 0 ? "" : $"{choice.HpCost} 体力 · ");
                AddShanheChoice(choice.Id, cost + choice.Name,
                    string.IsNullOrWhiteSpace(choice.Description) ? reward?.Description ?? "继续当前路线。" : choice.Description,
                    reward, false,
                    state.Gold >= choice.GoldCost && state.Hp > choice.HpCost);
            }
        RefreshShanheModules();
        RefreshShanheJourney();
        RefreshShanheRewards();
        RefreshShanheSettlement();
        ValidateShanheSkillReplacement();
        foreach (var property in new[] { nameof(ShanheCampaignTitle), nameof(ShanheRouteSummary), nameof(ShanheVitalText),
            nameof(ShanheGoldText), nameof(ShanheTacticsText), nameof(ShanheSkillsText), nameof(ShanheRankStatusText), nameof(SelectedShanheRankDescription), nameof(ShanheContextTitle),
            nameof(ShanheContextDescription), nameof(IsShanheBattleActive), nameof(IsShanheBattleSession),
            nameof(ShanheBuildTabTitle), nameof(ShanheTacticEmptyText), nameof(ShanheStartingLoadoutText), nameof(ShanheCarryoverNotice), nameof(ShanheEditionNotice), nameof(SelectedShanheGeneralMeta), nameof(HasShanheSession) }) RaisePropertyChanged(property);
        foreach (var command in new[] { StartShanheCommand, ContinueShanheCommand, ResumeShanheBattleCommand })
            (command as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void RefreshShanheJourneyMap(ShanheCampaignDefinition definition, IReadOnlyDictionary<string, int> depths,
        string? currentNodeId, IReadOnlySet<string> completed)
    {
        ShanheMapNodes.Clear();
        ShanheMapLinks.Clear();
        var visibleNodes = CurrentShanheChapterNodes(definition, depths, currentNodeId, completed).ToArray();
        var firstDepth = visibleNodes.Min(node => depths.GetValueOrDefault(node.Id));
        var maxDepth = Math.Max(1, visibleNodes.Max(node => depths.GetValueOrDefault(node.Id)) - firstDepth);
        var presentations = ShanheMapRows.SelectMany(row => row.Nodes).ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var group in visibleNodes.GroupBy(node => depths.GetValueOrDefault(node.Id)).OrderBy(group => group.Key))
        {
            var nodes = group.ToArray();
            for (var index = 0; index < nodes.Length; index++)
            {
                var relativeDepth = group.Key - firstDepth;
                var centerX = 72 + 860.0 * relativeDepth / maxDepth;
                // The current region fills the parchment while preserving its genuine branches.
                var centerY = nodes.Length == 1 ? 258 + (relativeDepth % 3 - 1) * 74
                    : 162 + 198.0 * index / (nodes.Length - 1);
                var node = nodes[index];
                ShanheMapNodes.Add(new(presentations[node.Id], centerX, centerY, node.Kind,
                    ShanheMapShortName(node), node.Id == currentNodeId, completed.Contains(node.Id)));
            }
        }
        var positions = ShanheMapNodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal);
        foreach (var node in visibleNodes)
            foreach (var nextId in node.NextNodeIds)
            {
                if (!positions.TryGetValue(nextId, out var end)) continue;
                var start = positions[node.Id];
                var route = new StreamGeometry();
                using (var context = route.Open())
                {
                    context.BeginFigure(new Point(start.CenterX, start.CenterY), false, false);
                    var bend = (end.CenterX - start.CenterX) * 0.48;
                    context.BezierTo(new Point(start.CenterX + bend, start.CenterY),
                        new Point(end.CenterX - bend, end.CenterY), new Point(end.CenterX, end.CenterY), true, false);
                }
                route.Freeze();
                ShanheMapLinks.Add(new(route, start.IsCompleted && (end.IsCompleted || end.IsCurrent),
                    (start.IsCurrent || start.IsCompleted) && end.IsAvailable));
            }
    }

    private static string ShanheMapShortName(ShanheNodeDefinition node) => node.Id switch
    {
        "hulao-start" => "北路出征", "guangcheng-start" => "南路出征", "hulao" => "虎牢关",
        "guangcheng" => "广成关", "huaxiong" => "汜水关", "dagu" => "大谷关", "huanyuan" => "轘辕关",
        "lijue" => "西凉铁骑", "guosi" => "乱军追击", "finalcamp" => "最后营火", "caravan" => "失散商旅",
        "luoyang" => "洛阳", "suanzao" => "酸枣", "henei" => "河内", _ => node.Name
    };

    private void AddShanheChoice(string id, string name, string description, ShanheRewardDefinition? reward, bool isReward, bool enabled)
    {
        var state = _shanheSession!.Snapshot;
        if (reward?.SkillId is { } learned && state.ExtraSkillIds.Contains(learned, StringComparer.Ordinal))
        {
            ShanheChoices.Add(new(id, name + " · 已习得", description, isReward, null, false, state.Revision));
            return;
        }
        if (reward?.SkillId is not null && state.ExtraSkillIds.Count >= state.ExtraSkillSlots + reward.AddSkillSlots)
        {
            foreach (var replaced in state.ExtraSkillIds)
                ShanheChoices.Add(new(id, $"{name} · 替换{ShanheSkillName(replaced)}", description, isReward, replaced, enabled, state.Revision));
            return;
        }
        ShanheChoices.Add(new(id, name, description, isReward, null, enabled, state.Revision));
    }

    private bool IsCurrentShanheNode(ShanheCampaignNode node) => node.IsAvailable &&
        _shanheSession?.Revision == node.Revision && ShanheMapRows.Any(row => row.Nodes.Any(current => ReferenceEquals(current, node)));

    private bool IsCurrentShanheChoice(ShanheCampaignChoice choice) => choice.IsEnabled &&
        _shanheSession?.Revision == choice.Revision && ShanheChoices.Any(current => ReferenceEquals(current, choice));

    private string ShanheGeneralName(string id) => StandardShanheCampaignCatalog.PlayerRosterRegistry.Generals.TryGetValue(id, out var general)
        ? general.Name : _contentRegistry.Generals.TryGetValue(id, out general) ? general.Name : id;
    private string ShanheSkillName(string id) => CurrentPresentationRegistry.Skills.TryGetValue(id, out var skill) ? skill.Name : id;
    private static string ShanheKindLabel(ShanheNodeKind kind) => kind switch
    { ShanheNodeKind.Battle => "交锋", ShanheNodeKind.EliteBattle => "精英", ShanheNodeKind.BossBattle => "决战", ShanheNodeKind.Event => "奇遇", ShanheNodeKind.Shop => "行商", ShanheNodeKind.Camp => "营地", _ => "征程" };
}

public sealed record ShanheScenarioOption(string Id, string Name);
public sealed record ShanheDifficultyOption(int Value, string Name);
public sealed record ShanheGeneralOption(string Id, string Name)
{
    public Brush Portrait => GeneralArt.GetPortrait(Id, decodeWidth: 300);
    public string SkillSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Kingdom { get; init; } = string.Empty;
    public string EditionText => (Id.StartsWith("ol:", StringComparison.Ordinal) ? "OL · " :
        Id.StartsWith("mobile:", StringComparison.Ordinal) ? "移动版 · " :
        Id.StartsWith("anniversary:", StringComparison.Ordinal) ? "十周年 · " : string.Empty) + Presentation.GeneralGalleryCatalog.Classify(Id).Title;
    public string SearchText => $"{Name} {Id} {Kingdom} {EditionText} {SkillSummary}";
}
public sealed record ShanheMapRow(string Title, IReadOnlyList<ShanheCampaignNode> Nodes);
public sealed record ShanheJourneyNode(ShanheCampaignNode Node, double CenterX, double CenterY,
    ShanheNodeKind Kind, string ShortName, bool IsCurrent, bool IsCompleted)
{
    public double X => CenterX - 42;
    public double Y => CenterY - 31;
    public bool IsAvailable => Node.IsAvailable;
    public string KindLabel => Node.KindLabel;
    public string StateLabel => IsCurrent ? $"{KindLabel} · 当前" : IsAvailable ? $"{KindLabel} · 可前往"
        : IsCompleted ? $"{KindLabel} · 已完成" : KindLabel;
    public string AccessibleName => Node.AccessibleName;
    public string DetailText => $"{Node.Name}\n{Node.StatusLabel}\n\n{Node.Description}";
}
public sealed record ShanheJourneyLink(Geometry Route, bool IsCompleted, bool IsAvailable);
public sealed record ShanheBuildCard(string Id, string Name, string Description, string RuleDescription, string SourceText)
{
    public bool HasRuleDescription => !string.IsNullOrWhiteSpace(RuleDescription) && RuleDescription != Description;
    public string AccessibleText => $"{Name}。{Description}。{SourceText}";
}
public sealed record ShanheCampaignNode(string Id, string Name, string KindLabel, string Description,
    string StatusLabel, bool IsAvailable, long Revision)
{
    public string AccessibleName => $"{Name}，{KindLabel}，{StatusLabel}";
}
public sealed record ShanheCampaignChoice(string Id, string Name, string Description, bool IsReward,
    string? ReplaceSkillId, bool IsEnabled, long Revision)
{
    public string AccessibleName => $"{Name}，{Description}";
}

