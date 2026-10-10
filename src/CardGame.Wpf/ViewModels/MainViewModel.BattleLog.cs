using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record BattleLogFilterOption(string Id, string Name);

public sealed record BattleLogEntryViewModel(
    int Sequence,
    int TurnNumber,
    string Type,
    string CategoryId,
    string CategoryName,
    string Message,
    int? ActorSeat,
    int? TargetSeat)
{
    public string Header => $"#{Sequence:000} · T{TurnNumber:000} · {CategoryName}";
}

public sealed partial class MainViewModel
{
    private readonly List<BattleLogEntryViewModel> _allBattleLogEntries = [];
    private string _selectedBattleLogCategory = "all";
    private string _selectedBattleLogSeat = "all";
    private string _battleLogSeatSignature = string.Empty;

    public ObservableCollection<BattleLogEntryViewModel> FilteredBattleLog { get; } = [];
    public ObservableCollection<BattleLogFilterOption> BattleLogSeats { get; } = [];
    public IReadOnlyList<BattleLogFilterOption> BattleLogCategories { get; } =
    [
        new("all", "全部"),
        new("action", "用牌 / 响应"),
        new("damage", "伤害 / 回复"),
        new("skill", "技能 / 装备"),
        new("turn", "回合 / 摸弃"),
        new("system", "规则 / 终局")
    ];

    public string SelectedBattleLogCategory
    {
        get => _selectedBattleLogCategory;
        private set { if (SetProperty(ref _selectedBattleLogCategory, value)) RefreshBattleLog(); }
    }

    public string SelectedBattleLogSeat
    {
        get => _selectedBattleLogSeat;
        private set { if (SetProperty(ref _selectedBattleLogSeat, value)) RefreshBattleLog(); }
    }

    public string BattleLogFilterSummary =>
        $"显示 {FilteredBattleLog.Count} / {_allBattleLogEntries.Count} 条公开记录";

    public ICommand SelectBattleLogCategoryCommand { get; private set; } = null!;
    public ICommand SelectBattleLogSeatCommand { get; private set; } = null!;

    private void InitializeBattleLogFilters()
    {
        SelectBattleLogCategoryCommand = new RelayCommand<string>(id =>
        {
            if (BattleLogCategories.Any(option => option.Id == id)) SelectedBattleLogCategory = id!;
        });
        SelectBattleLogSeatCommand = new RelayCommand<string>(id =>
        {
            if (BattleLogSeats.Any(option => option.Id == id)) SelectedBattleLogSeat = id!;
        });
        BattleLogSeats.Add(new("all", "全部角色"));
    }

    private void ResetBattleLog()
    {
        _allBattleLogEntries.Clear();
        FilteredBattleLog.Clear();
        RaisePropertyChanged(nameof(BattleLogFilterSummary));
    }

    private void AddBattleLogEntry(GameLogEntry entry)
    {
        var displayed = new BattleLogEntryViewModel(
            entry.Sequence, entry.TurnNumber, entry.Type, BattleLogCategory(entry.Type),
            BattleLogCategoryName(BattleLogCategory(entry.Type)), entry.Message, entry.ActorSeat, entry.TargetSeat);
        _allBattleLogEntries.Insert(0, displayed);
        if (MatchesBattleLogFilter(displayed)) FilteredBattleLog.Insert(0, displayed);
        while (FilteredBattleLog.Count > 0 && _allBattleLogEntries.Count > 400 &&
               FilteredBattleLog[^1].Sequence <= _allBattleLogEntries[^1].Sequence)
            FilteredBattleLog.RemoveAt(FilteredBattleLog.Count - 1);
        while (_allBattleLogEntries.Count > 400) _allBattleLogEntries.RemoveAt(_allBattleLogEntries.Count - 1);
        RaisePropertyChanged(nameof(BattleLogFilterSummary));
    }

    private void RefreshBattleLogSeatOptions()
    {
        var signature = string.Join('|', Seats.Select(seat => $"{seat.Seat}:{seat.DisplaySeatNumber}:{seat.GeneralName}"));
        if (signature == _battleLogSeatSignature) return;
        _battleLogSeatSignature = signature;
        BattleLogSeats.Clear();
        BattleLogSeats.Add(new("all", "全部角色"));
        foreach (var seat in Seats)
            BattleLogSeats.Add(new($"seat:{seat.Seat}", seat.IsHuman ? $"我 · {seat.GeneralName}" : $"{seat.DisplaySeatNumber}号 · {seat.GeneralName}"));
        BattleLogSeats.Add(new("system", "系统事件"));
        if (!BattleLogSeats.Any(option => option.Id == SelectedBattleLogSeat)) SelectedBattleLogSeat = "all";
    }

    private void RefreshBattleLog()
    {
        FilteredBattleLog.Clear();
        foreach (var entry in _allBattleLogEntries.Where(MatchesBattleLogFilter)) FilteredBattleLog.Add(entry);
        RaisePropertyChanged(nameof(BattleLogFilterSummary));
    }

    private bool MatchesBattleLogFilter(BattleLogEntryViewModel entry) =>
        (SelectedBattleLogCategory == "all" || entry.CategoryId == SelectedBattleLogCategory) &&
        (SelectedBattleLogSeat == "all" ||
         SelectedBattleLogSeat == "system" && entry.ActorSeat is null && entry.TargetSeat is null ||
         SelectedBattleLogSeat.StartsWith("seat:", StringComparison.Ordinal) &&
         int.TryParse(SelectedBattleLogSeat.AsSpan(5), out var seat) && (entry.ActorSeat == seat || entry.TargetSeat == seat));

    private static string BattleLogCategory(string type) => type switch
    {
        "CardUsed" or "CardEffect" or "Response" or "NullificationRequested" or "NullificationResponded" => "action",
        "Damage" or "Recovery" or "Dying" or "Death" or "KillReward" or "LordPenalty" => "damage",
        "SkillTriggered" or "SkillSkipped" or "SkillDraw" or "EquipmentEffect" or "ArmorEffect" or "Judgment" => "skill",
        "PhaseChanged" or "TurnStarted" or "TurnEnded" or "CardsDrawn" or "CardsDiscarded" or "DelayedCardEffect" or "EffectExpired" => "turn",
        _ => "system"
    };

    private static string BattleLogCategoryName(string category) => category switch
    {
        "action" => "用牌 / 响应",
        "damage" => "伤害 / 回复",
        "skill" => "技能 / 装备",
        "turn" => "回合 / 摸弃",
        _ => "规则 / 终局"
    };
}
