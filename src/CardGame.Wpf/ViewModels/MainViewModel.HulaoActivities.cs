using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool IsHulaoActivityModeSelection => SelectedModeDefinition?.HulaoActivity is not null;
    private bool IsHulaoActivitySnapshot => CurrentModeDefinition?.HulaoActivity is not null;

    private string HulaoActivitySetupText =>
        $"{SelectedTableMode.Name} · 联合军。你与两名人机搭档三对一迎战神吕布：先战最强神话，再战随机出现的暴怒战神或神鬼无前。吕布体力降至4或首次洗牌时立即变身并中断当前回合；第二次洗牌后，当前回合结束时联军败北。玩家固定五阶：体力上限+2、起手牌+1、出杀次数+1、摸牌数+1，获得一次涅槃（回复3点体力、摸3张牌）。手中武器可在出牌阶段重铸；队友阵亡时，其余存活队友回复1点体力并摸2张牌。";

    private string HulaoActivityStageText
    {
        get
        {
            if (CurrentModeDefinition is not { BossActivity: { } boss, HulaoActivity: { } hulao } || _snapshot is null)
                return string.Empty;
            var stage = _game.Events.Select(e => e.Payload).OfType<BossActivityStageStartedEvent>()
                .Select(e => e.StageIndex).DefaultIfEmpty(0).Last();
            var shuffleCount = _game.Events.Select(e => e.Payload).OfType<HulaoDeckShuffledEvent>()
                .Select(e => e.ShuffleNumber).DefaultIfEmpty(0).Last();
            var enemy = _snapshot.Players.SingleOrDefault(player => player.TeamId == boss.EnemyTeamId);
            var form = enemy is { GeneralId.Length: > 0 } ? enemy.GeneralName
                : stage >= 0 && stage < boss.Stages.Count ? boss.Stages[stage].Name : "神吕布";
            return $"第 {stage + 1}/{boss.Stages.Count} 阶段 · {form} · 洗牌 {shuffleCount}/{hulao.ShuffleLimit}";
        }
    }

    private string BossActivityRosterText => IsHulaoActivitySnapshot
        ? "联合军三席 · 两名人机搭档 · 双形态神吕布"
        : "联合军三席 · 两名人机搭档 · 四关连续迎战鬼将";
    private string BossActivitySelectionDetailText => IsHulaoActivitySnapshot
        ? "五阶普通武将 · 两名人机搭档 · 三英合战神吕布"
        : "五阶普通武将 · 两名人机搭档 · 四关连续挑战";
    private string BossActivityObjectiveText => IsHulaoActivitySnapshot
        ? $"你的目标：与两名联合军人机搭档合作，击败第二形态神吕布；第二次洗牌后的回合结束前必须取胜。当前{BossActivityStageText}。"
        : $"你的目标：与两名联合军人机搭档合作，连续击败四关鬼将；当前{BossActivityStageText}。";
    private string BossActivityArenaTitle => IsHulaoActivitySnapshot ? "虎 牢 关" : "驱 鬼 逐 邪";
    private string BossActivityEnemyLabel => IsHulaoActivitySnapshot ? "神吕布" : "鬼军";
    private string BossActivityGuideTitle => IsHulaoActivityModeSelection
        ? "虎牢关 · 五阶联合军" : "驱鬼逐邪 · 五阶联合军";
    private string[] BossActivityGuideSteps => IsHulaoActivityModeSelection
        ? HulaoActivityGuideSteps
        : ["你与两名人机搭档组成联合军，对抗一名鬼将。",
            "连续挑战小鬼、牛头马面、黑白无常、罗刹夜叉四关；联合军状态跨关保留。",
            "每名玩家固定五阶，并有一次濒死时发动的涅槃；击败最后一关鬼将获胜。"];
    private static string[] HulaoActivityGuideSteps =>
        ["你与两名人机搭档组成联合军，按先锋、中坚、大将、神吕布的顺序行动；玩家默认五阶。",
            "吕布体力不大于4或首次洗牌时中断当前回合并随机进入第二形态；体力触发由先锋先动，洗牌触发由吕布先动。",
            "出牌阶段可以重铸手中的武器。联军阵亡时其余存活队友回复1并摸2；第二次洗牌后的当前回合结束时联军败北。"];

    private static string BossActivityLobbyDescription(ContentModeDefinition mode) => mode.HulaoActivity is not null
        ? "五阶出征 · 两名人机队友\n双形态神吕布 · 武器重铸与洗牌限时"
        : $"五阶出征 · 两名人机队友\n{mode.BossActivity!.Stages.Count} 关接战 · 逐关挑战首领";
}
