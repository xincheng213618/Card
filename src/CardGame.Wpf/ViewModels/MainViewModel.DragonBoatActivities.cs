using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly IReadOnlyList<StartingTeamOption> DragonBoatStartingTeams = Array.AsReadOnly(new[]
    {
        new StartingTeamOption("魏船", "team:wei", "与一名魏势力人机队友协作，迎战另外三船"),
        new StartingTeamOption("蜀船", "team:shu", "与一名蜀势力人机队友协作，迎战另外三船"),
        new StartingTeamOption("吴船", "team:wu", "与一名吴势力人机队友协作，迎战另外三船"),
        new StartingTeamOption("群船", "team:qun", "与一名群势力人机队友协作，迎战另外三船")
    });

    public bool IsDragonBoatActivityModeSelection => SelectedModeDefinition?.DragonBoatActivity is not null;
    private bool IsDragonBoatActivitySnapshot => CurrentModeDefinition?.DragonBoatActivity is not null;
    public bool IsDragonBoatActivityTable => IsDragonBoatActivitySnapshot;
    public bool CanExpandGeneralRoster => !IsDragonBoatActivitySnapshot && !IsGuanduActivitySnapshot;
    public bool IsDragonBoatGuide => IsNewGameSetupOpen ? IsDragonBoatActivityModeSelection : IsDragonBoatActivitySnapshot;
    public string DragonBoatBasicsText => "魏、蜀、吴、群各两人组成四条船。你与一名AI队友合作，其他六人是对手。对敌伤害积累至宝，上限12枚；在第二个实际轮次结束时争取船队第一名。所有人每次濒死都受艾叶守护回复至1，牌局以竞分排名结束。";
    public string DragonBoatCardsOverview => "粽：为自己或队友回复1点体力。雄黄酒：本回合下一张杀伤害+1。同舟共济：本队各摸牌，至宝最多时加成。力争上游：你按本队至宝摸牌（至多5），其他角色各摸1。逆水行舟：对敌造成1点伤害，再按本队至宝排名弃牌或摸牌。连弩允许额外使用三张杀。";
    private static string DragonBoatActivityLobbyDescription => "八人四队 · 一名AI队友，六名AI对手\n两轮争夺至宝 · 同分先到者领先 · 本地五阶";
    private string DragonBoatActivitySetupText =>
        $"{SelectedStartingTeam.Name} · 魏、蜀、吴、群各两人。你与一名AI队友合作，迎战六名AI对手。对敌每造成1点伤害，本队获得1枚龙船至宝，最多12枚；第二个实际轮次结束后按至宝数量排名，同分先到者领先。艾叶守护永久生效，每次濒死回复至1。唐咨满12枚只在终局令本队优先第一，不会提前结束。全员使用本地五阶：体力及上限+2、起手五张、出杀次数+1、摸牌数+1，并保留一次涅槃。";
    private static string DragonBoatActivityRosterText => "魏蜀吴群各两席 · 一名AI队友 · 六名AI对手 · 两轮至宝竞分";
    private static string DragonBoatActivitySelectionDetailText => "本势力六选一 · 队友候选可见 · 手牌仍然保密 · 本地五阶";
    private static string DragonBoatActivityObjectiveText => "你的目标：与同船AI队友争夺至宝，在第二个实际轮次结束时获得四船第一名。对敌伤害积累至宝；艾叶每次濒死回复至1。";
    private static string[] DragonBoatActivityGuideSteps =>
    [
        "选择魏、蜀、吴或群船，每船两人；你有一名AI队友，其余六人都是AI对手。按所选势力六选一，也能查看同船队友的候选。",
        "对敌每造成1点伤害，本队获得1枚至宝，最多12枚。对自己或队友造成伤害不会增加至宝；失去体力也不算伤害。",
        "第二个实际轮次结束并完成当前子结算后，按至宝数及同分到达先后排名。唐咨满12枚只改变终局第一名，不会提前结束。",
        "所有人永久拥有艾叶守护，每次濒死回复至1。使用五张专属牌与活动版苏飞、黄权、唐咨、刘琦，争取竞分优势。"
    ];

    private int DragonBoatActualRound => _game.Events.Select(entry => entry.Payload).OfType<RoundStartedEvent>()
        .Select(started => started.RoundNumber).DefaultIfEmpty(0).Last();
    public string DragonBoatRoundText => DragonBoatActualRound > 0
        ? $"第 {DragonBoatActualRound}/{CurrentModeDefinition?.DragonBoatActivity?.Rounds ?? 2} 轮 · 四船竞分" : "两轮竞分 · 待开赛";
    public string DragonBoatStandingRuleText => "至宝多者领先，同分先到者领先；初始同分按魏、蜀、吴、群排序。";
    public IReadOnlyList<DragonBoatStandingView> DragonBoatStandings => (_snapshot?.DragonBoatStandings ?? [])
        .OrderBy(team => team.Rank).Select(team => new DragonBoatStandingView(team.TeamId, team.Treasures,
            team.Rank, team.ReachedSequence, team.TangZiVictory, team.TeamId == HumanPlayer?.TeamId,
            (_snapshot!.DragonBoatStandings ?? []).Where(other => other.Treasures == team.Treasures)
                .OrderBy(other => other.ReachedSequence).ThenBy(other => DragonBoatTeamOrder(other.TeamId))
                .Select(other => other.TeamId).ToList().IndexOf(team.TeamId) + 1)).ToArray();
    public bool HasDragonBoatTeammateDraft => IsDragonBoatActivitySnapshot && _snapshot?.DragonBoatDraft is not null;
    public string DragonBoatTeammateDraftText => _snapshot?.DragonBoatDraft is { } draft
        ? $"同船队友 · {draft.TeammateSeat + 1}号位" + (draft.GeneralIds.Count == 1
            ? "已选武将" : $"的{draft.GeneralIds.Count}名候选（仅候选公开）") : string.Empty;
    public IReadOnlyList<DragonBoatDraftChoice> DragonBoatTeammateChoices => _snapshot?.DragonBoatDraft is not { } draft ? [] :
        draft.GeneralIds.Select(id => _game.ContentRegistry.Generals[id]).Select(general => new DragonBoatDraftChoice(
            general.Id, general.Name, $"五阶体力上限 {general.BaseHp + 2}",
            string.Join("\n", general.SkillIds.Select(id => _game.ContentRegistry.Skills[id])
                .Select(skill => $"{skill.Name}：{skill.Description}")))).ToArray();
    private string DragonBoatActivityAliveText => "四船各两人 · 一名AI队友，六名AI对手";
    private string DragonBoatGameOverText => _snapshot?.WinnerTeamId is { } teamId
        ? $"两轮竞分结束 · {DragonBoatTeamName(teamId)}获得第一名" : "龙舟竞分结束";
    private string DragonBoatGameOutcomeTitle => DragonBoatStandings.FirstOrDefault(team => team.IsHumanTeam) is { } human
        ? $"同船第 {human.Rank} 名" : "竞 分 结 束";
    private static string DragonBoatTeamName(string id) => id switch
    { "team:wei" => "魏船", "team:shu" => "蜀船", "team:wu" => "吴船", "team:qun" => "群船", _ => id };
    private static int DragonBoatTeamOrder(string id) => id switch
    { "team:wei" => 0, "team:shu" => 1, "team:wu" => 2, "team:qun" => 3, _ => 4 };
    private void RefreshDragonBoatPresentation()
    {
        foreach (var name in new[] { nameof(IsDragonBoatActivityTable), nameof(DragonBoatRoundText), nameof(DragonBoatStandings),
                     nameof(HasDragonBoatTeammateDraft), nameof(DragonBoatTeammateDraftText), nameof(DragonBoatTeammateChoices), nameof(IsDragonBoatGuide), nameof(CanExpandGeneralRoster) })
            RaisePropertyChanged(name);
    }
}

public sealed record DragonBoatStandingView(string TeamId, int Treasures, int Rank, long ReachedSequence,
    bool TangZiVictory, bool IsHumanTeam, int EqualScoreArrivalOrder)
{
    public string TeamName => TeamId switch { "team:wei" => "魏船", "team:shu" => "蜀船", "team:wu" => "吴船", "team:qun" => "群船", _ => TeamId };
    public string RankText => $"第{Rank}名";
    public string TreasureText => $"{Treasures}/12 枚";
    public string ArrivalText => ReachedSequence == 0 ? "初始同分 · 按队序" : $"同分到达第{EqualScoreArrivalOrder}位";
    public string RuleText => TangZiVictory ? "唐咨满12 · 终局优先" : IsHumanTeam ? "你的船队" : "对手船队";
}

public sealed record DragonBoatDraftChoice(string GeneralId, string Name, string HealthText, string Description);
