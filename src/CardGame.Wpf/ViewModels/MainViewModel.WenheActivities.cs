using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly IReadOnlyList<WenheAspirationOption> AllWenheAspirations = Array.AsReadOnly(new[]
    {
        new WenheAspirationOption(1, "冠军", "争取最后存活；获得一次「志在必得」，出牌阶段摸2张牌"),
        new WenheAspirationOption(2, "前二", "争取进入前2名"),
        new WenheAspirationOption(4, "前四", "争取进入前4名"),
        new WenheAspirationOption(6, "前六", "争取进入前6名")
    });
    private WenheAspirationOption _selectedWenheAspiration = AllWenheAspirations[0];
    public IReadOnlyList<WenheAspirationOption> WenheAspirations => AllWenheAspirations;
    public WenheAspirationOption SelectedWenheAspiration
    {
        get => _selectedWenheAspiration;
        set
        {
            if (value is null || !AllWenheAspirations.Contains(value) || !SetProperty(ref _selectedWenheAspiration, value)) return;
            RaisePropertyChanged(nameof(TeamModeSetupText));
            RefreshPlayerGuide();
        }
    }
    public bool IsWenheActivityModeSelection => SelectedModeDefinition?.WenheActivity is not null;
    private bool IsWenheActivitySnapshot => CurrentModeDefinition?.WenheActivity is not null;
    public bool IsWenheActivityTable => IsWenheActivitySnapshot;
    public bool IsWenheAspirationSelection => SelectedModeDefinition?.WenheActivity is { EnableAspirations: true };
    public bool ShowModeSetupText => IsTeamModeSelection || IsWenheActivityModeSelection;
    public bool IsWenheGuide => IsNewGameSetupOpen ? IsWenheActivityModeSelection : IsWenheActivitySnapshot;

    private static string WenheActivityLobbyDescription => "八人各自求生 · 七名AI对手\n玩家默认五阶 · 七种轮次规则与个人排名";
    private string WenheActivitySetupText =>
        "八人各自求生，你独自迎战七名AI对手，按阵亡先后确定名次，最后存活者获得第一名。首轮固定乱武，之后每轮开始重新抽取并公布本轮规则。击杀其他角色增加1点体力上限（不回复体力）并摸3张牌；重赏轮奖励翻倍。八名角色统一使用本地五阶：体力及上限+2、起手五张、出杀次数+1、摸牌数+1，每人一次涅槃（回复3点体力并摸3张）。" +
        (IsWenheAspirationSelection ? $"本局免费志向：{SelectedWenheAspiration.Name}；只有冠军志向获得一次「志在必得」。" : string.Empty);
    private static string WenheActivitySelectionDetailText => "独立求生 · 七名AI对手 · 本地五阶 · 起手五张";
    private static string WenheActivityObjectiveText => "你的目标：独自存活到最后，取得第一名；其余七人都是AI对手。角色阵亡后名次即确定，你仍可继续观战。";
    public string WenheRoundText => _snapshot?.WenheRule is { } current
        ? $"第 {current.RoundNumber} 轮 · {WenheRuleName(current.Rule)}" : "首轮乱武 · 待开战";
    public string WenheCurrentRuleDescription => _snapshot?.WenheRule is { } current
        ? WenheRuleDescription(current.Rule) : "第一轮固定乱武；以后每轮重新抽取一条规则。";
    public string WenheAspirationText
    {
        get
        {
            if (_snapshot?.WenheAspiration is not { } aspiration) return "本局未启用志向";
            var result = WenheAspirationResultText(_snapshot, aspiration);
            var mark = aspiration.GoalRank != 1 ? string.Empty : aspiration.ChampionMarkUsed
                ? " · 志在必得已使用" : aspiration.CanUseChampionMark ? " · 志在必得可用" : " · 志在必得未使用";
            return $"你的志向：{WenheGoalName(aspiration.GoalRank)}{mark}{result}";
        }
    }
    private static string WenheAspirationResultText(GameSnapshot snapshot, WenheAspirationSnapshot aspiration)
    {
        var human = snapshot.Players.SingleOrDefault(player => player.IsHuman);
        if (human?.SurvivalRank is not { } rank) return string.Empty;
        var achieved = aspiration.GoalRank == 1
            ? snapshot.Winner == Winner.SoleSurvivor && snapshot.WinnerSeat == human.Seat
            : rank <= aspiration.GoalRank;
        return achieved ? " · 志向已达成" : " · 志向未达成";
    }
    private string WenheActivityAliveText => $"存活 {Seats.Count(seat => seat.IsAlive)}/{Seats.Count} · 各自求生" +
        (_snapshot?.Players.SingleOrDefault(player => player.IsHuman)?.SurvivalRank is { } rank ? $" · 你第{rank}名" : string.Empty);
    private string WenheGameOverText
    {
        get
        {
            var champion = _snapshot?.WinnerSeat is { } seat ? _snapshot.Players.SingleOrDefault(player => player.Seat == seat) : null;
            var outcome = _snapshot?.Winner == Winner.Draw ? "本局平局结束" : champion is null ? "本局结束" : $"{champion.GeneralName}获得第一名";
            return _snapshot?.WenheAspiration is null ? outcome : $"{outcome} · {WenheAspirationText}";
        }
    }
    private string WenheSpectatorHint => _snapshot?.Players.SingleOrDefault(player => player.IsHuman)?.SurvivalRank is { } rank
        ? $"你已获得第{rank}名 · {(IsAutoAdvance ? $"{SelectedPlaybackSpeed.Name}观战中，可暂停自动推进" : "观战已暂停，可点击「快速观战」继续")}"
        : "你已阵亡，可继续观战至冠军确定。";
    public string WenheBasicsText => "八人各自求生，最后存活者第一名，阵亡顺序决定其余名次。其余七名角色均由AI控制。玩家统一使用本地五阶，起手五张并各有一次濒死涅槃。志向只对自己可见；冠军志向的「志在必得」使用后才公开。";
    public string WenheRulesOverview => string.Join("\n\n", Enum.GetValues<WenheRuleKind>()
        .Select(rule => $"{WenheRuleName(rule)}：{WenheRuleDescription(rule)}"));
    private static string[] WenheActivityGuideSteps =>
    [
        "八人独立求生，其余七人都是AI对手。按阵亡先后排名，最后存活者第一名；玩家使用本地统一五阶。",
        "第一轮固定乱武，随后每轮随机公布一条规则。牌桌会显示当前轮次、规则名称与效果。",
        "击杀其他角色增加1点体力上限并摸3张，体力不会随上限增加而回复；重赏轮击杀奖励翻倍。",
        "本地可免费选择冠军、前二、前四或前六志向。冠军志向有一次出牌阶段摸2张的「志在必得」；使用前仅自己可见。"
    ];
    private static string WenheGoalName(int rank) => rank switch { 1 => "冠军", 2 => "前二", 4 => "前四", 6 => "前六", _ => $"前{rank}" };
    private static string WenheRuleName(WenheRuleKind rule) => rule switch
    {
        WenheRuleKind.Chaos => "乱武", WenheRuleKind.Bounty => "重赏", WenheRuleKind.LastStand => "破釜沉舟",
        WenheRuleKind.Equipment => "横刀跃马", WenheRuleKind.Sweep => "横扫千军", WenheRuleKind.Famine => "饿莩载道",
        WenheRuleKind.Poison => "宴安鸩毒", _ => rule.ToString()
    };
    private static string WenheRuleDescription(WenheRuleKind rule) => rule switch
    {
        WenheRuleKind.Chaos => "从随机角色起依次行动：向距离最近的另一名角色使用杀，否则失去1点体力。",
        WenheRuleKind.Bounty => "本轮击杀其他角色时，增加体力上限和摸牌的奖励翻倍。",
        WenheRuleKind.LastStand => "每个回合开始时，当前角色失去1点体力并摸3张牌。",
        WenheRuleKind.Equipment => "每个回合结束时，装备最少的所有角色各失去1点体力，并随机置入一件装备。",
        WenheRuleKind.Sweep => "本轮即将造成的每次伤害增加1点。",
        WenheRuleKind.Famine => "本轮结束时，手牌最少的所有角色各失去等于当前轮数的体力。",
        WenheRuleKind.Poison => "本轮桃均视为毒。毒正面朝上离开手牌区时，其原持有者失去1点体力。",
        _ => string.Empty
    };
    private static string WenheSeatLabel(PlayerSnapshot player) => player.SurvivalRank is { } rank
        ? $"第{rank}名" : player.IsHuman ? "求生者" : "对手";
    private bool IsVisibleWenheSkill(PlayerSnapshot player, string? id) => !IsWenheActivitySnapshot || (id switch
    {
        "wenhe:rules" or "wenhe:chaos-resolution" => false,
        "wenhe:champion-mark" => player.WenheChampionMarkUsed == true ||
            player.IsHuman && _snapshot.WenheAspiration?.GoalRank == 1,
        _ => true
    });
    private IReadOnlyList<GeneralSkillDefinition>? WenheDisplaySkills(PlayerSnapshot? player) => player?.Skills is not { } skills
        ? null : !IsWenheActivitySnapshot ? skills : skills.Where(skill => IsVisibleWenheSkill(player, skill.ContentId)).ToArray();
}

public sealed record WenheAspirationOption(int GoalRank, string Name, string Description);
