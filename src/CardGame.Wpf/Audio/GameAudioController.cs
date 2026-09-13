using System.ComponentModel;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Audio;

public interface IGameAudioOutput : IDisposable
{
    event EventHandler? Failed;
    void Play(GameSound sound, double volume);
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
        viewModel.PropertyChanged += OnPropertyChanged;
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        _active = active;
        if (!active) Stop();
    }

    private void OnSounds(object? sender, GameSoundsEventArgs args)
    {
        if (_disposed || !_active || _failed || !_viewModel.IsSoundEnabled || _viewModel.SoundVolume <= 0) return;
        try
        {
            if (_output is null)
            {
                _output = _createOutput();
                _output.Failed += OnFailed;
            }
            var now = _clock.GetTimestamp();
            foreach (var sound in GameSoundRules.SelectBatch(args.Sounds))
            {
                if (_failed) break;
                var interval = GameSoundRules.Priority(sound) >= 80 ? .35 : .12;
                if (_lastPlayed.TryGetValue(sound, out var previous) && _clock.GetElapsedTime(previous, now).TotalSeconds < interval) continue;
                if (GameSoundRules.Priority(sound) == 100) _output.StopAll();
                _output.Play(sound, _viewModel.SoundVolume);
                _lastPlayed[sound] = now;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { OnFailed(this, EventArgs.Empty); }
    }

    private void OnReset(object? sender, EventArgs args) => Stop();

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
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
        _viewModel.PropertyChanged -= OnPropertyChanged;
        ReleaseOutput();
        _lastPlayed.Clear();
    }
}
