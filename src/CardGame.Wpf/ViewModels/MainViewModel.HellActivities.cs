using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool IsHellActivityModeSelection => SelectedModeDefinition?.HellActivity is not null;
    private bool IsHellActivitySnapshot => CurrentModeDefinition?.HellActivity is not null;
    private string HellActivitySetupText =>
        $"{SelectedTableMode.Name} · 联合军。你与两名人机搭档连续闯四殿：秦广王、第二殿随机阎王、第三殿随机阎王、转轮王。孟婆会在前三殿之一登场；前50回合内抵达最后一殿且曾击败孟婆时，最后首领改为地藏王。玩家固定五阶：体力及上限+2、起手牌+2、出杀次数+1、摸牌数+1，随机装备一件装备，并获得一次濒死重生。重生弃置判定区的牌、复原武将牌、将手牌补至体力上限（至多5张）并回复满体力。敌方为{SelectedModeDefinition!.HellActivity!.BossTier}阶，联合军状态跨关保留。";
    private static string HellActivityLobbyDescription(ContentModeDefinition mode) =>
        $"玩家固定五阶 · 两名人机队友\n敌方{mode.HellActivity!.BossTier}阶 · 四殿与孟婆、隐藏地藏王";
    private static string HellActivitySelectionDetailText => "玩家五阶 · 起手六张 · 两名人机搭档 · 四殿连续挑战";
    private static string HellActivityRosterText => "联合军三席 · 两名人机搭档 · 阎王与孟婆两席";
    private string HellActivityObjectiveText =>
        $"你的目标：与两名联合军人机搭档合作，连续击败四殿首领。孟婆不是过关必杀目标；前50回合内抵达最后一殿且曾击败孟婆时挑战地藏王。当前{HellActivityStageText}。";
    private string HellActivityStageText
    {
        get
        {
            if (CurrentModeDefinition is not { HellActivity: { } hell, BossActivity: { } boss } || _snapshot is null)
                return string.Empty;
            var index = _game.Events.Select(entry => entry.Payload).OfType<BossActivityStageStartedEvent>()
                .Select(fact => fact.StageIndex).DefaultIfEmpty(0).Last();
            var leader = _snapshot.Players.FirstOrDefault(player => player.TeamId == boss.EnemyTeamId &&
                player.GeneralId != hell.AuxiliaryGeneralId);
            var name = leader?.GeneralName ?? (index >= 0 && index < boss.Stages.Count ? boss.Stages[index].Name : "阎王");
            return $"第 {index + 1}/{boss.Stages.Count} 殿 · {name} · 敌方{hell.BossTier}阶 · {HellAuxiliaryStatus}";
        }
    }
    private string HellAuxiliaryStatus
    {
        get
        {
            var auxiliary = _snapshot?.Players.FirstOrDefault(player =>
                player.GeneralId == CurrentModeDefinition?.HellActivity?.AuxiliaryGeneralId);
            return auxiliary is null || IsHellDormantAuxiliary(auxiliary) ? "孟婆未登场"
                : auxiliary.IsAlive ? "孟婆在场" : "孟婆已阵亡";
        }
    }
    private string HellActivityAliveText => CurrentModeDefinition?.BossActivity is { } activity
        ? $"联合军 {Seats.Count(seat => seat.IsAlive && seat.TeamId == activity.PlayerTeamId)} · 阎罗 {Seats.Count(seat => seat.IsAlive && seat.TeamId == activity.EnemyTeamId)} 存活 · {HellActivityStageText}"
        : string.Empty;
    private static string[] HellActivityGuideSteps =>
    [
        "你与两名人机搭档组成联合军连续挑战四殿；玩家固定五阶，起手六张并随机装备一件装备，状态跨关保留。敌方一至五阶可在大厅选择。",
        "前三殿依次迎战秦广王、随机第二殿阎王、随机第三殿阎王。孟婆在前三殿之一登场，未登场席位不算阵亡。",
        "最后一殿通常是转轮王；前50回合内抵达且曾击败孟婆时改为地藏王。击败当前首领并结清死亡效果后过关，孟婆无须击杀。",
        "每名玩家可在濒死时重生一次：弃判定、复原武将牌、手牌补至体力上限且至多5张、回复满体力。牌堆不含木牛流马。"
    ];

    private bool IsHellDormantAuxiliary(PlayerSnapshot player) => CurrentModeDefinition?.HellActivity is { } hell &&
        player.GeneralId == hell.AuxiliaryGeneralId && !_game.Events.Select(entry => entry.Payload)
            .OfType<HellNpcAppearedEvent>().Any(fact => fact.Seat == player.Seat);

    private string HellSeatLabel(PlayerSnapshot player)
    {
        var activity = CurrentModeDefinition!.BossActivity!;
        if (player.TeamId == activity.EnemyTeamId)
            return player.GeneralId == CurrentModeDefinition.HellActivity!.AuxiliaryGeneralId
                ? IsHellDormantAuxiliary(player) ? "孟婆·未登场" : "孟婆" : "首领";
        var offset = (player.Seat - _snapshot.HumanSeat + _snapshot.Players.Count) % _snapshot.Players.Count;
        return offset is >= 0 and <= 2 ? new[] { "先锋", "中坚", "大将" }[offset] : "联合军";
    }

    private LegalAction? SelectedHellReturnSoulAction()
    {
        if (_selectedCardId is not { } cardId || _snapshot?.PendingDecision?.Kind != DecisionKind.PlayCard ||
            PlayMaterials(_snapshot.Players.Single(player => player.IsHuman)).All(card => card.Id != cardId))
            return null;
        var actions = GetViewLegalActions().Where(action => action.CardId == cardId &&
            action.Kind == LegalActionKind.ReturnSoul && action.TargetCardId is null &&
            action.PlayedCardKind is null or CardKind.ReturnSoul && MatchesSelectedPlayConversion(action)).ToArray();
        return actions.Length == 1 ? actions[0] : null;
    }
}
