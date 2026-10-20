using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool IsShenwuActivityModeSelection => SelectedModeDefinition?.ShenwuActivity is not null;
    private bool IsShenwuActivitySnapshot => CurrentModeDefinition?.ShenwuActivity is not null;
    private string ShenwuActivitySetupText =>
        SelectedModeDefinition?.ShenwuActivity is { Edition: 2020 } activity
        ? $"{SelectedTableMode.Name} · 三名联合军挑战八兽之一及两名烛阴。首领包括混沌、穷奇、梼杌、饕餮、相柳、朱厌、毕方、英招。每人五名普通候选、三名共享神将候选。敌方{activity.EnemyTier}阶，二阶起随机装备，三阶起出杀+1，四阶起摸牌+1，五阶获得濒死觉醒【困兽】：回复至5血、手牌补至5、翻回正面并获得崩坏。玩家固定五阶：体力上限+2、起手+2、出杀+1、摸牌+1，开局随机装备，濒死可重生一次。首领首次半血永久获得隐藏技能；击败首领即可获胜。"
        :
        $"{SelectedTableMode.Name} · 联合军。你与两名人机搭档组成先锋、中坚、大将，挑战随机首领与两名烛阴。每人五名普通候选，另有三人共享的三名神将，已选神将不再可选。神将保持神势力并获得对应专属装备。击败首领即可获胜，无须消灭烛阴；首领首次降至半血时获得隐藏技能。玩家固定五阶：体力及上限+2、起手牌+1、出杀次数+1、摸牌数+1，每局一次涅槃。";
    private string ShenwuActivitySelectionDetailText =>
        "五名普通候选 · 三人共享神将候选 · 两名人机搭档 · 默认五阶";
    private string ShenwuActivityObjectiveText =>
        "你的目标：与两名联合军人机搭档合作，击败凶兽首领即可获胜，烛阴可以不消灭。首领首次降至半血时获得隐藏技能。";
    private string ShenwuActivityStageText
    {
        get
        {
            if (CurrentModeDefinition?.ShenwuActivity is not { } activity || _snapshot is null) return string.Empty;
            var leader = _snapshot.Players.SingleOrDefault(player => activity.LeaderPoolIds.Contains(player.GeneralId));
            if (leader is null) return "随机一位首领 · 单关挑战";
            var enraged = _game.Events.Select(entry => entry.Payload).OfType<ShenwuLeaderEnragedEvent>().Any();
            return $"首领 {leader.GeneralName} · {(activity.Edition == 2020 ? $"敌方{activity.EnemyTier}阶 · " : string.Empty)}{(enraged ? "隐藏技能已解锁" : "半血解锁隐藏技能")}";
        }
    }
    private string ShenwuActivityAliveText => CurrentModeDefinition?.ShenwuActivity is { } activity
        ? $"联合军 {Seats.Count(seat => seat.IsAlive && seat.TeamId == activity.PlayerTeamId)} · 首领 {Seats.Count(seat => seat.IsAlive && seat.RoleLabel == "首领")} · 烛阴 {Seats.Count(seat => seat.IsAlive && seat.RoleLabel == "烛阴")} 存活"
        : string.Empty;
    private static string ShenwuActivityLobbyDescription(ContentModeDefinition mode) => mode.ShenwuActivity is { Edition: 2020 } activity
        ? $"八兽首领 · 敌方{activity.EnemyTier}阶{(activity.EnemyTier == 5 ? " · 困兽觉醒" : string.Empty)}\n玩家五阶 · 两名人机队友 · 共享神将"
        : "五阶出征 · 两名人机队友\n共享神将与专属装备 · 随机凶兽首领";
    private string[] ShenwuActivityGuideSteps => (IsNewGameSetupOpen ? SelectedModeDefinition : CurrentModeDefinition)?.ShenwuActivity is { Edition: 2020 }
        ? ["三名联合军挑战八兽之一；在大厅选择敌方一至五阶，首领和两名烛阴使用同阶特权。",
            "半血授技：混沌隐贼、穷奇厌笃、梼杌乱常、饕餮积财、相柳恶臭、朱厌蓄势、毕方炎狱、英招司圃。",
            "敌方五阶的困兽在首次濒死时觉醒：回复至5血，手牌补至5，翻回正面并永久获得崩坏。",
            "玩家固定五阶，起手六张，开局随机装备。重生弃置判定牌、复原武将牌、手牌补至体力上限且至多5张、回复满体力。击败首领即可获胜。"]
        :
    [
        "先锋、中坚、大将依次选将：每人五名普通候选，三名神将由三人共享；神将保持神势力，开局装备对应专属牌。",
        "三名联合军迎战一名随机首领与两名烛阴；击败首领获胜，首领首次降至半血解锁隐藏技能。",
        "玩家击杀烛阴后回复1并摸3；联军阵亡后队友回复1并摸1，若阵亡者是神将则改为摸3。玩家默认五阶，每局可发动一次涅槃。"
    ];

    private string ShenwuSeatLabel(PlayerSnapshot player)
    {
        var activity = CurrentModeDefinition!.ShenwuActivity!;
        var offset = (player.Seat - _snapshot.HumanSeat + _snapshot.Players.Count) % _snapshot.Players.Count;
        var index = activity.PlayerSeatOffsets.ToList().IndexOf(offset);
        return index >= 0 ? new[] { "先锋", "中坚", "大将" }[index]
            : offset == activity.LeaderSeatOffset ? "首领" : "烛阴";
    }
}
