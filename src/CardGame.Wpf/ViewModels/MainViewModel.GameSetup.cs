using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isNewGameSetupOpen;
    private bool _isIdentityRevealOpen;
    private bool _manualDiscardEnabled = true;
    private StartingRoleOption _selectedStartingRole = new("主公", Role.Lord, "率领忠臣，平定叛乱");
    private TableModeOption _selectedTableMode = null!;
    private StartingTeamOption _selectedStartingTeam = null!;
    private DeckOption? _selectedDeck;
    private ModeCategoryOption _selectedModeCategory = null!;

    public IReadOnlyList<ModeCategoryOption> ModeCategories { get; } =
    [
        new("all", "全部", "全部可用模式"),
        new("identity", "身份", "经典身份与技能演示"),
        new("team", "阵营", "公开队伍协作对抗"),
        new("national", "国战", "双将暗置试验模式")
    ];
    public ObservableCollection<TableModeOption> VisibleTableModes { get; } = [];
    public ModeCategoryOption SelectedModeCategory
    {
        get => _selectedModeCategory;
        set
        {
            if (!SetProperty(ref _selectedModeCategory, value)) return;
            RefreshVisibleTableModes();
            RaisePropertyChanged(nameof(ModeLobbySummary));
        }
    }
    public string ModeLobbySummary => $"{SelectedModeCategory.Description} · {VisibleTableModes.Count} 个入口";

    public IReadOnlyList<StartingTeamOption> StartingTeams { get; } =
    [new("青队", "team:blue", "与青队同伴并肩作战"), new("赤队", "team:red", "与赤队同伴并肩作战")];
    public StartingTeamOption SelectedStartingTeam
    {
        get => _selectedStartingTeam;
        set
        {
            if (!SetProperty(ref _selectedStartingTeam, value)) return;
            RaisePropertyChanged(nameof(TeamModeSetupText));
            RefreshPlayerGuide();
        }
    }

    public IReadOnlyList<StartingRoleOption> StartingRoles { get; } =
    [
        new("随机", null, "在开局时抽取身份"),
        new("主公", Role.Lord, "率领忠臣，平定叛乱"),
        new("忠臣", Role.Loyalist, "保护主公，消灭敌人"),
        new("反贼", Role.Rebel, "与同伴一起击败主公"),
        new("内奸", Role.Renegade, "伺机而动，成为最后赢家")
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
            if (!SetProperty(ref _selectedTableMode, value)) return;
            RaisePropertyChanged(nameof(IsTeamModeSelection));
            RaisePropertyChanged(nameof(IsIdentityModeSelection));
            RaisePropertyChanged(nameof(IsNationalModeSelection));
            RaisePropertyChanged(nameof(IsClassicIdentityModeSelection));
            RaisePropertyChanged(nameof(SetupIdentityLabel));
            RaisePropertyChanged(nameof(TeamModeSetupText));
            RefreshPlayerGuide();
        }
    }

    public bool IsTeamModeSelection => IsSelectedTeamMode;
    public bool IsIdentityModeSelection => !IsTeamModeSelection && !IsNationalModeSelection;
    public bool IsClassicIdentityModeSelection =>
        SelectedTableMode?.ModeId.StartsWith("identity:classic-", StringComparison.Ordinal) == true;
    public string SetupIdentityLabel => IsNationalModeSelection ? "暗置与明置" : IsTeamModeSelection ? "你的阵营" : "你的身份";
    public string DeckSetupText => SelectedDeck is null
        ? string.Empty
        : $"{SelectedDeck.Name}：{SelectedDeck.Description}";
    public string TeamModeSetupText =>
        $"你将加入{SelectedStartingTeam.Name}（2 人）。双方阵营公开，击败另一队全部角色即可获胜。";

    public bool ManualDiscardEnabled { get => _manualDiscardEnabled; set => SetProperty(ref _manualDiscardEnabled, value); }
    public bool IsNewGameSetupOpen
    {
        get => _isNewGameSetupOpen;
        set { if (SetProperty(ref _isNewGameSetupOpen, value)) RefreshPlayerGuide(); }
    }
    public bool IsIdentityRevealOpen { get => _isIdentityRevealOpen; private set => SetProperty(ref _isIdentityRevealOpen, value); }
    public string IdentityRevealTitle => IsNationalSnapshot ? "势 力 揭 示" : IsTeamSnapshot ? "阵 营 揭 示" : "身 份 揭 示";
    public string IdentityRevealRole => HumanPlayer?.RoleLabel ?? "未知";
    public string IdentityRevealRoster => IsNationalSnapshot
        ? NationalSetupText
        : IsTeamSnapshot
            ? "青队 2 人 · 赤队 2 人 · 阵营公开"
            : _snapshot?.Players.Count == 5
                ? "主公 1 · 忠臣 1 · 反贼 2 · 内奸 1"
                : "主公 1 · 忠臣 2 · 反贼 4 · 内奸 1";
    public string TableModeText => IsTutorialActive
        ? "新手演练 · 四步基础操作"
        : IsNationalSnapshot ? NationalModeDisplayName
        : IsTeamSnapshot
            ? "2v2公开阵营 · 本地对战"
            : $"{(_snapshot?.Players.Count == 5 ? "五人" : "八人")}身份 · 本地对战";
    public string WindowTitle => IsTutorialActive
        ? $"群雄逐鹿 · 新手演练 {TutorialProgress}"
        : IsNationalSnapshot ? $"群雄逐鹿 · {NationalModeDisplayName}"
        : IsTeamSnapshot
            ? "群雄逐鹿 · 2v2公开阵营"
            : $"群雄逐鹿 · {(_snapshot?.Players.Count == 5 ? "五人" : "八人")}身份牌局";
    public string GeneralSelectionSubtitle => IsNationalSnapshot ? NationalSelectionSubtitle : IsTeamSnapshot
        ? $"你是{HumanPlayer?.RoleLabel} · 与 1 位队友合作，迎战 2 位对手"
        : $"你是{HumanPlayer?.RoleLabel} · 选择一位武将，迎战 {Seats.Count - 1} 位对手";
    public string GeneralSelectionFooter => IsNationalSnapshot
        ? "候选仅对你可见 · 双将暗置登场，可在出牌或杀响应时分别明置"
        : "候选仅对你可见 · 所有玩家选将结束后，武将同时亮相";
    public string IdentityObjective => IsNationalSnapshot ? $"你的目标：与{FactionName(_snapshot.Players.Single(player => player.IsHuman).FactionId)}势力同伴合作，消灭其他势力；未明置角色的势力尚不公开。" : IsTeamSnapshot
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
    private string NationalModeDisplayName => _snapshot?.Players.Count == 6
        ? "六人国战 M3 · 魏蜀野心家"
        : "四人国战 Lite · 魏蜀双将";
    public string TableArenaTitle => IsNationalSnapshot ? "国 战 · 暗 将" : IsTeamSnapshot ? "公 开 阵 营" : "身 份 场";
    public ICommand StartNewGameCommand { get; private set; } = null!;
    public ICommand StartNewGameFromLobbyCommand { get; private set; } = null!;
    public ICommand CancelNewGameSetupCommand { get; private set; } = null!;
    public ICommand ContinueFromIdentityRevealCommand { get; private set; } = null!;

    private void InitializeGameSetup()
    {
        RevealNationalGeneralCommand = new RelayCommand<NationalRevealChoice>(RevealNationalGeneral);
        SelectedModeCategory = ModeCategories[0];
        SelectedStartingTeam = StartingTeams[0];
        SelectedStartingRole = StartingRoles[1];
        SelectedTableMode = TableModes[0];
        SelectedDeck = DeckOptions.FirstOrDefault();
        RecommendDiscardCommand = new RelayCommand(RecommendDiscard);
        ContinueFromIdentityRevealCommand = new RelayCommand(() => IsIdentityRevealOpen = false);
        StartNewGameCommand = new RelayCommand(() => StartConfiguredGame(showOpeningDeal: false), () => !IsTutorialActive);
        StartNewGameFromLobbyCommand = new RelayCommand(() => StartConfiguredGame(showOpeningDeal: true), () => !IsTutorialActive);
        CancelNewGameSetupCommand = new RelayCommand(() => IsNewGameSetupOpen = false);
    }

    private void StartConfiguredGame(bool showOpeningDeal)
    {
        if (IsTutorialActive) return;
        if (showOpeningDeal) ArmOpeningDeal();
        IsNewGameSetupOpen = false;
        IsLogOpen = false;
        IsHelpOpen = false;
        NewGame();
        IsIdentityRevealOpen = true;
    }

    private void RefreshVisibleTableModes()
    {
        if (TableModes is null || SelectedModeCategory is null) return;
        var modes = TableModes.Where(mode => SelectedModeCategory.Id switch
        {
            "identity" => mode.ModeId.StartsWith("identity:", StringComparison.Ordinal),
            "team" => mode.ModeId.StartsWith("team:", StringComparison.Ordinal),
            "national" => mode.ModeId.StartsWith("national:", StringComparison.Ordinal),
            _ => true
        }).ToArray();
        VisibleTableModes.Clear();
        foreach (var mode in modes) VisibleTableModes.Add(mode);
        if (!modes.Contains(SelectedTableMode)) SelectedTableMode = modes.First();
        RaisePropertyChanged(nameof(ModeLobbySummary));
    }

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
        foreach (var name in new[] { nameof(TableModeText), nameof(WindowTitle), nameof(GeneralSelectionSubtitle), nameof(GeneralSelectionFooter), nameof(IdentityObjective), nameof(IdentityRevealTitle), nameof(IdentityRevealRole), nameof(IdentityRevealRoster), nameof(SetupIdentityLabel), nameof(TeamModeSetupText), nameof(TableArenaTitle) })
            RaisePropertyChanged(name);
    }
}

public sealed record StartingRoleOption(string Name, Role? Role, string Description);
public sealed record StartingTeamOption(string Name, string TeamId, string Description);
public sealed record TableModeOption(int PlayerCount, string Name, string Description, string ModeId)
{
    public string PlayerCountText => $"{PlayerCount} 人";
    public string ModeBadge => ModeId.StartsWith("national:", StringComparison.Ordinal) ? "双将试验"
        : ModeId.StartsWith("team:", StringComparison.Ordinal) ? "公开阵营"
        : ModeId.Contains("active-skills", StringComparison.Ordinal) ? "机制演示"
        : "经典身份";
}

public sealed record ModeCategoryOption(string Id, string Name, string Description);
public sealed record DeckOption(string DeckId, string Name, string Description);
