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
    public static void CatalogAndPublicRouting()
    {
        Assert(GameAudioCatalog.Assets.Count >= 206, "Official audio import is incomplete.");
        Assert(GameAudioCatalog.Assets.Select(asset => asset.DeliveredSha256).Distinct().Count() == GameAudioCatalog.Assets.Count,
            "Duplicated audio content is shipped.");
        foreach (var asset in GameAudioCatalog.Assets)
        {
            using var file = File.OpenRead(asset.FilePath);
            Assert(file.Length == asset.DeliveredBytes && Convert.ToHexString(SHA256.HashData(file)).Equals(asset.DeliveredSha256, StringComparison.OrdinalIgnoreCase), "Audio delivery differs: " + asset.Id);
        }
        Assert(GameAudioCatalog.Music("lobby") is not null && GameAudioCatalog.Music("battle") is not null, "Scene music is missing.");
        var ang = GameAudioCatalog.ForSkill("classic:cao-ang", "240401", "慷忾");
        Assert(ang.Any(voice => voice.LineText.Contains("典将军")) && ang.Any(voice => voice.LineText.Contains("父亲快走")), "Cao Ang voice attribution was lost.");
        Assert(GameAudioCatalog.ForSkill("classic:cao-ang", "140401", "慷忾").Count >= 2, "Shared classic skin bindings were lost.");
        Assert(GameAudioCatalog.ForSkill("classic:cao-ang", "unknown", "慷忾").Count == 0 &&
            GameAudioCatalog.ForSkill("classic:ma-dai", "130101", "慷忾").Count == 0, "Unknown skin or another general borrowed a voice.");

        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(), useExpandedContent: true);
        var player = Engine(vm).CreateSnapshot(0).Players[0] with
        {
            GeneralId = "classic:ma-dai", GeneralName = "马岱", IsGeneralPublic = true,
            Skills = [new GeneralSkillDefinition("潜袭", "") { ContentId = "classic:qianxi" }]
        };
        var trigger = new ProgramBindingResolvedEvent(1, "classic:qianxi", "test", "test-instance", player.Seat,
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, true, true);
        EventEnvelope Wrap(IGameEvent e) => new(new EventId(1), null, 1, 1, "voice-check", e);
        GeneralVoice? Route(IGameEvent e, PlayerSnapshot p, string skin = "130101") => GeneralVoiceProjector.Project([Wrap(e)], [p], _ => skin);
        Assert(Route(trigger, player)?.Binding.SkillName == "潜袭", "Committed program skill did not route its verified voice.");
        Assert(Route(trigger with { Activated = false }, player) is null && Route(trigger with { Completed = false }, player) is null,
            "Declined or incomplete skill produced speech.");
        Assert(Route(trigger, player with { IsGeneralPublic = false }) is null && Route(trigger, player, "unknown") is null,
            "Hidden identity or unknown skin produced speech.");
        Assert(Route(new GeneralSelectedEvent(0, "classic:ma-dai"), player) is null, "Selection produced an uncommitted skill voice.");
    }

    public static void GalleryMusicAndPreferences(string output)
    {
        var store = new FilePlayerPreferencesStore(Path.Combine(output, "official-audio-preferences.json"));
        using (var vm = new MainViewModel(false, 721019, true, new MemorySaveStore(), useExpandedContent: true, preferencesStore: store))
        {
            vm.IsSoundEnabled = true; vm.SoundVolume = .4; vm.IsMusicEnabled = true; vm.IsVoiceEnabled = true;
            var backend = new RecordingOutput();
            using var audio = new GameAudioController(vm, () => backend);
            Assert(backend.Music is null, "Background startup played music.");
            audio.SetActive(true);
            Assert(backend.Music == GameAudioCatalog.Music("lobby")!.FilePath && backend.MusicVolume == .4 * .28, "Lobby music or relative volume is incorrect.");
            vm.OpenGeneralGalleryCommand.Execute(null);
            var entry = vm.GeneralGalleryEntries.Single(e => e.GeneralId == "classic:ma-dai");
            vm.SelectGeneralGalleryEntryCommand.Execute(entry);
            vm.SelectGeneralDetailsTabCommand.Execute("voices");
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            Render(root, 1120, 740, Path.Combine(output, "160-general-voices.png"));
            var revision = Engine(vm).Revision;
            var voice = vm.GeneralVoices.First();
            var button = Find<Button>(root).First(b => b.Command == vm.PlayGeneralVoiceCommand && Equals(b.CommandParameter, voice));
            button.Command!.Execute(button.CommandParameter);
            Assert(backend.Speech == voice.Asset.FilePath && Engine(vm).Revision == revision, "Voice button changed rules or did not play.");
            vm.IsVoiceEnabled = false;
            Assert(backend.Speech is null && backend.Music is not null, "Voice toggle stopped music or left speech active.");
            vm.PlayGeneralVoiceCommand.Execute(voice);
            Assert(backend.Speech is null, "Disabled voice played.");
            vm.IsVoiceEnabled = true;
            vm.PlayGeneralVoiceCommand.Execute(voice);
            vm.CloseGeneralGalleryDetailsCommand.Execute(null);
            Assert(backend.Speech is null, "Closing details left preview active.");
            vm.CloseGeneralGalleryCommand.Execute(null);
            vm.StartNewGameCommand.Execute(null);
            Assert(backend.Music == GameAudioCatalog.Music("battle")!.FilePath, "Starting game did not change scene music.");
            audio.SetActive(false);
            Assert(backend.Music is null && backend.Speech is null, "Background did not stop music/voice.");
            var speechCount = backend.SpeechCount;
            audio.SetActive(true);
            Assert(backend.Music is not null && backend.SpeechCount == speechCount, "Foreground failed to resume music or replayed speech.");
            vm.SoundVolume = 0;
            Assert(backend.Music is null, "Zero volume kept music active.");
            vm.SoundVolume = .32;
            vm.IsMusicEnabled = false;
            Assert(backend.Music is null, "Music toggle failed.");
            vm.IsVoiceEnabled = false;
            Assert(vm.FlushPreferences(), "Audio preferences did not save.");
            vm.IsSettingsOpen = true;
            Render(root, 1120, 740, Path.Combine(output, "161-official-audio-settings.png"));
            Assert(Find<CheckBox>(root).Any(box => box.Name == "SettingsMusicToggle") && Find<CheckBox>(root).Any(box => box.Name == "SettingsVoiceToggle"), "Audio settings missing.");
            window.Content = null; window.Close();
        }
        using var reopened = new MainViewModel(false, 721019, true, new MemorySaveStore(), preferencesStore: store);
        Assert(!reopened.IsMusicEnabled && !reopened.IsVoiceEnabled && reopened.SoundVolume == .32, "Relaunch lost audio preferences.");
        var old = System.Text.Json.JsonSerializer.Deserialize<PlayerPreferences>("{\"FormatVersion\":1,\"SoundEnabled\":true,\"SoundVolume\":0.4,\"MotionEnabled\":false}")!;
        Assert(old.MusicEnabled && old.VoiceEnabled, "Legacy preferences have incorrect new defaults.");
    }

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
