using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record BattleLogFilterOption(string Id, string Name, string? ToolTip = null, string? CompactName = null)
{
    public bool IsTableFilter => Id != "system";
    public string TableLabel => CompactName ?? Name;
}

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
    public IReadOnlyList<string> ParticipantNames { get; init; } = [];
    internal string OriginalMessage { get; init; } = Message;
    internal IReadOnlyList<int> MentionedSeats { get; init; } = [];
}

public sealed partial class MainViewModel
{
    private readonly List<BattleLogEntryViewModel> _allBattleLogEntries = [];
    private string _selectedBattleLogCategory = "all";
    private string _selectedBattleLogSeat = "all";
    private string _battleLogSeatSignature = string.Empty;
    private Regex? _battleLogPlayerNames;
    private readonly Dictionary<string, SeatViewModel> _battleLogPlayersByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _battleLogGeneralNames = [];

    public ObservableRangeCollection<BattleLogEntryViewModel> FilteredBattleLog { get; } = [];
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
        BattleLogSeats.Add(new("all", "全部", "全部座次", "全"));
    }

    private void ResetBattleLog()
    {
        _allBattleLogEntries.Clear();
        FilteredBattleLog.Clear();
        _battleLogSeatSignature = string.Empty;
        _battleLogPlayerNames = null;
        _battleLogPlayersByName.Clear();
        _battleLogGeneralNames.Clear();
        _selectedBattleLogCategory = "all";
        _selectedBattleLogSeat = "all";
        RaisePropertyChanged(nameof(SelectedBattleLogCategory));
        RaisePropertyChanged(nameof(SelectedBattleLogSeat));
        RaisePropertyChanged(nameof(BattleLogFilterSummary));
    }

    private void AddBattleLogEntry(GameLogEntry entry)
    {
        var displayed = ProjectBattleLogEntry(new BattleLogEntryViewModel(
            entry.Sequence, entry.TurnNumber, entry.Type, BattleLogCategory(entry.Type),
            BattleLogCategoryName(BattleLogCategory(entry.Type)), entry.Message, entry.ActorSeat, entry.TargetSeat));
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
        var players = _snapshot.Players.ToDictionary(player => player.Seat);
        var generalNames = players.ToDictionary(pair => pair.Key, pair =>
            !pair.Value.IsHuman && pair.Value.IdentityConcealed ? "隐匿武将" :
            !pair.Value.IsHuman && !pair.Value.IsGeneralPublic ? "暗将" :
            string.IsNullOrWhiteSpace(pair.Value.GeneralName) ? "待选武将" : pair.Value.GeneralName);
        var signature = string.Join('|', Seats.Select(seat => $"{seat.Seat}:{seat.DisplaySeatNumber}:{players[seat.Seat].Name}:{generalNames[seat.Seat]}:{seat.RoleLabel == "主公"}"));
        if (signature == _battleLogSeatSignature) return;
        _battleLogSeatSignature = signature;
        BattleLogSeats.Clear();
        BattleLogSeats.Add(new("all", "全部", "全部座次", "全"));
        foreach (var seat in Seats.OrderBy(seat => seat.DisplaySeatNumber))
            BattleLogSeats.Add(new($"seat:{seat.Seat}", $"{seat.DisplaySeatNumber}号位", $"{seat.DisplaySeatNumber}号位",
                seat.RoleLabel == "主公" ? "主" : seat.DisplaySeatNumber is >= 1 and <= 10
                    ? "一二三四五六七八九十"[seat.DisplaySeatNumber - 1].ToString() : $"{seat.DisplaySeatNumber}"));
        BattleLogSeats.Add(new("system", "系统事件"));

        _battleLogPlayersByName.Clear();
        _battleLogGeneralNames.Clear();
        foreach (var seat in Seats)
        {
            _battleLogGeneralNames[seat.Seat] = generalNames[seat.Seat];
            if (!string.IsNullOrEmpty(players[seat.Seat].Name))
                _battleLogPlayersByName[players[seat.Seat].Name] = seat;
        }
        // Treat card and skill titles as literals, even when they contain a player's name.
        _battleLogPlayerNames = _battleLogPlayersByName.Count == 0 ? null : new Regex(
            "【[^】]*】|(?<player>" + string.Join('|', _battleLogPlayersByName.Keys
                .OrderByDescending(name => name.Length).Select(Regex.Escape)) + ")",
            RegexOptions.CultureInvariant);
        for (var index = 0; index < _allBattleLogEntries.Count; index++)
            _allBattleLogEntries[index] = ProjectBattleLogEntry(_allBattleLogEntries[index]);
        if (_allBattleLogEntries.Count > 0)
        {
            GameLog.Clear();
            foreach (var entry in _allBattleLogEntries)
                GameLog.Add($"#{entry.Sequence:000} · T{entry.TurnNumber:000}  {entry.Message}");
        }
        if (!BattleLogSeats.Any(option => option.Id == SelectedBattleLogSeat)) SelectedBattleLogSeat = "all";
        RefreshBattleLog();
    }

    private BattleLogEntryViewModel ProjectBattleLogEntry(BattleLogEntryViewModel entry)
    {
        var mentioned = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var message = _battleLogPlayerNames?.Replace(entry.OriginalMessage, match =>
        {
            if (!match.Groups["player"].Success) return match.Value;
            var seat = _battleLogPlayersByName[match.Value];
            mentioned.Add(seat.Seat);
            var name = _battleLogGeneralNames[seat.Seat];
            if (_battleLogGeneralNames.Values.Count(other => other == name) > 1) name += $"（{seat.DisplaySeatNumber}）";
            if (seat.IsHuman) name += "（你）";
            names.Add(name);
            return name;
        }) ?? entry.OriginalMessage;
        return entry with { Message = message, MentionedSeats = Array.AsReadOnly(mentioned.ToArray()), ParticipantNames = Array.AsReadOnly(names.ToArray()) };
    }

    private void RefreshBattleLog()
    {
        FilteredBattleLog.ReplaceAll(_allBattleLogEntries.Where(MatchesBattleLogFilter));
        RaisePropertyChanged(nameof(BattleLogFilterSummary));
    }

    private bool MatchesBattleLogFilter(BattleLogEntryViewModel entry) =>
        (SelectedBattleLogCategory == "all" || entry.CategoryId == SelectedBattleLogCategory) &&
        (SelectedBattleLogSeat == "all" ||
         SelectedBattleLogSeat == "system" && entry.ActorSeat is null && entry.TargetSeat is null && entry.MentionedSeats.Count == 0 ||
         SelectedBattleLogSeat.StartsWith("seat:", StringComparison.Ordinal) &&
         int.TryParse(SelectedBattleLogSeat.AsSpan(5), out var seat) &&
         (entry.ActorSeat == seat || entry.TargetSeat == seat || entry.MentionedSeats.Contains(seat)));

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
