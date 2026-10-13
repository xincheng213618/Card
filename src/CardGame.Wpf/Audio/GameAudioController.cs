using System.ComponentModel;
using System.Windows;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Audio;

public interface IGameAudioOutput : IDisposable
{
    event EventHandler? Failed;
    void Play(GameSound sound, double volume);
    void Play(GameSound sound, double volume, TimeSpan maximumStartDelay) => Play(sound, volume);
    void PrepareEffects() { }
    void PlayVoice(string path, double volume);
    void StopVoice();
    void SetMusic(string? path, double volume);
    void SetVolume(double volume);
    void StopAll();
}

/// <summary>Foreground-only sound routing. It never queues old cues or controls the rule engine.</summary>
public sealed class GameAudioController : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Func<IGameAudioOutput> _createOutput;
    private readonly TimeProvider _clock;
    private readonly BattleFeedbackLayer? _feedbackLayer;
    private readonly Dictionary<GameSound, long> _lastPlayed = [];
    private IGameAudioOutput? _output;
    private bool _active;
    private bool _failed;
    private bool _disposed;

    public GameAudioController(MainViewModel viewModel, Func<IGameAudioOutput> createOutput, TimeProvider? clock = null,
        BattleFeedbackLayer? feedbackLayer = null)
    {
        _viewModel = viewModel;
        _createOutput = createOutput;
        _clock = clock ?? TimeProvider.System;
        _feedbackLayer = feedbackLayer;
        if (feedbackLayer is not null)
        {
            feedbackLayer.SetSoundsEnabled(false);
            feedbackLayer.DiscardPendingSounds();
            feedbackLayer.SoundsRequested += OnBattleSounds;
            feedbackLayer.Loaded += OnFeedbackLoaded;
            feedbackLayer.Unloaded += OnFeedbackUnloaded;
        }
        viewModel.SoundsRequested += OnSounds;
        viewModel.SoundsReset += OnReset;
        viewModel.VoiceRequested += OnVoice;
        viewModel.VoiceReset += OnVoiceReset;
        viewModel.PropertyChanged += OnPropertyChanged;
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        _active = active;
        if (!active) Stop();
        else
        {
            UpdateFeedbackSoundGate();
            PrepareEffects();
            UpdateMusic();
        }
    }

    private void OnSounds(object? sender, GameSoundsEventArgs args)
    {
        var sounds = _feedbackLayer is { IsPresentationActive: true } && _viewModel.IsMotionEnabled
            ? args.NotificationSounds : args.Sounds;
        PlaySounds(sounds);
    }

    private void OnBattleSounds(object? sender, GameSoundsEventArgs args)
    {
        if (!_viewModel.IsMotionEnabled || _feedbackLayer is not { IsPresentationActive: true }) return;
        PlaySounds(args.Sounds, _feedbackLayer.PlaybackTimeScale);
    }

    private void PlaySounds(IEnumerable<GameSound> sounds, double? timeScale = null)
    {
        if (!CanPlay) return;
        var batch = GameSoundRules.SelectBatch(sounds);
        if (batch.Count == 0) return;
        try
        {
            var output = Output;
            var now = _clock.GetTimestamp();
            foreach (var sound in batch)
            {
                if (_failed) break;
                var interval = GameSoundRules.Priority(sound) >= 80 ? .35 : .12 * (timeScale ?? 1);
                if (_lastPlayed.TryGetValue(sound, out var previous) && _clock.GetElapsedTime(previous, now).TotalSeconds < interval) continue;
                if (GameSoundRules.Priority(sound) == 100)
                {
                    _feedbackLayer?.DiscardPendingSounds();
                    output.StopAll();
                }
                if (timeScale is { } scale) output.Play(sound, _viewModel.SoundVolume, TimeSpan.FromSeconds(.12 * scale));
                else output.Play(sound, _viewModel.SoundVolume);
                if (!_failed) _lastPlayed[sound] = now;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private bool CanPlay => !_disposed && _active && !_failed && _viewModel.IsSoundEnabled && _viewModel.SoundVolume > 0;

    private void UpdateFeedbackSoundGate() => _feedbackLayer?.SetSoundsEnabled(
        CanPlay && _viewModel.IsMotionEnabled && !_viewModel.HasGameOver);

    private void PrepareEffects()
    {
        if (!CanPlay) return;
        try { Output.PrepareEffects(); }
        catch (Exception error) when (error is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private void OnFeedbackLoaded(object sender, RoutedEventArgs args) { UpdateFeedbackSoundGate(); UpdateMusic(); }
    private void OnFeedbackUnloaded(object sender, RoutedEventArgs args) => Stop();

    private IGameAudioOutput Output
    {
        get
        {
            if (_output is null) { _output = _createOutput(); _output.Failed += OnFailed; }
            return _output;
        }
    }

    private void OnVoice(object? sender, GeneralVoiceEventArgs args)
    {
        if (!CanPlay || !_viewModel.IsVoiceEnabled) return;
        try { Output.PlayVoice(args.Voice.Asset.FilePath, _viewModel.SoundVolume); }
        catch (Exception error) when (error is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private void OnVoiceReset(object? sender, EventArgs args)
    {
        try { _output?.StopVoice(); }
        catch (Exception error) when (error is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private void UpdateMusic()
    {
        if (_disposed) return;
        try
        {
            var music = CanPlay && _viewModel.IsMusicEnabled && (_viewModel.IsNewGameSetupOpen || !_viewModel.HasGameOver)
                ? GameAudioCatalog.Music(_viewModel.IsNewGameSetupOpen ? "lobby" : "battle") : null;
            if (music is not null) Output.SetMusic(music.FilePath, _viewModel.SoundVolume * .28);
            else _output?.SetMusic(null, 0);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private void OnReset(object? sender, EventArgs args) { Stop(); UpdateFeedbackSoundGate(); UpdateMusic(); }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.IsMusicEnabled) or nameof(MainViewModel.IsNewGameSetupOpen) or nameof(MainViewModel.HasGameOver))
        { UpdateFeedbackSoundGate(); UpdateMusic(); return; }
        if (args.PropertyName == nameof(MainViewModel.IsMotionEnabled))
        { UpdateFeedbackSoundGate(); return; }
        if (args.PropertyName == nameof(MainViewModel.IsVoiceEnabled))
        { if (!_viewModel.IsVoiceEnabled) OnVoiceReset(this, EventArgs.Empty); return; }
        if (args.PropertyName is not (nameof(MainViewModel.IsSoundEnabled) or nameof(MainViewModel.SoundVolume))) return;
        if (!_viewModel.IsSoundEnabled || _viewModel.SoundVolume <= 0) Stop();
        else
        {
            if (_failed)
            {
                ReleaseOutput();
                _failed = false;
                _viewModel.IsAudioUnavailable = false;
            }
            UpdateFeedbackSoundGate();
            PrepareEffects();
            try { _output?.SetVolume(_viewModel.SoundVolume); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
            UpdateMusic();
        }
    }

    private void OnFailed(object? sender, EventArgs args)
    {
        if (_failed) return;
        _failed = true;
        _viewModel.IsAudioUnavailable = true;
        Stop();
    }

    private void Stop()
    {
        _feedbackLayer?.SetSoundsEnabled(false);
        _lastPlayed.Clear();
        try { _output?.StopAll(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _failed = true; _viewModel.IsAudioUnavailable = true; }
    }

    private void ReleaseOutput()
    {
        if (_output is null) return;
        _output.Failed -= OnFailed;
        try { _output.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        _output = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.SoundsRequested -= OnSounds;
        _viewModel.SoundsReset -= OnReset;
        _viewModel.VoiceRequested -= OnVoice;
        _viewModel.VoiceReset -= OnVoiceReset;
        _viewModel.PropertyChanged -= OnPropertyChanged;
        if (_feedbackLayer is not null)
        {
            _feedbackLayer.SetSoundsEnabled(false);
            _feedbackLayer.SoundsRequested -= OnBattleSounds;
            _feedbackLayer.Loaded -= OnFeedbackLoaded;
            _feedbackLayer.Unloaded -= OnFeedbackUnloaded;
        }
        ReleaseOutput();
        _lastPlayed.Clear();
    }
}
