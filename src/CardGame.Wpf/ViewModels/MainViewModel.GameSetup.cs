using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isNewGameSetupOpen;
    private bool _isLobbyConfigurationOpen;
    private bool _isModeConfigurationOpen;
    private TableModeGroupOption? _selectedModeGroup;
    private readonly Dictionary<string, TableModeOption> _configuredModeVariants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StartingTeamOption> _configuredStartingTeams = new(StringComparer.Ordinal);
    private bool _isIdentityRevealOpen;
    private bool _manualDiscardEnabled = true;
    private StartingRoleOption _selectedStartingRole = new("随机身份", null, "开局随机分配身份");
    private TableModeOption _selectedTableMode = null!;
    private StartingTeamOption _selectedStartingTeam = null!;
    private DeckOption? _selectedDeck;
    private ModeCategoryOption _selectedModeCategory = null!;

    public IReadOnlyList<ModeCategoryOption> ModeCategories { get; } =
    [
        new("all", "全部", "全部可用模式"),
        new("identity", "身份", "五人或八人随机身份"),
        new("team", "阵营", "公开队伍协作对抗"),
        new("national", "国战", "双将暗置试验模式"),
        new("activity", "活动场", "单骑无双 · 剑阁决战 · 驱鬼逐邪 · 虎牢关 · 神武再世 · 挑战年兽 · 十殿阎罗 · 文和乱武 · 龙舟争渡 · 官渡之战")
    ];
    public ObservableCollection<TableModeOption> VisibleTableModes { get; } = [];
    public ObservableCollection<TableModeGroupOption> VisibleModeGroups { get; } = [];
    public IReadOnlyList<TableModeOption> VisibleModeVariants => SelectedModeGroup?.Variants ?? [];
    private IReadOnlyList<TableModeGroupOption> ModeGroups { get; set; } = [];
    public TableModeGroupOption? SelectedModeGroup => _selectedModeGroup;
    public bool IsModeConfigurationOpen
    {
        get => _isModeConfigurationOpen;
        private set
        {
            if (!SetProperty(ref _isModeConfigurationOpen, value)) return;
            if (value && SelectedModeGroup is { } group)
            {
                _configuredModeVariants[group.Id] = SelectedTableMode;
                if (IsTeamModeSelection && StartingTeams.Contains(SelectedStartingTeam))
                    _configuredStartingTeams[group.Id] = SelectedStartingTeam;
            }
            RaisePropertyChanged(nameof(IsModeSelectionOpen));
            RaisePropertyChanged(nameof(LobbySetupBackText));
        }
    }
    public bool IsModeSelectionOpen => !IsModeConfigurationOpen;
    public string LobbySetupBackText => IsModeConfigurationOpen ? "返回模式选择" : "返回大厅";
    public bool HasModeVariantChoices => VisibleModeVariants.Count > 1;
    public ModeCategoryOption SelectedModeCategory
    {
        get => _selectedModeCategory;
        set
        {
            if (value is null || !TableModes.Any(mode => MatchesModeCategory(mode, value.Id))) return;
            if (!SetProperty(ref _selectedModeCategory, value)) return;
            IsModeConfigurationOpen = false;
            RefreshVisibleTableModes();
            RaisePropertyChanged(nameof(ModeLobbySummary));
        }
    }
    public string ModeLobbySummary => $"{VisibleModeGroups.Count} 个模式 · 先选模式，再选难度与开局设置";

    private static readonly IReadOnlyList<StartingTeamOption> StandardStartingTeams =
    [new("青队", "team:blue", "与青队同伴并肩作战"), new("赤队", "team:red", "与赤队同伴并肩作战")];
    private static readonly IReadOnlyList<StartingTeamOption> ActivityStartingTeams =
    [new("蜀军", "team:shu", "守卫剑阁，福佑助战"), new("魏军", "team:wei", "进攻剑阁，狂骨助战")];
    public IReadOnlyList<StartingTeamOption> StartingTeams => StartingTeamsFor(SelectedModeDefinition);
    public StartingTeamOption SelectedStartingTeam
    {
        get => _selectedStartingTeam;
        set
        {
            if (value is null || !SetProperty(ref _selectedStartingTeam, value)) return;
            if (IsModeConfigurationOpen && IsTeamModeSelection && SelectedModeGroup is { } group && StartingTeams.Contains(value))
                _configuredStartingTeams[group.Id] = value;
            RaisePropertyChanged(nameof(TeamModeSetupText));
            RefreshPlayerGuide();
        }
    }

    public IReadOnlyList<StartingRoleOption> StartingRoles { get; } =
    [
        new("随机身份", null, "开局随机分配身份；主公固定为 1 号位，沿右手方向依次编号。"),
        new("主公", Role.Lord, "消灭所有反贼与内奸。"),
        new("忠臣", Role.Loyalist, "保护主公，消灭反贼与内奸。"),
        new("反贼", Role.Rebel, "击败主公。"),
        new("内奸", Role.Renegade, "先消灭其他人，最后击败主公。")
    ];
    public IReadOnlyList<TableModeOption> TableModes { get; private set; } = null!;
    public IReadOnlyList<DeckOption> DeckOptions { get; private set; } = null!;
    public DeckOption? SelectedDeck
    {
        get => _selectedDeck;
        set
        {
            if (!SetProperty(ref _selectedDeck, value)) return;
            RaisePropertyChanged(nameof(DeckSetupText));
            RefreshPlayerGuide();
        }
    }
    public StartingRoleOption SelectedStartingRole { get => _selectedStartingRole; set { if (SetProperty(ref _selectedStartingRole, value)) RefreshPlayerGuide(); } }
    public TableModeOption SelectedTableMode
    {
        get => _selectedTableMode;
        set
        {
            // WPF clears SelectedItem while its ItemsSource is being refreshed.
            // Keep a valid setup mode until the replacement list is ready.
            if (value is null) return;
            if (!SetProperty(ref _selectedTableMode, value)) return;
            RefreshSelectedModeGroup();
            // Category refreshes select a valid fallback while the cards are hidden.
            // Remember only choices made in configuration, for this UI session.
            if (IsModeConfigurationOpen && SelectedModeGroup is { } group)
                _configuredModeVariants[group.Id] = value;
            RaisePropertyChanged(nameof(StartingTeams));
            if (!StartingTeams.Contains(SelectedStartingTeam)) SelectedStartingTeam = StartingTeams[0];
            RaisePropertyChanged(nameof(IsActivityModeSelection));
            RaisePropertyChanged(nameof(IsTeamModeSelection));
            RaisePropertyChanged(nameof(IsWushuangModeSelection));
            RaisePropertyChanged(nameof(IsIdentityModeSelection));
            RaisePropertyChanged(nameof(IsNationalModeSelection));
            RaisePropertyChanged(nameof(IsClassicIdentityModeSelection));
            RaisePropertyChanged(nameof(IsWenheActivityModeSelection));
            RaisePropertyChanged(nameof(IsWenheAspirationSelection));
            RaisePropertyChanged(nameof(IsDragonBoatActivityModeSelection));
            RaisePropertyChanged(nameof(IsGuanduActivityModeSelection));
            RaisePropertyChanged(nameof(ShowModeSetupText));
            RaisePropertyChanged(nameof(SetupIdentityLabel));
            RaisePropertyChanged(nameof(TeamModeSetupText));
            RefreshPlayerGuide();
        }
    }

    public bool IsTeamModeSelection => IsSelectedTeamMode && !IsWushuangModeSelection;
    public bool IsActivityModeSelection => SelectedModeDefinition is { } selected && IsActivityDefinition(selected);
    private bool IsActivitySnapshot => CurrentModeDefinition is { } current && IsActivityDefinition(current);
    private string ActivityDisplayName => CurrentModeDefinition!.Name;
    private string PublicTeamName(string id) => IsWushuangTable ? id == WushuangMode.PlayerTeamId ? "挑战者" : "AI 对手" : IsDragonBoatActivitySnapshot ? DragonBoatTeamName(id) : id switch
    { "team:shanhe-player" => "义军", "team:shanhe-enemy" => "敌军", "team:cao" => "曹操军", "team:yuan" => "袁绍军", "team:wei" => "魏军", "team:shu" => "蜀军", "team:allies" => "联合军", "team:boss" => BossActivityEnemyLabel, "team:beasts" => "凶兽", "team:nian" => "年兽", "team:hell" => "阎罗", "team:blue" => "青队", "team:red" => "赤队", _ => id };
    public bool IsIdentityModeSelection => !IsWushuangModeSelection && !IsTeamModeSelection && !IsNationalModeSelection && !IsWenheActivityModeSelection;
    public bool IsClassicIdentityModeSelection =>
        SelectedTableMode?.ModeId.StartsWith("identity:classic-", StringComparison.Ordinal) == true;
    public string SetupIdentityLabel => IsWenheActivityModeSelection ? IsWenheAspirationSelection ? "本局志向 · 免费选择" : "独立求生" : IsNationalModeSelection ? "暗置与明置" : IsTeamModeSelection ? "你的阵营" : "你的身份";
    public string DeckSetupText => SelectedDeck is null
        ? string.Empty
        : $"{SelectedDeck.Name}：{SelectedDeck.Description}";
    public string TeamModeSetupText =>
        IsWushuangModeSelection ? "本地单机 1v1，对手由 AI 操控。单局15分钟；开局免费战法三选一，自然回合与新轮次分别获得虎符，无经验升级。回合结束进入20秒购买阶段，可花1虎符刷新或锁定整页。最多拥有两个额外技能，满槽购买需替换；战法不占槽，总倒计时继续。" : IsGuanduActivityModeSelection ? GuanduSetupText : IsDragonBoatActivityModeSelection ? DragonBoatActivitySetupText : IsWenheActivityModeSelection ? WenheActivitySetupText : IsHellActivityModeSelection ? HellActivitySetupText : IsNianActivityModeSelection ? NianActivitySetupText : IsShenwuActivityModeSelection ? ShenwuActivitySetupText
        : IsBossActivityModeSelection ? BossActivitySetupText
        : IsActivityModeSelection
            ? $"{SelectedTableMode.Name} · {SelectedStartingTeam.Name}。你与人机搭档、两具器械迎战两名英灵和两具器械；击败敌方英灵获胜。玩家固定五阶：体力上限+2、起手牌+1、出杀次数+1、摸牌数+1，获得一次涅槃（回复3点体力、摸3张牌）。万夫莫开仅对你和人机搭档生效：魏军狂骨，蜀军福佑。"
            : $"你将加入{SelectedStartingTeam.Name}（2 人）。双方阵营公开，击败另一队全部角色即可获胜。";

    public bool ManualDiscardEnabled { get => _manualDiscardEnabled; set => SetProperty(ref _manualDiscardEnabled, value); }
    public bool IsNewGameSetupOpen
    {
        get => _isNewGameSetupOpen;
        set
        {
            if (!SetProperty(ref _isNewGameSetupOpen, value)) return;
            IsLobbyConfigurationOpen = false;
            RaisePropertyChanged(nameof(WindowTitle));
            RefreshPlayerGuide();
        }
    }
    public bool IsLobbyConfigurationOpen
    {
        get => _isLobbyConfigurationOpen;
        private set
        {
            if (!SetProperty(ref _isLobbyConfigurationOpen, value)) return;
            IsModeConfigurationOpen = false;
        }
    }
    public ICommand OpenLobbyCategoryCommand { get; private set; } = null!;
    public ICommand BackToLobbyCommand { get; private set; } = null!;
    public ICommand OpenModeConfigurationCommand { get; private set; } = null!;
    public ICommand BackInLobbySetupCommand { get; private set; } = null!;
    public bool IsIdentityRevealOpen { get => _isIdentityRevealOpen; private set => SetProperty(ref _isIdentityRevealOpen, value); }
    public string IdentityRevealTitle => IsWushuangTable ? "单 骑 决 战" : IsShanheBattleSession ? "义 军 出 征" : IsWenheActivitySnapshot ? "各 自 求 生" : IsNationalSnapshot ? "势 力 揭 示" : IsTeamSnapshot ? "阵 营 揭 示" : "身 份 揭 示";
    public string IdentityRevealRole => IsWushuangTable ? "挑战者 · 对手为 AI" : HumanPlayer?.RoleLabel ?? "未知";
    public string IdentityRevealRoster => IsWushuangTable ? "你与 1 名 AI 对手 · 1v1 · 15 分钟 · 两个额外技能槽" : IsShanheBattleSession ? ShanheBattleRosterText : IsGuanduActivitySnapshot ? GuanduRosterText : IsDragonBoatActivitySnapshot ? DragonBoatActivityRosterText : IsWenheActivitySnapshot ? "八名独立求生者 · 七名AI对手 · 玩家默认五阶" : IsNationalSnapshot
        ? NationalSetupText
        : IsHellActivitySnapshot ? HellActivityRosterText
        : IsNianActivitySnapshot ? "联合军三席 · 两名人机搭档 · 逐轮轮换四态年兽"
        : IsShenwuActivitySnapshot ? "联合军三席 · 两名人机搭档 · 一名首领与两名烛阴"
        : IsBossActivitySnapshot ? BossActivityRosterText
        : IsActivitySnapshot ? "双方各四席 · 一名人机搭档 · 两具友军器械 · 两名敌方英灵"
        : IsTeamSnapshot
            ? "青队 2 人 · 赤队 2 人 · 阵营公开"
            : _snapshot?.Players.Count == 5
                ? "主公 1 · 忠臣 1 · 反贼 2 · 内奸 1"
                : "主公 1 · 忠臣 2 · 反贼 4 · 内奸 1";
    public string TableModeText => IsTutorialActive
        ? "新手演练 · 四步基础操作"
        : IsWushuangTable ? "单骑无双 · 单机 1v1 AI · 15 分钟"
        : IsShanheBattleSession ? $"山河图 · {ShanheBattleNodeName} · 单人征程"
        : IsGuanduActivitySnapshot ? $"{ActivityDisplayName} · {GuanduRoundText} · 本地五阶"
        : IsDragonBoatActivitySnapshot ? $"{ActivityDisplayName} · {DragonBoatRoundText} · 五阶"
        : IsWenheActivitySnapshot ? $"{ActivityDisplayName} · {WenheRoundText} · 五阶"
        : IsHellActivitySnapshot ? $"{ActivityDisplayName} · {HellActivityStageText} · 玩家五阶"
        : IsNianActivitySnapshot ? $"{ActivityDisplayName} · {NianActivityStageText} · 五阶"
        : IsShenwuActivitySnapshot ? $"{ActivityDisplayName} · {ShenwuActivityStageText} · 五阶"
        : IsBossActivitySnapshot ? $"{ActivityDisplayName} · {BossActivityStageText} · 五阶"
        : IsActivitySnapshot ? $"{ActivityDisplayName} · 万夫莫开 · 五阶"
        : IsNationalSnapshot ? NationalModeDisplayName
        : IsDualIdentitySnapshot ? "八人身份·双将 · 本地对战"
        : IsTeamSnapshot
            ? "2v2公开阵营 · 本地对战"
            : $"{(_snapshot?.Players.Count == 5 ? "五人" : "八人")}身份 · 本地对战";
    public string WindowTitle => IsNewGameSetupOpen ? "群雄逐鹿 · 游戏大厅" : IsTutorialActive
        ? $"群雄逐鹿 · 新手演练 {TutorialProgress}"
        : IsWushuangTable ? "群雄逐鹿 · 单骑无双"
        : IsShanheBattleSession ? $"群雄逐鹿 · 山河图 · {ShanheBattleNodeName}"
        : IsActivitySnapshot ? $"群雄逐鹿 · {ActivityDisplayName}"
        : IsNationalSnapshot ? $"群雄逐鹿 · {NationalModeDisplayName}"
        : IsDualIdentitySnapshot ? "群雄逐鹿 · 八人身份·双将牌局"
        : IsTeamSnapshot
            ? "群雄逐鹿 · 2v2公开阵营"
            : $"群雄逐鹿 · {(_snapshot?.Players.Count == 5 ? "五人" : "八人")}身份牌局";
    private PlayerSnapshot? GeneralSelectionViewer => _snapshot is
        { PendingDecision: { Kind: DecisionKind.SelectGeneral } pending } snapshot && pending.PlayerSeat == snapshot.HumanSeat
            ? snapshot.Players.FirstOrDefault(player => player.Seat == snapshot.HumanSeat)
            : null;
    public string GeneralSelectionContextTitle
    {
        get
        {
            if (GeneralSelectionViewer is not { } viewer) return string.Empty;
            if (IsWushuangTable) return "单骑无双 · 选择你的武将";
            if (IsShanheBattleSession) return $"山河图 · {ShanheBattleNodeName}";
            if (IsWenheActivitySnapshot) return "独立求生 · 选择你的武将";
            if (IsNationalSnapshot) return $"你的势力 · {FactionName(viewer.FactionId)}";
            var role = viewer.TeamId is { } team ? PublicTeamName(team) : viewer.Role is { } visibleRole ? GetRoleName(visibleRole) : "未揭示";
            return $"{(IsTeamSnapshot ? "你的阵营" : "你的身份")} · {role}";
        }
    }
    public string GeneralSelectionContextDetail
    {
        get
        {
            if (GeneralSelectionViewer is not { } viewer) return string.Empty;
            if (IsWushuangTable) return "单机 1v1 · 对手由 AI 操控 · 本局 15 分钟";
            if (IsShanheBattleSession) return ShanheBattleRosterText;
            if (IsNationalSnapshot)
                return viewer.GeneralId.Length == 0
                    ? "选择主将 · 双将同势力，暗置入场"
                    : $"主将 · {viewer.GeneralName} · 选择同势力副将";
            if (IsDualIdentitySnapshot)
                return viewer.GeneralId.Length == 0
                    ? "选择主将 · 势力与性别沿用主将"
                    : $"主将 · {viewer.GeneralName} · 选择副将（不限势力）";
            if (IsGuanduActivitySnapshot) return GuanduSelectionDetail;
            if (IsWenheActivitySnapshot) return WenheActivitySelectionDetailText;
            if (IsDragonBoatActivitySnapshot) return DragonBoatActivitySelectionDetailText;
            if (IsHellActivitySnapshot) return HellActivitySelectionDetailText;
            if (IsNianActivitySnapshot) return NianActivitySelectionDetailText;
            if (IsShenwuActivitySnapshot) return ShenwuActivitySelectionDetailText;
            if (IsBossActivitySnapshot) return BossActivitySelectionDetailText;
            if (IsActivitySnapshot) return "五阶普通武将 · 同阵营人机搭档 · 两具器械协作";
            if (IsTeamSnapshot) return "公开阵营 · 与 1 位队友协作";
            // These public flags remain authoritative even in the diagnostic
            // reveal-all view. Never infer a public general from its visible name.
            var lord = _snapshot.Players.FirstOrDefault(player => player.Role == Role.Lord &&
                player.IsRoleRevealed && player.IsGeneralPublic && !player.IdentityConcealed);
            return lord is not null
                ? $"主公 · {lord.GeneralName} · 1 号位"
                : viewer.Role == Role.Lord
                    ? "你先选将 · 武将将向全场公开"
                    : "主公尚未亮将 · 选择一名武将";
        }
    }
    public string GeneralSelectionSubtitle
    {
        get
        {
            if (IsWushuangTable) return "独自迎战1名AI · 初始战法与虎符商店 · 两个额外技能槽";
            if (IsShanheBattleSession) return $"{ShanheBattleNodeName} · {ShanheBattleRosterText}";
            if (IsNationalSnapshot) return NationalSelectionSubtitle;
            if (IsDualIdentitySnapshot)
                return $"你是{HumanPlayer?.RoleLabel} · 全员双将 · 两将体力平均向下取整，主公 +1";
            if (IsGuanduActivitySnapshot) return $"{ActivityDisplayName} · {PublicTeamName(HumanPlayer?.TeamId ?? SelectedStartingTeam.TeamId)} · 三名AI队友，四名AI对手 · 本地五阶";
            if (IsWenheActivitySnapshot) return $"{ActivityDisplayName} · 七名AI对手 · 按阵亡顺序排名 · 本地五阶";
            if (IsDragonBoatActivitySnapshot) return $"{ActivityDisplayName} · {PublicTeamName(HumanPlayer?.TeamId ?? SelectedStartingTeam.TeamId)} · 一名AI队友，六名AI对手 · 本地五阶";
            if (IsHellActivitySnapshot) return $"{ActivityDisplayName} · 联合军三人合作 · 两名队友由人机操控 · 玩家固定五阶";
            if (IsNianActivitySnapshot) return $"{ActivityDisplayName} · 联合军三人合作 · 两名队友由人机操控 · 默认五阶";
            if (IsShenwuActivitySnapshot) return $"{ActivityDisplayName} · 共享神将依次选取 · 两名队友由人机操控 · 默认五阶";
            if (IsBossActivitySnapshot) return $"{ActivityDisplayName} · 联合军三人合作 · 默认五阶，两名队友由人机操控";
            if (IsActivitySnapshot) return $"{ActivityDisplayName} · 仅限本阵营武将 · 默认五阶，队友由人机操控";
            if (IsTeamSnapshot) return $"你是{HumanPlayer?.RoleLabel} · 与 1 位队友合作，迎战 2 位对手";
            var lord = _snapshot?.Players.FirstOrDefault(player => player.Role == Role.Lord && player.IsGeneralPublic);
            return lord is null
                ? $"你是{HumanPlayer?.RoleLabel} · 选择一位武将，迎战 {Seats.Count - 1} 位对手"
                : $"你是{HumanPlayer?.RoleLabel} · 主公：{lord.GeneralName} · 选择一位武将";
        }
    }
    public string GeneralSelectionFooter => IsWushuangTable ? "原武将技能保留 · 最多两个额外技能 · 选将后开始限时决战" : IsShanheBattleSession ? ShanheCarryoverNotice : IsDragonBoatActivitySnapshot ? "你与队友的六名候选互相可见 · 其他船候选与所有队友手牌保持私密" : IsWenheActivitySnapshot ? "候选仅对你可见 · 选将结束后同时亮相 · 各自求生" : IsNationalSnapshot
        ? "候选仅对你可见 · 双将暗置登场，可在出牌或杀响应时分别明置"
        : IsDualIdentitySnapshot ? "两将技能同时生效 · 势力与性别沿用主将 · 主公先公开，其余开局亮相"
        : IsHellActivitySnapshot ? "候选仅对你可见 · 玩家固定五阶 · 四殿跨关保留状态"
        : IsNianActivitySnapshot ? "候选仅对你可见 · 神将保持神势力 · 全队共享三次协力复活"
        : IsShenwuActivitySnapshot ? "普通候选仅对你可见 · 神将候选三人共享，选择后从共享池移除"
        : IsTeamSnapshot ? "候选仅对你可见 · 所有玩家选将结束后，武将同时亮相"
        : "候选仅对你可见 · 主公先选并公开，其余玩家选完后亮相";
    public string IdentityObjective => IsWushuangTable ? "你的目标：独自击败AI对手。单局15分钟；开局免费选一个战法，回合结束使用虎符购买，额外技能最多两个。" : IsShanheBattleSession ? ShanheBattleObjective : IsGuanduActivitySnapshot ? GuanduObjectiveText : IsDragonBoatActivitySnapshot ? DragonBoatActivityObjectiveText : IsWenheActivitySnapshot ? WenheActivityObjectiveText : IsHellActivitySnapshot ? HellActivityObjectiveText : IsNianActivitySnapshot ? NianActivityObjectiveText : IsShenwuActivitySnapshot ? ShenwuActivityObjectiveText : IsBossActivitySnapshot ? BossActivityObjectiveText : IsActivitySnapshot ? $"你的目标：与{PublicTeamName(HumanPlayer?.TeamId ?? "team:shu")}人机搭档和两具器械合作，击败两名敌方英灵。" : IsNationalSnapshot ? $"你的目标：与{FactionName(_snapshot.Players.Single(player => player.IsHuman).FactionId)}势力同伴合作，消灭其他势力；未明置角色的势力尚不公开。" : IsTeamSnapshot
        ? $"你的目标：与{HumanPlayer?.RoleLabel}队友合作，击败{(HumanPlayer?.TeamId == "team:blue" ? "赤队" : "青队")}"
        : HumanPlayer?.RoleLabel switch
        {
            "主公" => "你的目标：消灭所有反贼与内奸",
            "忠臣" => "你的目标：保护主公，消灭反贼与内奸",
            "反贼" => "你的目标：击败主公",
            "内奸" => "你的目标：先消灭其他人，最后击败主公",
            _ => "选择身份，进入战局"
        };

    private bool IsTeamSnapshot => _snapshot?.Players.Any(player => player.TeamId is not null) == true;
    private bool IsDualIdentitySnapshot => CurrentModeDefinition?.DualGenerals == true;
    private bool HasDualGeneralSlots => IsNationalSnapshot || IsDualIdentitySnapshot;
    private string NationalModeDisplayName => _snapshot?.Players.Count == 6
        ? "六人国战 M3 · 魏蜀野心家"
        : "四人国战 Lite · 魏蜀双将";
    public string TableArenaTitle => IsWushuangTable ? "单 骑 无 双" : IsShanheBattleSession ? "山 河 图 · 征 程" : IsGuanduActivitySnapshot ? "官 渡 之 战" : IsDragonBoatActivitySnapshot ? "龙 舟 争 渡" : IsWenheActivitySnapshot ? "文 和 乱 武" : IsHellActivitySnapshot ? "十 殿 阎 罗" : IsNianActivitySnapshot ? "挑 战 年 兽" : IsShenwuActivitySnapshot ? "神 武 再 世" : IsBossActivitySnapshot ? BossActivityArenaTitle : IsActivitySnapshot ? "剑 阁 决 战" : IsNationalSnapshot ? "国 战 · 暗 将" : IsDualIdentitySnapshot ? "身 份 场 · 双 将" : IsTeamSnapshot ? "公 开 阵 营" : "身 份 场";
    public ICommand StartNewGameCommand { get; private set; } = null!;
    public ICommand StartNewGameFromLobbyCommand { get; private set; } = null!;
    public ICommand CancelNewGameSetupCommand { get; private set; } = null!;
    public ICommand ContinueFromIdentityRevealCommand { get; private set; } = null!;

    private void InitializeGameSetup()
    {
        ModeGroups = TableModes.GroupBy(mode => mode.GroupId, StringComparer.Ordinal)
            .Select(group => new TableModeGroupOption(group.Key, group.ToArray())).ToArray();
        RevealNationalGeneralCommand = new RelayCommand<NationalRevealChoice>(RevealNationalGeneral);
        SelectedModeCategory = ModeCategories[0];
        SelectedStartingTeam = StartingTeams[0];
        SelectedStartingRole = StartingRoles[0];
        SelectedTableMode = TableModes[0];
        SelectedDeck = DeckOptions.FirstOrDefault();
        RecommendDiscardCommand = new RelayCommand(RecommendDiscard);
        ContinueFromIdentityRevealCommand = new RelayCommand(() => IsIdentityRevealOpen = false);
        StartNewGameCommand = new RelayCommand(() => StartConfiguredGame(showOpeningDeal: false), () => !IsTutorialActive);
        StartNewGameFromLobbyCommand = new RelayCommand(() => StartConfiguredGame(showOpeningDeal: true), () => !IsTutorialActive);
        CancelNewGameSetupCommand = new RelayCommand(() => IsNewGameSetupOpen = false);
        OpenLobbyCategoryCommand = new RelayCommand<string>(categoryId =>
        {
            var category = ModeCategories.FirstOrDefault(item => item.Id == categoryId);
            if (category is null || !TableModes.Any(mode => MatchesModeCategory(mode, categoryId))) return;
            SelectedModeCategory = category;
            IsLobbyConfigurationOpen = true;
        }, categoryId => !IsTutorialActive && TableModes.Any(mode => MatchesModeCategory(mode, categoryId)));
        BackToLobbyCommand = new RelayCommand(() => IsLobbyConfigurationOpen = false);
        OpenModeConfigurationCommand = new RelayCommand<TableModeGroupOption>(group =>
        {
            if (group is null || !VisibleModeGroups.Contains(group)) return;
            _configuredStartingTeams.TryGetValue(group.Id, out var rememberedTeam);
            if (_configuredModeVariants.TryGetValue(group.Id, out var remembered) && group.Variants.Contains(remembered))
                SelectedTableMode = remembered;
            else if (!group.Variants.Contains(SelectedTableMode)) SelectedTableMode = group.Variants[0];
            // Browsing other categories can change the available teams. Restore only
            // a still-legal choice from this group's configuration in this UI session.
            if (rememberedTeam is not null && StartingTeams.Contains(rememberedTeam))
                SelectedStartingTeam = rememberedTeam;
            IsModeConfigurationOpen = true;
        });
        BackInLobbySetupCommand = new RelayCommand(() =>
        {
            if (IsModeConfigurationOpen) IsModeConfigurationOpen = false;
            else IsLobbyConfigurationOpen = false;
        });
    }

    private void StartConfiguredGame(bool showOpeningDeal)
    {
        if (IsTutorialActive) return;
        IsNewGameSetupOpen = false;
        IsLogOpen = false;
        IsHelpOpen = false;
        NewGame();
        if (showOpeningDeal) ArmOpeningDeal();
        IsIdentityRevealOpen = true;
    }

    private void RefreshVisibleTableModes()
    {
        if (TableModes is null || SelectedModeCategory is null) return;
        var modes = TableModes.Where(mode => MatchesModeCategory(mode, SelectedModeCategory.Id)).ToArray();
        var selection = modes.Contains(SelectedTableMode) ? SelectedTableMode : modes.First();
        VisibleTableModes.Clear();
        foreach (var mode in modes) VisibleTableModes.Add(mode);
        VisibleModeGroups.Clear();
        foreach (var group in ModeGroups.Where(group => modes.Contains(group.Variants[0]))) VisibleModeGroups.Add(group);
        SelectedTableMode = selection;
        // Reapply the selection even when the retained mode did not change.
        RaisePropertyChanged(nameof(SelectedTableMode));
        RaisePropertyChanged(nameof(ModeLobbySummary));
    }

    private void RefreshSelectedModeGroup()
    {
        var group = ModeGroups.FirstOrDefault(item => item.Variants.Contains(SelectedTableMode));
        if (group is null || ReferenceEquals(group, _selectedModeGroup)) return;
        _selectedModeGroup = group;
        RaisePropertyChanged(nameof(SelectedModeGroup));
        RaisePropertyChanged(nameof(VisibleModeVariants));
        RaisePropertyChanged(nameof(HasModeVariantChoices));
        // The old group's ItemsSource could not select this mode when SetProperty
        // first announced it. Reapply after the new difficulty cards exist.
        RaisePropertyChanged(nameof(SelectedTableMode));
    }

    private static bool MatchesModeCategory(TableModeOption mode, string categoryId) =>
        categoryId == "all" || categoryId == "activity" && mode.ModeId == WushuangMode.ModeId || mode.ModeId.StartsWith(categoryId + ":", StringComparison.Ordinal);

    private void OpenGameSetup()
    {
        if (IsTutorialActive) return;
        IsLogOpen = false;
        IsHelpOpen = false;
        IsNewGameSetupOpen = true;
    }

    private void RefreshGameSetupPresentation()
    {
        RefreshNationalPresentation();
        RefreshDragonBoatPresentation();
        RefreshGuanduPresentation();
        RefreshWushuangPresentation();
        foreach (var name in new[] { nameof(IsWenheActivityTable), nameof(WenheRoundText), nameof(WenheCurrentRuleDescription), nameof(WenheAspirationText), nameof(IsWenheGuide) })
            RaisePropertyChanged(name);
        foreach (var name in new[] { nameof(TableModeText), nameof(WindowTitle), nameof(GeneralSelectionContextTitle), nameof(GeneralSelectionContextDetail), nameof(GeneralSelectionSubtitle), nameof(GeneralSelectionFooter), nameof(IdentityObjective), nameof(IdentityRevealTitle), nameof(IdentityRevealRole), nameof(IdentityRevealRoster), nameof(SetupIdentityLabel), nameof(TeamModeSetupText), nameof(TableArenaTitle) })
            RaisePropertyChanged(name);
    }
}

public sealed record StartingRoleOption(string Name, Role? Role, string Description);
public sealed record StartingTeamOption(string Name, string TeamId, string Description);
public sealed record TableModeOption(int PlayerCount, string Name, string Description, string ModeId)
{
    // Group presentation entries by activity name while keeping every real mode ID
    // intact for setup, rules and saves, including newly registered editions.
    public string GroupId => ModeId.StartsWith("activity:", StringComparison.Ordinal) ? $"activity:{Name.Split('·')[0]}" : ModeId;
    public string DifficultyName => Name[(Name.IndexOf('·') + 1)..];
    public string PlayerCountText => $"{PlayerCount} 人";
    public string ModeBadge => ModeId == WushuangMode.ModeId ? "限时单挑" : ModeId == "activity:guandu-2023" ? "五阶军争"
        : ModeId == "activity:wenhe-2019" ? "五阶生存"
        : ModeId == "activity:dragon-boat-2018" ? "五阶竞舟"
        : ModeId.StartsWith("activity:", StringComparison.Ordinal) ? "五阶 PVE"
        : ModeId.StartsWith("national:", StringComparison.Ordinal) ? "双将试验"
        : ModeId == "identity:classic-dual-8" ? "双将身份"
        : ModeId.StartsWith("team:", StringComparison.Ordinal) ? "公开阵营"
        : ModeId.Contains("composed-skills", StringComparison.Ordinal) ? "配置组合"
        : ModeId.Contains("active-skills", StringComparison.Ordinal) ? "机制演示"
        : "经典身份";
}

public sealed record TableModeGroupOption(string Id, IReadOnlyList<TableModeOption> Variants)
{
    public string Name => Id.StartsWith("activity:", StringComparison.Ordinal) ? Variants[0].Name.Split('·')[0] : Variants[0].Name;
    public string Description => Name == "十殿阎罗" ? "玩家固定五阶 · 两名人机队友\n四殿与孟婆 · 隐藏地藏王" : Variants[0].Description;
    public string PlayerCountText => Variants[0].PlayerCountText;
    public string ModeBadge => Variants[0].ModeBadge;
    public string SelectionHint => Variants.Count > 1 ? $"{Variants.Count} 项难度 / 版本 · 点击选择 ›" : "选择版本与开局设置 ›";
}

public sealed record ModeCategoryOption(string Id, string Name, string Description);
public sealed record DeckOption(string DeckId, string Name, string Description);
