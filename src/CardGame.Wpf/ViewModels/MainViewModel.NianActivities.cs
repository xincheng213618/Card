using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool IsNianActivityModeSelection => SelectedModeDefinition?.NianActivity is not null;
    private bool IsNianActivitySnapshot => CurrentModeDefinition?.NianActivity is not null;
    private string NianActivitySetupText =>
        $"{SelectedTableMode.Name} · 联合军。你与两名人机搭档三对一挑战12体力年兽。首轮为初始形态，第二轮起每轮随机进入睿智、警觉、任性或暴怒，同组四种状态不重复，四种用尽后重新抽取。全队共享三次协力复活：复活队友弃置区域里的牌、复原武将牌、摸2张并回复至2体力。玩家固定五阶：体力及上限+2、起手牌+1、出杀次数+1、摸牌数+1，每局一次涅槃。神将保持神势力。开战后限时30分钟，暂停自动推进仍计时。";
    private string NianActivitySelectionDetailText => "五阶武将与神将 · 两名人机搭档 · 全队共享三次复活";
    private string NianActivityObjectiveText =>
        "你的目标：与两名联合军人机搭档合作击败年兽。第二轮起每轮更换状态，留意睿智弃牌、警觉回血、任性摸牌和暴怒强攻；全队共享三次协力复活。";
    private string NianActivityStageText
    {
        get
        {
            if (CurrentModeDefinition is not { NianActivity: { } activity, BossActivity: { } boss } || _snapshot is null)
                return string.Empty;
            var enemy = _snapshot.Players.SingleOrDefault(player => player.TeamId == boss.EnemyTeamId);
            var used = _game.Events.Select(entry => entry.Payload).OfType<NianRevivalPaidEvent>()
                .Select(fact => fact.UsedRevives).DefaultIfEmpty(0).Last();
            var remaining = Math.Max(0, activity.SharedRevives - used);
            var seconds = (Math.Max(0, _game.NianRemainingMilliseconds ?? 0) + 999) / 1000;
            return $"{enemy?.GeneralName ?? "年兽·初始"} · 共享复活 {remaining}/{activity.SharedRevives} · 剩余 {seconds / 60:00}:{seconds % 60:00}";
        }
    }
    private string NianActivityAliveText => CurrentModeDefinition?.BossActivity is { } activity
        ? $"联合军 {Seats.Count(seat => seat.IsAlive && seat.TeamId == activity.PlayerTeamId)} · 年兽 {Seats.Count(seat => seat.IsAlive && seat.TeamId == activity.EnemyTeamId)} 存活 · {NianActivityStageText}"
        : string.Empty;
    private static string NianActivityLobbyDescription => "五阶出征 · 两名人机队友\n四态逐轮轮换 · 全队共享三次复活";
    private static string[] NianActivityGuideSteps =>
    [
        "你与两名人机搭档三对一挑战年兽；玩家默认五阶，神将保持神势力，击败年兽获胜。",
        "首轮初始形态。第二轮起每轮换为睿智、警觉、任性、暴怒中的一种，同组不重复，四种用尽后开始新一组。换状态保留年兽体力和各区域。",
        "全队共享三次协力复活：复活的队友弃置区域里的牌、复原武将牌、摸2张并回复至2体力。涅槃是每名玩家独立的一次濒死技能。",
        "开战后限时30分钟，暂停自动推进仍消耗时间。保存保留剩余时间，关闭程序期间不补计离线时间。"
    ];

    private string NianSeatLabel(PlayerSnapshot player)
    {
        if (player.TeamId == CurrentModeDefinition!.BossActivity!.EnemyTeamId) return "年兽";
        var offset = (player.Seat - _snapshot.HumanSeat + _snapshot.Players.Count) % _snapshot.Players.Count;
        return offset is >= 0 and <= 2 ? new[] { "先锋", "中坚", "大将" }[offset] : "联合军";
    }
}
