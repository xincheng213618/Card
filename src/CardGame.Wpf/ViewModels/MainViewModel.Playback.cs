using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _deferAutomaticTableRefresh;
    private GameSnapshot? _automaticTableSnapshot;

    private void PresentCommittedState(GameSnapshot snapshot)
    {
        // Only coalesce invisible automatic continuations within this dispatcher
        // slice. Human prompts, completion and opening hands must be presented now.
        if (_deferAutomaticTableRefresh && snapshot.Status == EngineStatus.Running && snapshot.PendingDecision is null)
        {
            _automaticTableSnapshot = snapshot;
            return;
        }
        Refresh(snapshot);
    }

    private void FlushAutomaticTableRefresh()
    {
        if (_automaticTableSnapshot is { } snapshot) Refresh(snapshot);
    }

    private PlaybackSpeed _selectedPlaybackSpeed = PlaybackSpeed.Normal;
    public IReadOnlyList<PlaybackSpeed> PlaybackSpeeds => PlaybackSpeed.All;
    public PlaybackSpeed SelectedPlaybackSpeed
    {
        get => _selectedPlaybackSpeed;
        set
        {
            if (value is null || PlaybackSpeed.Find(value.Id) is not { } speed || !SetProperty(ref _selectedPlaybackSpeed, speed)) return;
            if (_advanceTimer is not null) _advanceTimer.Interval = TimeSpan.FromMilliseconds(speed.IntervalMilliseconds);
            RefreshPlaybackPresentation();
            if (_snapshot is not null) RefreshSelectionHint();
            QueueAutoSave();
        }
    }
    public bool IsSpectating => !IsTutorialActive && _snapshot?.Status != EngineStatus.Completed &&
        _snapshot?.Players.SingleOrDefault(player => player.IsHuman) is { IsAlive: false };
    public bool CanFastSpectate => IsSpectating && (!IsAutoAdvance || SelectedPlaybackSpeed != PlaybackSpeed.Fast);
    public string SpectatorHint => IsAutoAdvance
        ? $"你已阵亡 · {SelectedPlaybackSpeed.Name}观战中；取消「自动推进」可暂停。阵营胜负仍以最终结算为准。"
        : "你已阵亡 · 观战已暂停；点击「快速观战」继续，也可从新对局重新开局。";
    public ICommand FastSpectateCommand { get; private set; } = null!;

    private void InitializePlayback()
    {
        FastSpectateCommand = new RelayCommand(() =>
        {
            if (!CanFastSpectate) return;
            SelectedPlaybackSpeed = PlaybackSpeed.Fast;
            IsAutoAdvance = true;
        }, () => CanFastSpectate);
    }

    private void RefreshPlaybackPresentation()
    {
        RaisePropertyChanged(nameof(IsSpectating));
        RaisePropertyChanged(nameof(CanFastSpectate));
        RaisePropertyChanged(nameof(SpectatorHint));
        (FastSpectateCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}
