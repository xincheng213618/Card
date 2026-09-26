using CardGame.Core;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public const double DefaultSoundVolume = .4;
    private bool _isSoundEnabled = true;
    private double _soundVolume = DefaultSoundVolume;
    private bool _isAudioUnavailable;
    private PromptId? _audioPromptId;
    private bool _audioCompleted;

    public event EventHandler<GameSoundsEventArgs>? SoundsRequested;
    public event EventHandler? SoundsReset;
    public bool IsSoundEnabled
    {
        get => _isSoundEnabled;
        set { if (SetProperty(ref _isSoundEnabled, value)) { RaisePropertyChanged(nameof(SoundStatusText)); RaisePropertyChanged(nameof(GeneralVoiceStatus)); QueueAutoSave(); QueuePreferencesSave(); } }
    }
    public double SoundVolume
    {
        get => _soundVolume;
        set
        {
            if (!double.IsFinite(value)) return;
            if (SetProperty(ref _soundVolume, Math.Clamp(value, 0, 1))) { RaisePropertyChanged(nameof(SoundStatusText)); RaisePropertyChanged(nameof(GeneralVoiceStatus)); QueueAutoSave(); QueuePreferencesSave(); }
        }
    }
    public bool IsAudioUnavailable
    {
        get => _isAudioUnavailable;
        set { if (SetProperty(ref _isAudioUnavailable, value)) { RaisePropertyChanged(nameof(SoundStatusText)); RaisePropertyChanged(nameof(GeneralVoiceStatus)); } }
    }
    public string SoundStatusText => HasPreferencesError ? PreferencesStatus : IsAudioUnavailable ? "声音暂不可用，可关闭后重新开启重试。"
        : !IsSoundEnabled || SoundVolume == 0 ? "已静音 · Ctrl+M 切换声音" : $"游戏音量 {SoundVolume:P0} · 切到后台自动停声 · Ctrl+M 静音";
    public string GameOutcomeTitle => !HasGameOver ? string.Empty : GameSoundRules.Outcome(_snapshot) switch
    {
        GameSound.Victory => "胜 利",
        GameSound.Defeat => "败 北",
        _ => "平 局"
    };

    private void ResetAudioFeedback()
    {
        _audioPromptId = _game.PendingDecision?.PromptId;
        _audioCompleted = _game.State.Status == EngineStatus.Completed;
        SoundsReset?.Invoke(this, EventArgs.Empty);
    }

    private void PublishGameSounds(IReadOnlyList<BattleCue> cues)
    {
        var sounds = GameSoundRules.FromPublicCues(cues).ToList();
        var prompt = _snapshot.PendingDecision;
        if (prompt is not null && prompt.PlayerSeat == _snapshot.HumanSeat && prompt.PromptId != _audioPromptId)
            sounds.Add(prompt.Kind == DecisionKind.PlayCard ? GameSound.YourTurn : GameSound.Prompt);
        _audioPromptId = prompt?.PromptId;
        if (_snapshot.Status == EngineStatus.Completed && !_audioCompleted)
            sounds.Add(GameSoundRules.Outcome(_snapshot));
        _audioCompleted = _snapshot.Status == EngineStatus.Completed;
        if (sounds.Count > 0) SoundsRequested?.Invoke(this, new GameSoundsEventArgs(GameSoundRules.SelectBatch(sounds)));
    }
}
