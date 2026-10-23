using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private ShanheRankOption? _selectedShanheRank;
    public IReadOnlyList<ShanheRankOption> ShanheRanks { get; } =
    [
        new(1, "一阶 · 基础"), new(2, "二阶 · 备战"), new(3, "三阶 · 骁勇"),
        new(4, "四阶 · 精锐"), new(5, "五阶 · 重生")
    ];

    public ShanheRankOption SelectedShanheRank
    {
        get => _selectedShanheRank ?? ShanheRanks[0];
        set
        {
            if (value is null || !ShanheRanks.Contains(value) ||
                IsShanheRunInProgress && value.Value != _shanheSession!.Snapshot.PlayerRank ||
                !SetProperty(ref _selectedShanheRank, value)) return;
            RaisePropertyChanged(nameof(SelectedShanheRankDescription));
            RaisePropertyChanged(nameof(ShanheRankStatusText));
        }
    }

    public string SelectedShanheRankDescription => SelectedShanheRank.Value switch
    {
        1 => "一阶：使用武将原有体力、起手与摸牌数量。",
        2 => "二阶：初始手牌 +1。",
        3 => "三阶：体力及上限 +1，初始手牌 +1，出杀次数 +1。",
        4 => "四阶：体力及上限 +1，初始手牌 +2，摸牌数 +1，出杀次数 +1。",
        _ => "五阶：体力及上限 +2，初始手牌 +2，摸牌数 +1，出杀次数 +1；整次征程可重生一次，回复至5体力后获得5张牌。"
    };

    public string ShanheRankStatusText
    {
        get
        {
            var rank = _shanheSession?.Snapshot.PlayerRank ?? SelectedShanheRank.Value;
            var label = ShanheRanks.Single(option => option.Value == rank).Name;
            if (rank != 5) return label;
            var used = _shanheSession?.Snapshot.RebirthUsed == true ||
                _shanheBattleGame?.Events.Any(e => e.Payload is SkillUsageConsumedEvent
                { SkillOwnerSeat: 0, SkillId: ShanheGeneralRanks.RebirthSkillId, Scope: SkillUsageScope.Game, Count: > 0 }) == true;
            return label + (used ? " · 重生已用" : " · 重生可用（本征程一次）");
        }
    }
}

public sealed record ShanheRankOption(int Value, string Name);
