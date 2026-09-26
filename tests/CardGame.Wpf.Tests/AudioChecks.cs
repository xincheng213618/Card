using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Content.Standard;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class AudioChecks
{
    public static void CommandRouting()
    {
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 721019, false, store) { IsMotionEnabled = false };
        var output = new RecordingOutput();
        var clock = new TestClock();
        var constructed = 0;
        using var audio = new GameAudioController(vm, () => { constructed++; return output; }, clock);
        vm.StartNewGameCommand.Execute(null);
        Assert(constructed == 0, "Background window constructed native audio.");
        audio.SetActive(true);
        Assert(output.Played.Count == 0, "Foreground activation replayed the existing general prompt.");
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        Assert(output.Played.Count(sound => sound == GameSound.YourTurn) == 1, "Playable human turn did not emit one attention cue.");
        var before = output.Played.Count;
        vm.IsDeveloperView = true;
        vm.IsDeveloperView = false;
        vm.SortHandCommand.Execute(null);
        Assert(output.Played.Count == before, "Presentation refresh repeated a prompt sound.");
        vm.SaveGameCommand.Execute(null);
        var savedState = State(vm);
        SelectSlash(vm);
        Assert(output.Played.Count == before, "Selecting a card played an uncommitted sound.");
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(output.Played.Last() == GameSound.Card, "Committed Slash is silent with motion disabled.");
        var stops = output.StopCount;
        vm.IsSoundEnabled = false;
        Assert(output.StopCount > stops, "Mute did not stop active voices.");
        before = output.Played.Count;
        vm.LoadManualGameCommand.Execute(null);
        Assert(State(vm) == savedState && !vm.IsSoundEnabled, "Restore failed or unexpectedly unmuted the player.");
        Assert(output.Played.Count == before, "Restore replayed historical sounds.");
        vm.IsSoundEnabled = true;
        audio.SetActive(false);
        PlaySlash(vm);
        Assert(output.Played.Count == before, "Background AI or player action played audio.");
        audio.SetActive(true);
        Assert(output.Played.Count == before, "Reactivation played missed background audio.");
        vm.LoadManualGameCommand.Execute(null);
        vm.SoundVolume = 0;
        PlaySlash(vm);
        Assert(output.Played.Count == before, "Zero volume still submitted a sound.");
        vm.LoadManualGameCommand.Execute(null);
        vm.SoundVolume = .23;
        clock.Advance(1);
        PlaySlash(vm);
        Assert(output.LastVolume == .23, "App volume did not reach the output.");
        audio.Dispose();
        Assert(output.Disposed, "Closing leaked the audio output.");
        vm.LoadManualGameCommand.Execute(null);
        before = output.Played.Count;
        PlaySlash(vm);
        Assert(output.Played.Count == before, "Disposed controller still receives commands.");

        using var broken = new MainViewModel(false, 721019, false, new MemorySaveStore());
        using var reference = new MainViewModel(false, 721019, false, new MemorySaveStore());
        using var faultyAudio = new GameAudioController(broken, () => new RecordingOutput { ThrowOnPlay = true });
        faultyAudio.SetActive(true);
        foreach (var model in new[] { broken, reference })
        {
            model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
            AdvanceToDecision(model);
            PlaySlash(model);
        }
        Assert(broken.IsAudioUnavailable && State(broken) == State(reference), "Audio failure interrupted or changed the actual rule command.");
        Assert(GameSoundRules.SelectBatch([GameSound.Card, GameSound.Card, GameSound.Hit, GameSound.Prompt]).SequenceEqual([GameSound.Hit, GameSound.Prompt]), "Burst sounds displaced the player's decision cue.");
        Assert(GameSoundRules.SelectBatch([GameSound.Death, GameSound.Hit, GameSound.Victory]).SequenceEqual([GameSound.Victory]), "Result sound did not replace combat sounds.");
        foreach (var role in Enum.GetValues<Role>())
        {
            Assert(GameSoundRules.Outcome(Winner.LordAndLoyalists, role) == (role is Role.Lord or Role.Loyalist ? GameSound.Victory : GameSound.Defeat), "Lord result disagrees with the player's camp.");
            Assert(GameSoundRules.Outcome(Winner.Rebels, role) == (role == Role.Rebel ? GameSound.Victory : GameSound.Defeat), "Rebel result disagrees with the player's camp.");
            Assert(GameSoundRules.Outcome(Winner.Renegade, role) == (role == Role.Renegade ? GameSound.Victory : GameSound.Defeat), "Renegade result disagrees with the player's camp.");
            Assert(GameSoundRules.Outcome(Winner.Draw, role) == GameSound.Draw, "Draw was announced as a win or loss.");
        }
        Assert(GameSoundRules.Outcome(Winner.TeamA, Role.TeamA) == GameSound.Victory &&
               GameSoundRules.Outcome(Winner.TeamA, Role.TeamB) == GameSound.Defeat &&
               GameSoundRules.Outcome(Winner.TeamB, Role.TeamB) == GameSound.Victory &&
               GameSoundRules.Outcome(Winner.TeamB, Role.TeamA) == GameSound.Defeat,
            "Public-team results disagree with the player's team.");
    }

    public static void SettingsAndAssets(string output)
    {
        foreach (var sound in Enum.GetValues<GameSound>())
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", $"{sound.ToString().ToLowerInvariant()}.wav");
            using var input = new BinaryReader(File.OpenRead(path));
            Assert(new string(input.ReadChars(4)) == "RIFF", $"Missing WAV: {sound}.");
            input.BaseStream.Position = 20;
            Assert(input.ReadInt16() == 1 && input.ReadInt16() == 1 && input.ReadInt32() == 44100, "Audio is not portable mono PCM.");
            input.BaseStream.Position = 34;
            Assert(input.ReadInt16() == 16, "Unexpected audio bit depth.");
            input.BaseStream.Position = 40;
            var bytes = input.ReadInt32();
            Assert(bytes == input.BaseStream.Length - 44 && bytes / 88200.0 is > .1 and < 1.5, "Truncated or overlong effect.");
            var samples = Enumerable.Range(0, bytes / 2).Select(_ => input.ReadInt16()).ToArray();
            Assert(samples.Max(sample => Math.Abs((int)sample)) is > 1000 and < 30000 && samples[0] == 0 && samples[^1] == 0,
                "Audio is silent, clipped, or has a discontinuous edge.");
        }

        var store = new FileGameSaveStore(Path.Combine(output, "save-fixtures", $"audio-{Guid.NewGuid():N}"));
        using var vm = new MainViewModel(false, 721019, false, store) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        AdvanceToDecision(vm);
        Render(root, 1120, 740, Path.Combine(output, "18-audio-settings.png"));
        var slider = (Slider)window.FindName("SoundVolumeSlider");
        var toggle = (CheckBox)window.FindName("SoundToggle");
        var state = State(vm);
        slider.Value = .27;
        toggle.IsChecked = false;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert(vm.SoundVolume == .27 && !vm.IsSoundEnabled && !slider.IsEnabled && State(vm) == state, "Sound controls changed the game or failed to bind.");
        Assert(slider.ActualHeight > 0 && slider.TranslatePoint(new Point(slider.ActualWidth, slider.ActualHeight), root) is { X: <= 1120, Y: <= 740 }, "Sound controls overflow the small window.");
        vm.SaveGameCommand.Execute(null);
        var saved = store.Read(GameSaveSlot.Manual);
        Assert(saved.SoundEnabled == false && saved.SoundVolume == .27, "Sound preferences did not reach JSON.");
        vm.SoundVolume = .8;
        vm.IsSoundEnabled = true;
        vm.LoadManualGameCommand.Execute(null);
        Assert(vm.IsSoundEnabled && vm.SoundVolume == .8 && State(vm) == state, "Loading a real save replaced the current device preferences.");
        var original = JsonNode.Parse(File.ReadAllText(store.GetPath(GameSaveSlot.Manual)))!.AsObject();
        original.Remove("SoundEnabled");
        original.Remove("SoundVolume");
        File.WriteAllText(store.GetPath(GameSaveSlot.Manual), original.ToJsonString());
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.HasSaveError && vm.IsSoundEnabled && vm.SoundVolume == .8, "Existing saves without sound fields replaced current preferences.");
        original["SoundVolume"] = 7;
        File.WriteAllText(store.GetPath(GameSaveSlot.Manual), original.ToJsonString());
        vm.LoadManualGameCommand.Execute(null);
        Assert(vm.HasSaveError && State(vm) == state && vm.SoundVolume == .8, "Invalid saved volume replaced the active game.");
        store.Write(GameSaveSlot.Manual, saved);
        vm.LoadManualGameCommand.Execute(null);
        vm.IsSoundEnabled = true;
        Render(root, 1120, 740, Path.Combine(output, "18-audio-settings.png"));
        window.Content = null;
        window.Close();

        var drawEngine = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 721019,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            PlayerCount = 8,
            MaxTurns = 1,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false
        }, StandardContentRegistry.Create());
        Assert(drawEngine.Submit(new StartGameCommand()).Accepted, "Draw fixture did not start.");
        var drawStore = new MemorySaveStore();
        drawStore.Write(GameSaveSlot.Manual, new GameSaveFile(1, DateTimeOffset.UtcNow, false, drawEngine.CreateCheckpoint()));
        using var draw = new MainViewModel(false, 721019, false, drawStore) { IsMotionEnabled = false };
        var drawSounds = new List<GameSound>();
        draw.SoundsRequested += (_, args) => drawSounds.AddRange(args.Sounds);
        draw.LoadManualGameCommand.Execute(null);
        for (var step = 0; step < 100 && !draw.HasGameOver; step++)
        {
            if (draw.IsGeneralSelectionPending) draw.SelectGeneralChoiceCommand.Execute(draw.GeneralChoices[0]);
            else if (draw.IsDiscardSelectionPending) ResolveDiscard(draw);
            else if (draw.CanEndTurn) draw.EndTurnCommand.Execute(null);
            else if (draw.CanStepAi) draw.StepAiCommand.Execute(null);
            else throw new InvalidOperationException("Draw fixture stopped at an unexpected prompt.");
        }
        Assert(draw.HasGameOver && Engine(draw).State.Winner == Winner.Draw && draw.GameOutcomeTitle == "平 局" &&
            !draw.GameOverText.Contains("获胜") && drawSounds.Count(sound => sound == GameSound.Draw) == 1, "Actual drawn game displayed or sounded like a victory.");
        var drawWindow = new MainWindow(draw);
        Render((FrameworkElement)drawWindow.Content, 1120, 740, Path.Combine(output, "19-draw.png"));
        drawWindow.Content = null;
        drawWindow.Close();
    }

    public static void NativeSilentPlayback()
    {
        using var output = new MediaPlayerAudioOutput();
        var failures = 0;
        output.Failed += (_, _) => failures++;
        foreach (var sound in Enum.GetValues<GameSound>())
        {
            var completed = output.CompletedClipCount;
            output.Play(sound, 0); // Exercise the actual WPF/Windows media path without speaker output or a visible window.
            PumpUntil(() => output.CompletedClipCount > completed || failures > 0, TimeSpan.FromSeconds(6));
            Assert(failures == 0 && output.CompletedClipCount == completed + 1, $"Native audio could not decode and finish {sound}.");
        }
        Assert(output.LoadedClipCount == 13 && output.StartedClipCount == 13 && output.ActiveVoiceCount == 0, "Native audio verification did not cover the complete shipped bank.");
        using var cancelled = new MediaPlayerAudioOutput();
        cancelled.Play(GameSound.Card, 0);
        cancelled.StopAll();
        PumpUntil(() => cancelled.LoadedClipCount == 1, TimeSpan.FromSeconds(6));
        Assert(cancelled.StartedClipCount == 0, "A delayed MediaOpened callback replayed a stopped cue.");
        var brokenDirectory = Path.Combine(AppContext.BaseDirectory, "audio-fault-fixture");
        Directory.CreateDirectory(brokenDirectory);
        File.WriteAllText(Path.Combine(brokenDirectory, "card.wav"), "invalid wave contents");
        using var corrupted = new MediaPlayerAudioOutput(brokenDirectory);
        var corruptFailures = 0;
        corrupted.Failed += (_, _) => corruptFailures++;
        corrupted.Play(GameSound.Card, 0);
        PumpUntil(() => corruptFailures > 0, TimeSpan.FromSeconds(6));
        Assert(corruptFailures == 1 && corrupted.ActiveVoiceCount == 0 && corrupted.StartedClipCount == 0,
            "Corrupted audio did not finish through the asynchronous failure path.");
        Console.WriteLine("  All 13 shipped WAVs opened, played silently and reached MediaEnded in the native WPF backend.");
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        timer.Tick += (_, _) => { if (condition() || watch.Elapsed >= timeout) { timer.Stop(); frame.Continue = false; } };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
    private static void SelectSlash(MainViewModel vm)
    {
        var action = Engine(vm).GetHumanLegalActions().First(candidate => candidate.CardId is { } id && candidate.TargetSeats.Count == 1 &&
            candidate.TargetCardId is null && vm.Hand.Single(card => card.Id == id).Name == "杀");
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
        if (!vm.Seats.Single(seat => seat.Seat == action.TargetSeat).IsSelectedTarget)
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeat));
        Assert(vm.CanConfirmSelected, "Audio fixture selected an illegal Slash.");
    }
    private static void PlaySlash(MainViewModel vm) { SelectSlash(vm); vm.ConfirmSelectedCommand.Execute(null); }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }

    private sealed class RecordingOutput : IGameAudioOutput
    {
        public event EventHandler? Failed { add { } remove { } }
        public List<GameSound> Played { get; } = [];
        public int StopCount { get; private set; }
        public double LastVolume { get; private set; }
        public bool Disposed { get; private set; }
        public bool ThrowOnPlay { get; init; }
        public void Play(GameSound sound, double volume)
        {
            if (ThrowOnPlay) throw new InvalidOperationException("Audio output unavailable");
            Played.Add(sound);
            LastVolume = volume;
        }
        public void SetVolume(double volume) => LastVolume = volume;
        public void PlayVoice(string path, double volume) { }
        public void StopVoice() { }
        public void SetMusic(string? path, double volume) { }
        public void StopAll() => StopCount++;
        public void Dispose() => Disposed = true;
    }
}
