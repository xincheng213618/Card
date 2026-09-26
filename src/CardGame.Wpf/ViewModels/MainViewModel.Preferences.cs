using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IPlayerPreferencesStore _preferencesStore = null!;
    private DispatcherTimer? _preferencesTimer;
    private bool _preferencesEstablished;
    private bool _applyingPreferences;
    private bool _preferencesPending;
    private bool _hasPreferencesError;
    private string _preferencesStatus = "声音与动画保存在本机，重新开局或读档后保持当前设置。";

    public bool HasPreferencesError { get => _hasPreferencesError; private set { if (SetProperty(ref _hasPreferencesError, value)) RaisePropertyChanged(nameof(SoundStatusText)); } }
    public string PreferencesStatus { get => _preferencesStatus; private set { if (SetProperty(ref _preferencesStatus, value)) RaisePropertyChanged(nameof(SoundStatusText)); } }
    public ICommand RetryPreferencesCommand { get; private set; } = null!;

    private void InitializePreferences(IPlayerPreferencesStore? store)
    {
        _preferencesStore = store ?? new MemoryPlayerPreferencesStore();
        _preferencesTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _preferencesTimer.Tick += OnPreferencesTick;
        RetryPreferencesCommand = new RelayCommand(() => { if (_preferencesPending) FlushPreferences(); else ReadPreferences(); });
        ReadPreferences();
    }

    private void ReadPreferences()
    {
        try
        {
            if (_preferencesStore.Read() is { } preferences)
            {
                ApplyPreferences(preferences);
                _preferencesEstablished = true;
            }
            HasPreferencesError = false;
            PreferencesStatus = "声音与动画保存在本机，重新开局或读档后保持当前设置。";
        }
        catch (Exception error) when (FileMatchHistoryStore.IsHistoryError(error))
        {
            // Preserve the unreadable file until the player explicitly changes a setting.
            _preferencesEstablished = true;
            ApplyPreferences(new(1, false, DefaultSoundVolume, false));
            HasPreferencesError = true;
            PreferencesStatus = "设置无法读取，暂以静音和无动画启动；调整设置后可重新保存，原文件会备份。";
        }
    }

    private void ApplyPreferences(PlayerPreferences preferences)
    {
        _applyingPreferences = true;
        try
        {
            IsSoundEnabled = preferences.SoundEnabled;
            SoundVolume = preferences.SoundVolume;
            IsMotionEnabled = preferences.MotionEnabled;
            IsMusicEnabled = preferences.MusicEnabled;
            IsVoiceEnabled = preferences.VoiceEnabled;
            ApplyGeneralSkinPreferences(preferences.GeneralSkins);
        }
        finally { _applyingPreferences = false; }
    }

    private void ImportLegacyPreferences(GameSaveFile save)
    {
        if (_preferencesEstablished) return;
        ApplyPreferences(new(1, save.SoundEnabled ?? true, save.SoundVolume ?? DefaultSoundVolume,
            save.MotionEnabled ?? System.Windows.SystemParameters.ClientAreaAnimation));
        QueuePreferencesSave();
    }

    private void QueuePreferencesSave()
    {
        if (_initializing || _disposed || _applyingPreferences) return;
        _preferencesEstablished = true;
        _preferencesPending = true;
        if (_preferencesTimer is { IsEnabled: false }) _preferencesTimer.Start();
    }

    private void OnPreferencesTick(object? sender, EventArgs e)
    {
        _preferencesTimer?.Stop();
        FlushPreferences();
    }

    public bool FlushPreferences()
    {
        if (!_preferencesPending) return !HasPreferencesError;
        _preferencesTimer?.Stop();
        try
        {
            _preferencesStore.Write(new(1, IsSoundEnabled, SoundVolume, IsMotionEnabled)
            {
                MusicEnabled = IsMusicEnabled,
                VoiceEnabled = IsVoiceEnabled,
                GeneralSkins = _generalSkinPreferences.Count == 0 ? null : new(_generalSkinPreferences, StringComparer.Ordinal)
            });
            _preferencesPending = false;
            HasPreferencesError = false;
            PreferencesStatus = "声音与动画已保存在本机，重新开局或读档后保持当前设置。";
            return true;
        }
        catch (Exception error) when (FileMatchHistoryStore.IsHistoryError(error))
        {
            HasPreferencesError = true;
            PreferencesStatus = "设置已生效，但尚未保存；点击重试，或退出时再次尝试。";
            return false;
        }
    }
}
