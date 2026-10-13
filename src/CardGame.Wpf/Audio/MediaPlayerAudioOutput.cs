using System.IO;
using System.Windows.Media;

namespace CardGame.Wpf.Audio;

/// <summary>Short local WAV voices with their own volume; no system mixer changes.</summary>
public sealed class MediaPlayerAudioOutput : IGameAudioOutput
{
    private const int MaximumActiveVoices = 4;
    private const int MaximumExtraVoices = 3;
    private readonly Dictionary<GameSound, List<Voice>> _voices = [];
    private readonly TimeProvider _clock;
    private readonly string _directory;
    private bool _disposed;
    private MediaPlayer? _speech;
    private MediaPlayer? _music;
    private string? _musicPath;
    private long _nextActivationId;
    public int SpeechStartedCount { get; private set; }
    public int SpeechCompletedCount { get; private set; }
    public int MusicStartedCount { get; private set; }
    public bool IsMusicReady { get; private set; }
    public event EventHandler? Failed;
    public int LoadedClipCount => _voices.Values.Count(bank => bank.Any(voice => voice.Ready));
    public int LoadedVoiceCount => Voices.Count(voice => voice.Ready);
    public int TotalVoiceCount => Voices.Count();
    public int ActiveVoiceCount => Voices.Count(voice => voice.Active);
    public IReadOnlyList<GameSound> ActiveSounds => Array.AsReadOnly(Voices.Where(voice => voice.Active).Select(voice => voice.Sound).ToArray());
    public int StartedClipCount { get; private set; }
    public int CompletedClipCount { get; private set; }
    public int StoppedClipCount { get; private set; }
    public int DroppedClipCount { get; private set; }

    private IEnumerable<Voice> Voices => _voices.Values.SelectMany(bank => bank);

    public MediaPlayerAudioOutput(string? directory = null, TimeProvider? clock = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Audio");
        _clock = clock ?? TimeProvider.System;
    }

    public void PrepareEffects()
    {
        if (_disposed) return;
        foreach (var sound in Enum.GetValues<GameSound>())
        {
            if (_disposed) return;
            OpenPrimary(sound);
        }
        foreach (var sound in new[] { GameSound.Hit, GameSound.Thunder, GameSound.Response })
        {
            if (_disposed) return;
            if (!_voices.TryGetValue(sound, out var bank) || bank.All(voice => voice.IsPrimary)) OpenExtra(sound);
        }
    }

    public void Play(GameSound sound, double volume) => Play(sound, volume, TimeSpan.FromSeconds(.75));

    public void Play(GameSound sound, double volume, TimeSpan maximumStartDelay)
    {
        if (_disposed) return;
        var requestedAt = _clock.GetTimestamp();
        if (OpenPrimary(sound) is null || _disposed) return;
        var bank = _voices[sound];
        var voice = bank.FirstOrDefault(item => item.Ready && !item.Active && !item.Pending)
            ?? bank.FirstOrDefault(item => !item.Active && !item.Pending)
            ?? OpenExtra(sound);
        if (voice is null) { DroppedClipCount++; return; }
        if (_disposed || voice.Retired) return;
        voice.RequestedAt = requestedAt;
        voice.LastUsedAt = requestedAt;
        voice.MaximumStartDelay = maximumStartDelay < TimeSpan.Zero ? TimeSpan.Zero : maximumStartDelay;
        voice.Pending = true;
        try
        {
            voice.Player.Volume = Math.Clamp(volume, 0, 1);
            if (voice.Ready) Start(voice);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Fail(voice); }
    }

    private Voice? OpenPrimary(GameSound sound)
    {
        if (_disposed) return null;
        if (_voices.TryGetValue(sound, out var bank) && bank.FirstOrDefault(voice => voice.IsPrimary) is { } primary) return primary;
        return OpenEffect(sound, true);
    }

    private Voice? OpenExtra(GameSound sound)
    {
        if (_disposed) return null;
        if (Voices.Count(voice => !voice.IsPrimary) >= MaximumExtraVoices)
        {
            // An opening or playing extra owns its slot. A new cold request must not cut it off before becoming ready.
            var idle = Voices.Where(voice => !voice.IsPrimary && !voice.Active && !voice.Pending).MinBy(voice => voice.LastUsedAt);
            if (idle is null) return null;
            Retire(idle);
        }
        return OpenEffect(sound, false);
    }

    private Voice? OpenEffect(GameSound sound, bool isPrimary)
    {
        if (_disposed) return null;
        var path = Path.Combine(_directory, $"{sound.ToString().ToLowerInvariant()}.wav");
        if (!Enum.IsDefined(sound) || !File.Exists(path)) { Failed?.Invoke(this, EventArgs.Empty); return null; }
        var voice = new Voice(new MediaPlayer { Volume = 0 }, sound, isPrimary) { LastUsedAt = _clock.GetTimestamp() };
        if (!_voices.TryGetValue(sound, out var bank)) _voices.Add(sound, bank = []);
        bank.Add(voice);
        try
        {
            voice.Player.MediaOpened += (_, _) =>
            {
                if (_disposed || voice.Retired) return;
                try
                {
                    voice.Ready = true;
                    if (voice.Pending)
                    {
                        if (_clock.GetElapsedTime(voice.RequestedAt) <= voice.MaximumStartDelay) Start(voice);
                        else { voice.Pending = false; DroppedClipCount++; }
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException) { Fail(voice); }
            };
            voice.Player.MediaFailed += (_, _) => { if (!_disposed && !voice.Retired) Fail(voice); };
            voice.Player.Open(new Uri(path, UriKind.Absolute));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Fail(voice); return null; }
        return voice.Retired ? null : voice;
    }

    private void Start(Voice voice)
    {
        if (_disposed || voice.Retired || voice.Active || !voice.Ready || !voice.Pending) return;
        voice.Pending = false;
        if (ActiveVoiceCount >= MaximumActiveVoices)
        {
            var candidate = Voices.Where(item => item.Active && GameSoundRules.Priority(item.Sound) <= GameSoundRules.Priority(voice.Sound))
                .OrderBy(item => GameSoundRules.Priority(item.Sound)).ThenBy(item => item.StartedAt).FirstOrDefault();
            if (candidate is null) { DroppedClipCount++; return; }
            Retire(candidate);
        }
        var activationId = voice.ActivationId = ++_nextActivationId;
        if (voice.EndedHandler is not null) voice.Player.MediaEnded -= voice.EndedHandler;
        voice.EndedHandler = (_, _) =>
        {
            if (_disposed || voice.Retired || !voice.Active || voice.ActivationId != activationId) return;
            voice.Active = false;
            voice.LastUsedAt = _clock.GetTimestamp();
            CompletedClipCount++;
        };
        voice.Player.MediaEnded += voice.EndedHandler;
        voice.Player.Position = TimeSpan.Zero;
        voice.Player.Play();
        voice.StartedAt = _clock.GetTimestamp();
        voice.Active = true;
        StartedClipCount++;
    }

    private void Retire(Voice voice)
    {
        if (voice.Retired) return;
        voice.Retired = true;
        voice.Pending = false;
        if (voice.Active) StoppedClipCount++;
        voice.Active = false;
        _voices[voice.Sound].Remove(voice);
        if (voice.EndedHandler is not null) voice.Player.MediaEnded -= voice.EndedHandler;
        voice.Player.Close();
    }

    private void Fail(Voice voice)
    {
        if (_disposed || voice.Retired) return;
        try { Retire(voice); }
        finally { if (!_disposed) Failed?.Invoke(this, EventArgs.Empty); }
    }

    public void SetVolume(double volume)
    {
        foreach (var voice in Voices.ToArray()) voice.Player.Volume = Math.Clamp(volume, 0, 1);
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
        foreach (var voice in Voices.ToArray())
        {
            voice.Pending = false;
            if (voice.Active) Retire(voice);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        _disposed = true;
        foreach (var voice in Voices.ToArray()) Retire(voice);
        _voices.Clear();
    }

    private sealed class Voice(MediaPlayer player, GameSound sound, bool isPrimary)
    {
        public MediaPlayer Player { get; } = player;
        public GameSound Sound { get; } = sound;
        public bool IsPrimary { get; } = isPrimary;
        public bool Ready { get; set; }
        public bool Active { get; set; }
        public bool Pending { get; set; }
        public bool Retired { get; set; }
        public long RequestedAt { get; set; }
        public long StartedAt { get; set; }
        public long LastUsedAt { get; set; }
        public long ActivationId { get; set; }
        public EventHandler? EndedHandler { get; set; }
        public TimeSpan MaximumStartDelay { get; set; }
    }
}
