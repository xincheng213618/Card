using System.IO;
using System.Windows.Media;

namespace CardGame.Wpf.Audio;

/// <summary>Short local WAV voices with their own volume; no system mixer changes.</summary>
public sealed class MediaPlayerAudioOutput : IGameAudioOutput
{
    private readonly Dictionary<GameSound, Voice> _voices = [];
    private readonly TimeProvider _clock;
    private readonly string _directory;
    private bool _disposed;
    private MediaPlayer? _speech;
    private MediaPlayer? _music;
    private string? _musicPath;
    public int SpeechStartedCount { get; private set; }
    public int SpeechCompletedCount { get; private set; }
    public int MusicStartedCount { get; private set; }
    public bool IsMusicReady { get; private set; }
    public event EventHandler? Failed;
    public int LoadedClipCount => _voices.Values.Count(voice => voice.Ready);
    public int ActiveVoiceCount => _voices.Values.Count(voice => voice.Active);
    public int StartedClipCount { get; private set; }
    public int CompletedClipCount { get; private set; }

    public MediaPlayerAudioOutput(string? directory = null, TimeProvider? clock = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Audio");
        _clock = clock ?? TimeProvider.System;
    }

    public void Play(GameSound sound, double volume)
    {
        if (_disposed) return;
        if (!_voices.TryGetValue(sound, out var voice))
        {
            var path = Path.Combine(_directory, $"{sound.ToString().ToLowerInvariant()}.wav");
            if (!File.Exists(path)) { Failed?.Invoke(this, EventArgs.Empty); return; }
            voice = new Voice(new MediaPlayer());
            _voices.Add(sound, voice);
            voice.Player.MediaOpened += (_, _) =>
            {
                if (_disposed) return;
                try
                {
                    voice.Ready = true;
                    if (voice.Pending && _clock.GetElapsedTime(voice.RequestedAt).TotalSeconds <= .75) Start(voice);
                    else voice.Pending = false;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    voice.Pending = false;
                    voice.Active = false;
                    Failed?.Invoke(this, EventArgs.Empty);
                }
            };
            voice.Player.MediaEnded += (_, _) => { voice.Active = false; CompletedClipCount++; };
            voice.Player.MediaFailed += (_, _) => { voice.Pending = false; voice.Active = false; if (!_disposed) Failed?.Invoke(this, EventArgs.Empty); };
            voice.Player.Open(new Uri(path, UriKind.Absolute));
        }
        voice.Player.Volume = Math.Clamp(volume, 0, 1);
        voice.RequestedAt = _clock.GetTimestamp();
        voice.Pending = true;
        if (voice.Ready) Start(voice);
    }

    private void Start(Voice voice)
    {
        voice.Pending = false;
        if (!voice.Active && _voices.Values.Count(item => item.Active) >= 4)
        {
            var oldest = _voices.Values.Where(item => item.Active).MinBy(item => item.RequestedAt)!;
            oldest.Player.Stop();
            oldest.Active = false;
        }
        voice.Player.Position = TimeSpan.Zero;
        voice.Player.Play();
        voice.Active = true;
        StartedClipCount++;
    }

    public void SetVolume(double volume)
    {
        foreach (var voice in _voices.Values) voice.Player.Volume = Math.Clamp(volume, 0, 1);
        if (_speech is not null) _speech.Volume = Math.Clamp(volume, 0, 1);
    }

    public void PlayVoice(string path, double volume)
    {
        if (_disposed) return;
        StopVoice();
        var player = _speech = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
        var requestedAt = _clock.GetTimestamp();
        player.MediaOpened += (_, _) =>
        {
            if (_disposed || _speech != player) return;
            // Slow opens must not replay a voice after the action has already passed.
            if (_clock.GetElapsedTime(requestedAt).TotalSeconds > 3) { StopVoice(); return; }
            try { player.Play(); SpeechStartedCount++; }
            catch (Exception error) when (error is not OutOfMemoryException) { StopVoice(); Failed?.Invoke(this, EventArgs.Empty); }
        };
        player.MediaEnded += (_, _) => { if (_speech == player) { SpeechCompletedCount++; StopVoice(); } };
        player.MediaFailed += (_, _) => { if (!_disposed && _speech == player) { StopVoice(); Failed?.Invoke(this, EventArgs.Empty); } };
        player.Open(new Uri(path, UriKind.Absolute));
    }

    public void StopVoice()
    {
        var previous = _speech;
        _speech = null;
        previous?.Close();
    }

    public void SetMusic(string? path, double volume)
    {
        if (_disposed) return;
        if (path is not null && path == _musicPath && _music is not null) { _music.Volume = Math.Clamp(volume, 0, 1); return; }
        var previous = _music;
        _music = null;
        _musicPath = null;
        IsMusicReady = false;
        previous?.Close();
        if (path is null) return;
        var player = _music = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
        _musicPath = path;
        player.MediaOpened += (_, _) =>
        {
            if (_disposed || _music != player) return;
            try { IsMusicReady = true; player.Play(); MusicStartedCount++; }
            catch (Exception error) when (error is not OutOfMemoryException) { SetMusic(null, 0); Failed?.Invoke(this, EventArgs.Empty); }
        };
        player.MediaEnded += (_, _) =>
        {
            if (_disposed || _music != player) return;
            try { player.Position = TimeSpan.Zero; player.Play(); }
            catch (Exception error) when (error is not OutOfMemoryException) { SetMusic(null, 0); Failed?.Invoke(this, EventArgs.Empty); }
        };
        player.MediaFailed += (_, _) => { if (!_disposed && _music == player) { SetMusic(null, 0); Failed?.Invoke(this, EventArgs.Empty); } };
        player.Open(new Uri(path, UriKind.Absolute));
    }

    public void StopAll()
    {
        StopVoice();
        SetMusic(null, 0);
        foreach (var voice in _voices.Values)
        {
            voice.Pending = false;
            voice.Active = false;
            if (voice.Ready) voice.Player.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        _disposed = true;
        foreach (var voice in _voices.Values) voice.Player.Close();
        _voices.Clear();
    }

    private sealed class Voice(MediaPlayer player)
    {
        public MediaPlayer Player { get; } = player;
        public bool Ready { get; set; }
        public bool Active { get; set; }
        public bool Pending { get; set; }
        public long RequestedAt { get; set; }
    }
}
