using System.ComponentModel;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Audio;

public interface IGameAudioOutput : IDisposable
{
    event EventHandler? Failed;
    void Play(GameSound sound, double volume);
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
    private readonly Dictionary<GameSound, long> _lastPlayed = [];
    private IGameAudioOutput? _output;
    private bool _active;
    private bool _failed;
    private bool _disposed;

    public GameAudioController(MainViewModel viewModel, Func<IGameAudioOutput> createOutput, TimeProvider? clock = null)
    {
        _viewModel = viewModel;
        _createOutput = createOutput;
        _clock = clock ?? TimeProvider.System;
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
        else UpdateMusic();
    }

    private void OnSounds(object? sender, GameSoundsEventArgs args)
    {
        if (_disposed || !_active || _failed || !_viewModel.IsSoundEnabled || _viewModel.SoundVolume <= 0) return;
        try
        {
            var output = Output;
            var now = _clock.GetTimestamp();
            foreach (var sound in GameSoundRules.SelectBatch(args.Sounds))
            {
                if (_failed) break;
                var interval = GameSoundRules.Priority(sound) >= 80 ? .35 : .12;
                if (_lastPlayed.TryGetValue(sound, out var previous) && _clock.GetElapsedTime(previous, now).TotalSeconds < interval) continue;
                if (GameSoundRules.Priority(sound) == 100) output.StopAll();
                output.Play(sound, _viewModel.SoundVolume);
                _lastPlayed[sound] = now;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private bool CanPlay => !_disposed && _active && !_failed && _viewModel.IsSoundEnabled && _viewModel.SoundVolume > 0;

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

    private void OnReset(object? sender, EventArgs args) { Stop(); UpdateMusic(); }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.IsMusicEnabled) or nameof(MainViewModel.IsNewGameSetupOpen) or nameof(MainViewModel.HasGameOver))
        { UpdateMusic(); return; }
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
        ReleaseOutput();
        _lastPlayed.Clear();
    }
}
