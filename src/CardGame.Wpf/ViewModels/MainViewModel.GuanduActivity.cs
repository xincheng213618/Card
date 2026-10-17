using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly IReadOnlyList<StartingTeamOption> GuanduStartingTeams =
    [new("曹操军", "team:cao", "与三名人机同袍击败袁军主帅"), new("袁绍军", "team:yuan", "与三名人机同袍击败曹军主帅")];
    public IReadOnlyList<GuanduRoleOption> GuanduRoles { get; } = Array.AsReadOnly(new[]
    {
        new GuanduRoleOption(GuanduSeatRole.General, "部将", "从本军候选中选择武将，与人机主帅协作"),
        new GuanduRoleOption(GuanduSeatRole.Commander, "主帅", "使用本军固定主帅：界曹操或界袁绍")
    });
    private GuanduRoleOption? _selectedGuanduRole;
    public GuanduRoleOption SelectedGuanduRole
    {
        get => _selectedGuanduRole ?? GuanduRoles[0];
        set
        {
            if (value is null || !GuanduRoles.Contains(value) || !SetProperty(ref _selectedGuanduRole, value)) return;
            RaisePropertyChanged(nameof(TeamModeSetupText));
            RefreshPlayerGuide();
        }
    }
    public bool IsGuanduActivityModeSelection => SelectedModeDefinition?.GuanduActivity is not null;
    private bool IsGuanduActivitySnapshot => _snapshot?.GuanduActivity is not null;
    public bool IsGuanduActivityTable => IsGuanduActivitySnapshot;
    public bool IsGuanduGuide => IsNewGameSetupOpen ? IsGuanduActivityModeSelection : IsGuanduActivitySnapshot;
    private static string GuanduLobbyDescription => "曹袁各四席 · 三名AI队友，四名AI对手\n双固定主帅 · 九种事件 · 玩家默认本地五阶";
    private string GuanduSetupText => $"{SelectedStartingTeam.Name} · {SelectedGuanduRole.Name}。曹、袁两军各四人，你与三名人机同袍迎战四名人机敌军；主帅固定界曹操与界袁绍，主帅体力上限另加1并拥有主公技。击败敌方主帅获胜；牌堆耗尽则玩家方失败。每局抽取一个随机事件。全体武将默认采用本地五阶设定：体力及上限+2、起手+1、出杀次数+1、摸牌数+1、一次涅槃。本地提供五种随机座次。";
    private static string GuanduRosterText => "曹袁各四席 · 三名AI队友 · 四名AI对手 · 两名固定主帅";
    private static string GuanduObjectiveText => "你的目标：保护己方主帅，与三名人机同袍击败敌方主帅。牌堆耗尽则玩家方失败，无须消灭所有部将。";
    private string GuanduSelectionDetail => SelectedGuanduRole.Role == GuanduSeatRole.Commander ? "固定主帅 · 无须改选普通部将 · 本地五阶" : "本军部将候选 · 仅自己可见 · 本地五阶";
    private string GuanduSelectionHealth(ContentGeneralDefinition general) => $"五阶体力上限 {general.BaseHp + 2 + (_snapshot?.GuanduActivity?.HumanRole == GuanduSeatRole.Commander ? 1 : 0)}";
    private string GuanduSeatLabel(PlayerSnapshot player) => _snapshot?.GuanduActivity is { } state
        ? $"{PublicTeamName(player.TeamId ?? string.Empty)}·{(player.Seat == state.PlayerCommanderSeat || player.Seat == state.OpponentCommanderSeat ? "主帅" : "部将")}" : string.Empty;
    public string GuanduRoundText => _snapshot?.GuanduActivity is { } state ? $"第 {state.RoundNumber} 轮 · {GuanduEventName(state.Event)}" : "九种随机事件 · 待开战";
    public string GuanduEventText => _snapshot?.GuanduActivity is { } state ? GuanduEventDescription(state.Event) : "每局固定抽取一项事件，整局生效。";
    public string GuanduCommanderText => _snapshot?.GuanduActivity is { } state
        ? $"己方主帅 {state.PlayerCommanderSeat + 1}号位 · 敌方主帅 {state.OpponentCommanderSeat + 1}号位 · {(state.DeckExhausted ? "牌堆已耗尽，玩家方败北" : "牌堆耗尽即玩家方失败")}" : string.Empty;
    private string GuanduAliveText => $"己方 {Seats.Count(p => p.IsAlive && p.TeamId == HumanPlayer?.TeamId)}/4 · 敌方 {Seats.Count(p => p.IsAlive && p.TeamId != HumanPlayer?.TeamId)}/4 存活 · {GuanduCommanderText}";
    private string GuanduGameOverText => _snapshot?.GuanduActivity?.DeckExhausted == true ? "牌堆已耗尽 · 玩家方败北" : _snapshot?.Winner == Winner.Draw ? "本局平局结束" : _snapshot?.WinnerTeamId is { } team ? $"{PublicTeamName(team)}获胜 · {(team == _snapshot.GuanduActivity?.PlayerTeamId ? "敌方" : "己方")}主帅已阵亡" : "官渡之战结束";
    public string GuanduBasicsText => "官方曹操一号位先行动；本地牌桌随你的位置旋转，仍由曹操先行。双方各一名固定主帅、三名部将。击败敌军主帅即可获胜，牌堆摸完则玩家军失败。击杀敌军获得其所有手牌；击杀友军弃置自己的所有牌。";
    private static string[] GuanduGuideSteps =>
    ["选择曹操军或袁绍军，以及主帅或部将；同军另外三席由人机操控，敌军四席均由人机操控。",
     "主帅固定界曹操、界袁绍，拥有主公技并增加1点体力上限。全体武将默认使用本地五阶设定。",
     "九种随机事件每局抽取一种，整局生效。留意桌面事件说明；击败敌方主帅，无须清空敌军部将。",
     "牌堆耗尽玩家军失败。使用屯粮、援军、勠力同心和酗酒；屯粮可选自己，援军只选已受伤的其他角色。勠力同心点一人即选择其整军，也可不选目标直接重铸。"];
    private static string GuanduEventName(GuanduBattleEvent value) => value switch
    {
        GuanduBattleEvent.TenWinsTenLosses => "十胜十败", GuanduBattleEvent.WuchaoFire => "火烧乌巢",
        GuanduBattleEvent.GrainShortage => "粮草匮乏", GuanduBattleEvent.SlayTheGenerals => "斩颜良诛文丑",
        GuanduBattleEvent.HoldTheLine => "坚守待战", GuanduBattleEvent.Standoff => "两军相持",
        GuanduBattleEvent.SlowAdvance => "徐图缓进", GuanduBattleEvent.Underdog => "以弱胜强",
        GuanduBattleEvent.Arrogance => "恃宠而骄", _ => value.ToString()
    };
    private static string GuanduEventDescription(GuanduBattleEvent value) => value switch
    {
        GuanduBattleEvent.TenWinsTenLosses => "整局每第十张使用牌，若非装备或延时锦囊，结算后对仍合法的原目标再生效一次。",
        GuanduBattleEvent.WuchaoFire => "无属性伤害改为火焰伤害。",
        GuanduBattleEvent.GrainShortage => "摸牌阶段少摸1张；用牌造成伤害后摸1张，每张牌限一次。",
        GuanduBattleEvent.SlayTheGenerals => "回合开始选择另一角色，视为对其使用决斗，否则失去1点体力。",
        GuanduBattleEvent.HoldTheLine => "可将杀当闪使用或打出；每轮首次如此做，下个弃牌阶段手牌上限减1。",
        GuanduBattleEvent.Standoff => "前四轮手牌上限增加当前轮数；第五轮起，己回合首次使用杀造成的伤害加1。",
        GuanduBattleEvent.SlowAdvance => "本回合出牌阶段未使用或打出杀，下回合摸牌阶段多摸1张。",
        GuanduBattleEvent.Underdog => "受伤者体力高于伤害来源时，此伤害加1。",
        GuanduBattleEvent.Arrogance => "结束阶段，体力全场唯一最多则弃1牌；弃牌及触发结算后独立比较手牌，最多（含并列）则失去1体力。",
        _ => string.Empty
    };
    private void RefreshGuanduPresentation()
    {
        foreach (var name in new[] { nameof(IsGuanduActivityModeSelection), nameof(IsGuanduActivityTable), nameof(IsGuanduGuide), nameof(GuanduRoundText), nameof(GuanduEventText), nameof(GuanduCommanderText) }) RaisePropertyChanged(name);
    }
}

public sealed record GuanduRoleOption(GuanduSeatRole Role, string Name, string Description);
