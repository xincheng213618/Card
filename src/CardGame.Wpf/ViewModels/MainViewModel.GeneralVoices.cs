using System.Windows.Input;
using CardGame.Wpf.Audio;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isMusicEnabled = true;
    private bool _isVoiceEnabled = true;
    public bool IsMusicEnabled
    {
        get => _isMusicEnabled;
        set { if (SetProperty(ref _isMusicEnabled, value)) QueuePreferencesSave(); }
    }
    public bool IsVoiceEnabled
    {
        get => _isVoiceEnabled;
        set { if (SetProperty(ref _isVoiceEnabled, value)) { QueuePreferencesSave(); RaisePropertyChanged(nameof(GeneralVoiceStatus)); } }
    }
    public event EventHandler<GeneralVoiceEventArgs>? VoiceRequested;
    public event EventHandler? VoiceReset;
    public bool IsGeneralVoicesTab => GeneralDetailsTab == "voices";
    public IReadOnlyList<GeneralVoice> GeneralVoices => SelectedGeneralGalleryEntry is { } entry
        ? GameAudioCatalog.ForGeneral(entry.GeneralId).OrderByDescending(voice => voice.Binding.SkinId == entry.Portrait.SkinId)
            .ThenBy(voice => voice.SkinName).ThenBy(voice => voice.SkillName).ToArray() : [];
    public bool HasNoGeneralVoices => GeneralVoices.Count == 0;
    public string GeneralVoiceStatus => IsAudioUnavailable ? "声音暂不可用 · 可关闭声音后重新开启重试"
        : !IsSoundEnabled || SoundVolume <= 0 ? "已静音 · 开启声音后可试听"
        : !IsVoiceEnabled ? "武将配音已关闭 · 可在设置中开启" : "点击试听 · 对局按当前皮肤播放已收录配音";
    public ICommand PlayGeneralVoiceCommand { get; private set; } = null!;
    public ICommand PlayGeneralSkillVoiceCommand { get; private set; } = null!;
    public ICommand StopGeneralVoiceCommand { get; private set; } = null!;

    private void InitializeGeneralVoices()
    {
        PlayGeneralVoiceCommand = new RelayCommand<GeneralVoice>(voice =>
        {
            if (GeneralVoices.Contains(voice)) VoiceRequested?.Invoke(this, new(voice));
        });
        PlayGeneralSkillVoiceCommand = new RelayCommand<string>(name =>
        {
            if (GeneralVoices.FirstOrDefault(voice => voice.Binding.SkillName == name) is { } voice)
                VoiceRequested?.Invoke(this, new(voice));
        });
        StopGeneralVoiceCommand = new RelayCommand(() => VoiceReset?.Invoke(this, EventArgs.Empty));
    }

    private void RefreshGeneralVoices()
    {
        VoiceReset?.Invoke(this, EventArgs.Empty);
        RaisePropertyChanged(nameof(GeneralVoices));
        RaisePropertyChanged(nameof(HasNoGeneralVoices));
        RaisePropertyChanged(nameof(GeneralVoiceStatus));
    }
}
