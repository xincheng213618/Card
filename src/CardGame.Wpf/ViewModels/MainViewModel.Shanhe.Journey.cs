using System.Windows.Input;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _shanheResumeFailed;
    private ShanheCampaignNode? _shanhePreviewNode;
    private string _shanheEncounterEnemyName = string.Empty;
    private string _shanheEncounterEnemyDetail = string.Empty;
    private string _shanheEncounterRewardText = string.Empty;
    private Brush _shanheEncounterEnemyPortrait = Brushes.Transparent;
    private int _shanheChapter = 1;
    private string _shanheChapterHeading = "第一章 · 关隘争锋";
    private int _shanheChapterCompleted;

    public bool IsShanheRunInProgress => _shanheSession?.Snapshot.Phase is ShanheCampaignPhase.Map
        or ShanheCampaignPhase.NodeChoice or ShanheCampaignPhase.Battle or ShanheCampaignPhase.Reward or ShanheCampaignPhase.OpeningSkillDraft;
    public bool IsShanhePreparation => !IsShanheRunInProgress;
    // Do not replace a saved expedition before the user has tried to resume it.
    public bool CanPrepareShanheExpedition => !IsTutorialActive && !IsShanheRunInProgress &&
        (!HasShanheProgress || _shanheSession is not null || _shanheResumeFailed);
    public string ShanhePreparationHint => IsShanheRunInProgress
        ? "征程进行中，武将与初始战法已锁定。战后选择奖励，沿路线前进至征程结束。"
        : HasShanheProgress && _shanheSession is null && !_shanheResumeFailed
            ? "本机有未读取的征程，请先继续征程。"
            : "选择出征武将，编辑山河册中的初始战法，再开启一段新征程。";
    public string ShanheCodexPhaseText => IsShanheRunInProgress
        ? "征程中仅可查看山河册；征程结束后可解锁和编辑初始战法。"
        : CanPrepareShanheExpedition ? "出征前可解锁和编辑初始战法，开始后本次配置固定。" : ShanhePreparationHint;
    public string ShanheChapterTitle => _shanheChapterHeading;
    public string ShanheChapterProgressText => $"本章已过 {_shanheChapterCompleted} 处 · 难度 {_shanheRunDifficulty}";
    public string ShanheChapterObjectiveText => _shanheSession?.Snapshot.Phase switch
    {
        ShanheCampaignPhase.Battle => "返回牌桌，击败本场全部敌军",
        ShanheCampaignPhase.OpeningSkillDraft => "传承触发，先选择一项稀有技能",
        ShanheCampaignPhase.Reward => _shanheSession?.Snapshot.BossRewardSummary is not null
            ? "分别领取战法和卡牌，确认后进入下一章" : "领取一项战利品，或放弃并领取铜币",
        ShanheCampaignPhase.NodeChoice => "完成当前地点的选择，继续行军",
        ShanheCampaignPhase.Won => "此番征程已完成，可重新整备出征",
        ShanheCampaignPhase.Lost => "此番征程已结束，可重新整备出征",
        _ => _shanheChapter == 3 ? "选择最终路线，迎接最后一战" : "选择亮起的地点，击败本章首领"
    };
    public Brush CurrentShanheGeneralPortrait => _shanheSession?.Snapshot.PlayerGeneralId is { } id
        ? GeneralArt.GetPortrait(id, decodeWidth: 300) : SelectedShanhePortrait;
    public string CurrentShanheGeneralName => _shanheSession?.Snapshot.PlayerGeneralId is { } id
        ? ShanheGeneralName(id) : SelectedShanheGeneral?.Name ?? "待选武将";
    public string CurrentShanheGeneralSkillsText => _shanheSession?.Snapshot.PlayerGeneralId is { } id
        ? ShanheGenerals.FirstOrDefault(general => general.Id == id)?.SkillSummary ?? string.Empty : SelectedShanheSkillText;

    public bool IsShanheEncounterPreviewOpen => _shanhePreviewNode is not null;
    public string ShanheEncounterTitle => _shanhePreviewNode?.Name ?? string.Empty;
    public string ShanheEncounterDescription => _shanhePreviewNode is { } preview
        ? _shanheSession?.Definition.Nodes.SingleOrDefault(node => node.Id == preview.Id)?.StoryText ?? preview.Description : string.Empty;
    public string ShanheEncounterEnemyName => _shanheEncounterEnemyName;
    public Brush ShanheEncounterEnemyPortrait => _shanheEncounterEnemyPortrait;
    public string ShanheEncounterEnemyDetail => _shanheEncounterEnemyDetail;
    public string ShanheEncounterRewardText => _shanheEncounterRewardText;
    public ICommand PreviewShanheNodeCommand { get; private set; } = null!;
    public ICommand ChallengeShanheEncounterCommand { get; private set; } = null!;
    public ICommand CloseShanheEncounterPreviewCommand { get; private set; } = null!;

    private void InitializeShanheJourney()
    {
        PreviewShanheNodeCommand = new RelayCommand<ShanheCampaignNode>(PreviewShanheNode, IsCurrentShanheNode);
        ChallengeShanheEncounterCommand = new RelayCommand(() =>
        {
            var node = _shanhePreviewNode;
            if (node is null || IsTutorialActive || !IsCurrentShanheNode(node)) return;
            CloseShanheEncounterPreview();
            SelectShanheNode(node);
        }, () => !IsTutorialActive && _shanhePreviewNode is { } node && IsCurrentShanheNode(node));
        CloseShanheEncounterPreviewCommand = new RelayCommand(CloseShanheEncounterPreview);
    }

    private void PreviewShanheNode(ShanheCampaignNode node)
    {
        if (IsTutorialActive || !IsCurrentShanheNode(node) || _shanheSession is not { } session) return;
        var definition = session.Definition.Nodes.Single(item => item.Id == node.Id);
        if (definition.Kind is not (ShanheNodeKind.Battle or ShanheNodeKind.EliteBattle or ShanheNodeKind.BossBattle))
        {
            SelectShanheNode(node);
            return;
        }
        var encounters = definition.Encounters!;
        var enemies = encounters.SelectMany(encounter => encounter.Enemies).Distinct().ToArray();
        var first = encounters[0];
        var state = session.Snapshot;
        // A registry probe reads only published content. No campaign command, battle engine,
        // random roll or save is produced until the player confirms the challenge.
        var probe = new ShanheBattleSetup(1, state.EncounterOrdinal + 1, definition.Id, state.PlayerGeneralId,
            state.Hp, state.Bonuses.MaxHp, state.Bonuses.InitialHand, state.Bonuses.Draw, state.Bonuses.Slash,
            state.ExtraSkillIds, state.StartingCards, state.StartingEquipment, enemies,
            encounters.SelectMany(encounter => encounter.Allies ?? []).Distinct().ToArray(), session.ContentHash);
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(probe);
        _shanhePreviewNode = node;
        _shanheEncounterEnemyName = string.Join(" · ", enemies.Select(enemy => registry.Generals[enemy.GeneralId].Name));
        _shanheEncounterEnemyPortrait = GeneralArt.GetPortrait(enemies[0].GeneralId, decodeWidth: 360);
        _shanheEncounterEnemyDetail = (encounters.Count > 1 ? "可能出现的敌军：\n" : string.Empty) +
            string.Join("\n\n", enemies.Select(enemy =>
            {
                var general = registry.Generals[enemy.GeneralId];
                var maxHp = general.BaseHp + enemy.MaxHpBonus;
                var skillIds = general.SkillIds.Concat(enemy.ExtraSkillIds ?? []).Distinct(StringComparer.Ordinal);
                return $"{general.Name}  体力 {enemy.Hp ?? maxHp}/{maxHp}\n" + string.Join(" · ", skillIds.Select(id => registry.GetSkill(id).Name));
            }));
        var gold = encounters.Select(encounter => encounter.VictoryGold).Distinct().Order().ToArray();
        var goldText = gold.Length == 1 ? gold[0].ToString() : $"{gold[0]}–{gold[^1]}";
        _shanheEncounterRewardText = (definition.BossRewards is { } boss
                ? $"铜币 × {boss.FixedGold}    技能槽 +{boss.SkillSlots}\n" +
                    string.Join("    ", boss.Groups.Select(group => $"{group.Name}（{group.ChoiceCount}选一）"))
                : $"铜币 × {goldText}" +
                    (definition.RewardIds?.Count > 0 ? $"    战利品 {definition.RewardChoiceCount} 选一" : string.Empty) +
                    (definition.RewardSkipGold > 0 ? $"\n放弃战利品可额外获得 {definition.RewardSkipGold} 铜币" : string.Empty)) +
            (first.HealAfterVictory > 0 && encounters.All(encounter => encounter.HealAfterVictory == first.HealAfterVictory)
                ? $"\n胜利后恢复 {first.HealAfterVictory} 点体力，剩余体力带入后续战斗" : string.Empty);
        NotifyShanheEncounterPreview();
    }

    private void CloseShanheEncounterPreview()
    {
        _shanhePreviewNode = null;
        NotifyShanheEncounterPreview();
    }

    private void NotifyShanheEncounterPreview()
    {
        foreach (var property in new[] { nameof(IsShanheEncounterPreviewOpen), nameof(ShanheEncounterTitle),
            nameof(ShanheEncounterDescription), nameof(ShanheEncounterEnemyName), nameof(ShanheEncounterEnemyPortrait),
            nameof(ShanheEncounterEnemyDetail), nameof(ShanheEncounterRewardText) }) RaisePropertyChanged(property);
        (ChallengeShanheEncounterCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void RefreshShanheJourney()
    {
        if (_shanhePreviewNode is { } node && !IsCurrentShanheNode(node)) CloseShanheEncounterPreview();
        foreach (var property in new[] { nameof(IsShanheRunInProgress), nameof(IsShanhePreparation), nameof(CanPrepareShanheExpedition),
            nameof(ShanhePreparationHint), nameof(ShanheCodexPhaseText), nameof(ShanheChapterTitle), nameof(ShanheChapterProgressText),
            nameof(ShanheChapterObjectiveText), nameof(CurrentShanheGeneralPortrait), nameof(CurrentShanheGeneralName),
            nameof(CurrentShanheGeneralSkillsText) }) RaisePropertyChanged(property);
        foreach (var command in new[] { StartShanheCommand, UnlockCodexNodeCommand, EquipCodexTacticCommand, RemoveCodexTacticCommand })
            (command as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private IEnumerable<ShanheNodeDefinition> CurrentShanheChapterNodes(ShanheCampaignDefinition definition,
        IReadOnlyDictionary<string, int> depths, string? currentNodeId, IReadOnlySet<string> completed)
    {
        var state = _shanheSession?.Snapshot;
        var focusNode = state?.Phase == ShanheCampaignPhase.Map && state.AvailableNodes.Count > 0
            ? state.AvailableNodes.OrderBy(node => depths.GetValueOrDefault(node.Id)).First()
            : definition.Nodes.FirstOrDefault(node => node.Id == currentNodeId);
        int ChapterOf(ShanheNodeDefinition node) => node.ChapterNumber > 0 ? node.ChapterNumber
            : depths.GetValueOrDefault(node.Id) <= 4 ? 1 : depths.GetValueOrDefault(node.Id) <= 8 ? 2 : 3;
        // New scenarios publish explicit chapter metadata. The depth fallback preserves the
        // older saved local definitions without pretending their map has new chapter content.
        _shanheChapter = focusNode is null ? 1 : ChapterOf(focusNode);
        var available = state?.AvailableNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var north = completed.Contains("hulao-start") || available.Contains("hulao-start") || currentNodeId is "hulao-start" or "hulao";
        var south = completed.Contains("guangcheng-start") || available.Contains("guangcheng-start") || currentNodeId is "guangcheng-start" or "guangcheng";
        _shanheChapterHeading = _shanheChapter switch
        {
            1 => north ? "第一章 · 虎牢关" : south ? "第一章 · 广成关" : "第一章 · 关隘争锋",
            2 => completed.Contains("dagu") || currentNodeId is "dagu" or "caravan" ? "第二章 · 大谷关"
                : completed.Contains("huanyuan") || currentNodeId is "huanyuan" or "rumor" ? "第二章 · 轘辕关" : "第二章 · 西凉烽烟",
            _ => currentNodeId is "luoyang" or "suanzao" or "henei"
                ? "第三章 · " + definition.Nodes.Single(node => node.Id == currentNodeId).Name : "第三章 · 最后的营火"
        };
        var nodes = definition.Nodes.Where(node => !IsShanheRunInProgress || _shanheChapter == ChapterOf(node)).ToArray();
        var chapterTitle = focusNode?.ChapterTitle ?? nodes.FirstOrDefault(node => node.ChapterNumber == _shanheChapter)?.ChapterTitle;
        if (!string.IsNullOrWhiteSpace(chapterTitle)) _shanheChapterHeading = chapterTitle;
        _shanheChapterCompleted = nodes.Count(node => completed.Contains(node.Id));
        return nodes;
    }
}
