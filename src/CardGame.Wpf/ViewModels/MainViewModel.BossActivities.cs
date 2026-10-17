using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private ContentRegistry SelectedModeRegistry => SelectedTableMode is { } selected &&
        !_contentRegistry.Modes.ContainsKey(selected.ModeId) && _game?.ContentRegistry is { } restored &&
        restored.Modes.ContainsKey(selected.ModeId) ? restored : _contentRegistry;
    private ContentModeDefinition? SelectedModeDefinition => SelectedTableMode is { } selected
        ? SelectedModeRegistry.Modes.GetValueOrDefault(selected.ModeId) : null;
    private ContentModeDefinition? CurrentModeDefinition => _snapshot is not null
        ? _game.ContentRegistry?.Modes.GetValueOrDefault(_game.ModeId) : null;
    private static bool IsActivityDefinition(ContentModeDefinition mode) =>
        mode.TeamActivity is not null || mode.BossActivity is not null || mode.ShenwuActivity is not null || mode.NianActivity is not null || mode.WenheActivity is not null || mode.DragonBoatActivity is not null || mode.GuanduActivity is not null;
    private bool IsBossActivityModeSelection => SelectedModeDefinition?.BossActivity is not null;
    private bool IsBossActivitySnapshot => CurrentModeDefinition?.BossActivity is not null;

    private static IReadOnlyList<StartingTeamOption> StartingTeamsFor(ContentModeDefinition? mode) =>
        mode?.Id == "campaign:shanhe-battle"
            ? [new("义军", "team:shanhe-player", "从山河图地图出征；当前节点决定实际敌军与友军人数")]
        : mode?.GuanduActivity is not null ? GuanduStartingTeams : mode?.DragonBoatActivity is not null ? DragonBoatStartingTeams : mode?.ShenwuActivity is { } shenwu
            ? [new("联合军", shenwu.PlayerTeamId, "你与两名人机搭档挑战凶兽首领和两名烛阴")]
            : mode?.BossActivity is { } boss
            ? [new("联合军", boss.PlayerTeamId, mode.HellActivity is not null
                ? "你与两名人机搭档闯过四殿，迎战阎王与孟婆"
                : mode.NianActivity is not null
                ? "你与两名人机搭档挑战逐轮变换状态的年兽"
                : mode.HulaoActivity is not null
                ? "你与两名人机搭档合战双形态神吕布"
                : "你与两名人机搭档连续迎战四关鬼将")]
            : mode?.TeamActivity is not null ? ActivityStartingTeams : StandardStartingTeams;

    private string BossActivitySetupText => IsHellActivityModeSelection ? HellActivitySetupText : IsNianActivityModeSelection ? NianActivitySetupText : IsHulaoActivityModeSelection ? HulaoActivitySetupText :
        $"{SelectedTableMode.Name} · 联合军。你与两名人机搭档以三对一连续挑战四关：小鬼、牛头马面、黑白无常、罗刹夜叉。联合军状态跨关保留，击败最后一关鬼将获胜。玩家固定五阶：体力上限+2、起手牌+1、出杀次数+1、摸牌数+1，获得一次涅槃（回复3点体力、摸3张牌）。";

    private string BossActivityStageText
    {
        get
        {
            if (IsHellActivitySnapshot) return HellActivityStageText;
            if (IsNianActivitySnapshot) return NianActivityStageText;
            if (IsHulaoActivitySnapshot) return HulaoActivityStageText;
            if (CurrentModeDefinition?.BossActivity is not { } boss || _snapshot is null) return string.Empty;
            var index = _game.Events.Select(e => e.Payload).OfType<BossActivityStageStartedEvent>()
                .Select(e => e.StageIndex).DefaultIfEmpty(0).Last();
            if (index < 0 || index >= boss.Stages.Count) return $"共 {boss.Stages.Count} 关";
            return $"第 {index + 1}/{boss.Stages.Count} 关 · {boss.Stages[index].Name}";
        }
    }
}
