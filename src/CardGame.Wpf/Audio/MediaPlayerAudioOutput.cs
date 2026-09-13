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
    }

    public void StopAll()
    {
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
