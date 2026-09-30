using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class OfficialAudioChecks
{


    public static void NativeSilentPlayback()
    {
        using var output = new MediaPlayerAudioOutput();
        var failed = false;
        output.Failed += (_, _) => failed = true;
        output.SetMusic(GameAudioCatalog.Music("lobby")!.FilePath, 0);
        Pump(() => failed || output.IsMusicReady, 8);
        Assert(!failed && output.IsMusicReady && output.MusicStartedCount == 1, "Native MP3 music failed to open/start.");
        output.SetMusic(GameAudioCatalog.Music("battle")!.FilePath, 0);
        Pump(() => failed || output.IsMusicReady, 8);
        Assert(!failed && output.IsMusicReady && output.MusicStartedCount == 2, "Native scene transition failed.");
        var mp3 = GameAudioCatalog.ForSkill("classic:ma-dai", "130101", "潜袭").First().Asset;
        var wav = GameAudioCatalog.Assets.First(asset => asset.LocalPath.EndsWith(".wav", StringComparison.Ordinal));
        foreach (var asset in new[] { mp3, wav })
        {
            var completed = output.SpeechCompletedCount;
            output.PlayVoice(asset.FilePath, 0);
            Pump(() => failed || output.SpeechCompletedCount > completed, 20);
            Assert(!failed && output.SpeechCompletedCount > completed, "Native voice did not complete: " + asset.LocalPath);
        }
        var started = output.SpeechStartedCount;
        output.PlayVoice(mp3.FilePath, 0);
        output.StopAll();
        Pump(() => failed, 1);
        Assert(!failed && !output.IsMusicReady && output.SpeechStartedCount == started, "Canceled asynchronous audio replayed.");
        Console.WriteLine("  Official MP3 music/voice and converted PCM WAV opened and played at zero volume; voice clips reached MediaEnded.");
    }

    private static void Pump(Func<bool> done, int seconds)
    {
        var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (done() || watch.Elapsed.TotalSeconds >= seconds) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private sealed class RecordingOutput : IGameAudioOutput
    {
        public event EventHandler? Failed { add { } remove { } }
        public string? Music { get; private set; }
        public double MusicVolume { get; private set; }
        public string? Speech { get; private set; }
        public int SpeechCount { get; private set; }
        public void Play(GameSound sound, double volume) { }
        public void PlayVoice(string path, double volume) { Speech = path; SpeechCount++; }
        public void StopVoice() => Speech = null;
        public void SetMusic(string? path, double volume) { Music = path; MusicVolume = volume; }
        public void SetVolume(double volume) { }
        public void StopAll() { Music = null; Speech = null; }
        public void Dispose() => StopAll();
    }
}
