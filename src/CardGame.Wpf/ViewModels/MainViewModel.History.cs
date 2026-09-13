using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IMatchHistoryStore _historyStore = null!;
    private readonly Dictionary<string, MatchHistoryEntry> _pendingHistory = [];
    private GameEngine? _historyCapturedGame;
    private bool _isHistoryOpen;
    private string _historyStatus = "已完成的正式对局会自动记入战绩。";
    private MatchHistoryEntry? _selectedHistoryEntry;

    public ObservableCollection<MatchHistoryEntry> MatchHistory { get; } = [];
    public bool HasMatchHistory => MatchHistory.Count > 0;
    public bool HasNoMatchHistory => !HasMatchHistory;
    public bool IsHistoryOpen { get => _isHistoryOpen; set => SetProperty(ref _isHistoryOpen, value); }
    public string HistoryStatus { get => _historyStatus; private set => SetProperty(ref _historyStatus, value); }
    public MatchHistoryEntry? SelectedHistoryEntry { get => _selectedHistoryEntry; set => SetProperty(ref _selectedHistoryEntry, value); }
    public string HistoryTotals => $"最近 {MatchHistory.Count} 局 · 胜 {MatchHistory.Count(entry => entry.Outcome == MatchOutcome.Win)} · " +
        $"负 {MatchHistory.Count(entry => entry.Outcome == MatchOutcome.Loss)} · 平 {MatchHistory.Count(entry => entry.Outcome == MatchOutcome.Draw)}";
    public ICommand OpenHistoryCommand { get; private set; } = null!;
    public ICommand CloseHistoryCommand { get; private set; } = null!;
    public ICommand RefreshHistoryCommand { get; private set; } = null!;

    private void InitializeHistory(IMatchHistoryStore? store)
    {
        _historyStore = store ?? new MemoryMatchHistoryStore();
        OpenHistoryCommand = new RelayCommand(() => { FlushMatchHistory(); IsHistoryOpen = true; });
        CloseHistoryCommand = new RelayCommand(() => IsHistoryOpen = false);
        RefreshHistoryCommand = new RelayCommand(FlushMatchHistory);
        FlushMatchHistory();
    }

    private void CaptureCompletedHistory()
    {
        if (_initializing || IsTutorialActive || CompletedMatch is null || ReferenceEquals(_historyCapturedGame, _game)) return;
        // Submit has returned, so the final accepted command is included in this fingerprint.
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GameCheckpointJson.Serialize(_game.CreateCheckpoint())))).ToLowerInvariant();
        var sound = GameSoundRules.Outcome(_snapshot);
        _pendingHistory.TryAdd(id, new MatchHistoryEntry(1, id, DateTimeOffset.UtcNow, TableModeText, _game.RulesVersion,
            sound switch { GameSound.Victory => MatchOutcome.Win, GameSound.Defeat => MatchOutcome.Loss, _ => MatchOutcome.Draw }, CompletedMatch));
        _historyCapturedGame = _game;
        FlushMatchHistory();
        SelectedHistoryEntry = MatchHistory.FirstOrDefault(entry => entry.Id == id) ?? SelectedHistoryEntry;
    }

    private void FlushMatchHistory()
    {
        if (_historyStore is null) return;
        foreach (var entry in _pendingHistory.Values.ToArray())
        {
            try { _historyStore.Record(entry); _pendingHistory.Remove(entry.Id); }
            catch (Exception error) when (FileMatchHistoryStore.IsHistoryError(error)) { }
        }
        try
        {
            var loaded = _historyStore.Read();
            var selectedId = SelectedHistoryEntry?.Id;
            MatchHistory.Clear();
            foreach (var entry in loaded.Entries) MatchHistory.Add(entry);
            SelectedHistoryEntry = MatchHistory.FirstOrDefault(entry => entry.Id == selectedId) ?? MatchHistory.FirstOrDefault();
            HistoryStatus = _pendingHistory.Count > 0 ? $"{_pendingHistory.Count} 局战绩尚未写入，点击刷新重试；当前牌局仍可继续。"
                : loaded.UnreadableCount > 0 ? $"{loaded.UnreadableCount} 条战绩无法读取，其他记录仍可查看；原文件已保留。"
                : "显示最近 50 局；历史文件保留在本机。同一终局重复加载不会重复计入。";
        }
        catch (Exception error) when (FileMatchHistoryStore.IsHistoryError(error))
        {
            HistoryStatus = "战绩暂时无法读取，点击刷新重试；当前牌局仍可继续。";
        }
        RaisePropertyChanged(nameof(HasMatchHistory));
        RaisePropertyChanged(nameof(HasNoMatchHistory));
        RaisePropertyChanged(nameof(HistoryTotals));
    }
}
