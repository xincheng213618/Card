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
        StartLordFixture(vm);
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
            StartLordFixture(model);
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

    public static void NativeSilentPlayback()
    {
        using var output = new MediaPlayerAudioOutput();
        var failures = 0;
        output.Failed += (_, _) => failures++;
        var sounds = Enum.GetValues<GameSound>();
        output.PrepareEffects();
        output.PrepareEffects();
        PumpUntil(() => output.LoadedClipCount == sounds.Length && output.LoadedVoiceCount == sounds.Length + 3 || failures > 0,
            TimeSpan.FromSeconds(6));
        Assert(failures == 0 && output.LoadedClipCount == sounds.Length &&
               output.LoadedVoiceCount == sounds.Length + 3 && output.TotalVoiceCount == sounds.Length + 3 &&
               output.StartedClipCount == 0 && output.ActiveVoiceCount == 0 && output.CompletedClipCount == 0,
            "Preparing the complete effect bank must reuse silent opened clips without pending playback.");
        output.PrepareEffects();
        Assert(output.LoadedClipCount == sounds.Length && output.StartedClipCount == 0 && output.ActiveVoiceCount == 0,
            "Preparing an already-ready bank restarted or duplicated an effect.");
        foreach (var sound in sounds)
        {
            var completed = output.CompletedClipCount;
            output.Play(sound, 0); // Exercise the actual WPF/Windows media path without speaker output or a visible window.
            PumpUntil(() => output.CompletedClipCount > completed || failures > 0, TimeSpan.FromSeconds(6));
            Assert(failures == 0 && output.CompletedClipCount == completed + 1, $"Native audio could not decode and finish {sound}.");
        }
        Assert(output.LoadedClipCount == sounds.Length && output.StartedClipCount == sounds.Length && output.ActiveVoiceCount == 0,
            "Native audio verification did not cover the complete shipped bank.");

        NativePolyphonicPlayback(sounds.Length);

        var deadlineClock = new TestClock();
        using var expired = new MediaPlayerAudioOutput(clock: deadlineClock);
        var deadlineFailures = 0;
        expired.Failed += (_, _) => deadlineFailures++;
        expired.Play(GameSound.Hit, 0, TimeSpan.FromSeconds(.12));
        deadlineClock.Advance(.13); // No dispatcher pump has delivered the cold MediaOpened callback yet.
        PumpUntil(() => expired.LoadedClipCount == 1 || deadlineFailures > 0, TimeSpan.FromSeconds(6));
        Assert(deadlineFailures == 0 && expired.LoadedClipCount == 1 && expired.StartedClipCount == 0 && expired.ActiveVoiceCount == 0,
            "A cold effect that missed its own start deadline replayed after becoming ready.");
        expired.Play(GameSound.Hit, 0, TimeSpan.Zero);
        Assert(expired.StartedClipCount == 1 && expired.ActiveVoiceCount == 1,
            "A fresh request for a ready effect did not start immediately after an earlier request expired.");
        PumpUntil(() => expired.CompletedClipCount == 1 || deadlineFailures > 0, TimeSpan.FromSeconds(6));
        Assert(deadlineFailures == 0 && expired.CompletedClipCount == 1 && expired.ActiveVoiceCount == 0,
            "The fresh ready effect did not finish through the native media path.");
        expired.Play(GameSound.Response, 0, TimeSpan.FromSeconds(.12));
        expired.StopAll();
        PumpUntil(() => expired.LoadedClipCount == 2 || deadlineFailures > 0, TimeSpan.FromSeconds(6));
        Assert(deadlineFailures == 0 && expired.LoadedClipCount == 2 && expired.StartedClipCount == 1 && expired.ActiveVoiceCount == 0,
            "Stopping a cold timed effect failed to cancel its pending MediaOpened start.");

        var ordinaryClock = new TestClock();
        using var ordinary = new MediaPlayerAudioOutput(clock: ordinaryClock);
        var ordinaryFailures = 0;
        ordinary.Failed += (_, _) => ordinaryFailures++;
        ordinary.Play(GameSound.Card, 0);
        ordinaryClock.Advance(.5);
        PumpUntil(() => ordinary.CompletedClipCount == 1 || ordinaryFailures > 0, TimeSpan.FromSeconds(6));
        Assert(ordinaryFailures == 0 && ordinary.StartedClipCount == 1 && ordinary.CompletedClipCount == 1 && ordinary.ActiveVoiceCount == 0,
            "The ordinary notification path lost its existing 750ms cold-open grace.");
        using var cancelled = new MediaPlayerAudioOutput();
        var cancelFailures = 0;
        cancelled.Failed += (_, _) => cancelFailures++;
        cancelled.Play(GameSound.Card, 0);
        cancelled.StopAll();
        PumpUntil(() => cancelled.LoadedClipCount == 1 || cancelFailures > 0, TimeSpan.FromSeconds(6));
        Assert(cancelFailures == 0 && cancelled.LoadedClipCount == 1 && cancelled.ActiveVoiceCount == 0 && cancelled.StartedClipCount == 0,
            "A cancelled cold cue failed to decode silently or replayed from its delayed MediaOpened callback.");
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
        Console.WriteLine($"  All {sounds.Length} shipped WAVs preloaded without playback, then played silently to native MediaEnded; overlapping voices, priority/cache saturation and StopAll close/reopen verified; timed cold starts expired/cancelled without replay.");
    }

    private static void NativePolyphonicPlayback(int soundCount)
    {
        var clock = new TestClock();
        using var output = new MediaPlayerAudioOutput(clock: clock);
        var failures = 0;
        output.Failed += (_, _) => failures++;

        void PrepareBank()
        {
            output.PrepareEffects();
            PumpUntil(() => output.LoadedClipCount == soundCount && output.LoadedVoiceCount == soundCount + 3 || failures > 0,
                TimeSpan.FromSeconds(6));
            Assert(failures == 0 && output.LoadedClipCount == soundCount && output.LoadedVoiceCount == soundCount + 3 &&
                   output.TotalVoiceCount == soundCount + 3 && output.ActiveVoiceCount == 0,
                "The native overlap fixture must warm the complete bank and all three shared extra voices without playback.");
        }

        void Play(GameSound sound)
        {
            output.Play(sound, 0, TimeSpan.Zero);
            clock.Advance(.001);
        }

        void Finish(int expectedCompleted)
        {
            PumpUntil(() => output.CompletedClipCount >= expectedCompleted && output.ActiveVoiceCount == 0 || failures > 0,
                TimeSpan.FromSeconds(6));
            Assert(failures == 0 && output.CompletedClipCount == expectedCompleted && output.ActiveVoiceCount == 0 &&
                   output.TotalVoiceCount <= soundCount + 3,
                "Native voices did not each finish exactly once, or a stopped/dropped request was counted as completed.");
        }

        void Trace(string stage) => Console.WriteLine(
            $"  Native audio {stage}: started={output.StartedClipCount}, completed={output.CompletedClipCount}, " +
            $"active={output.ActiveVoiceCount}, stopped={output.StoppedClipCount}, dropped={output.DroppedClipCount}, " +
            $"voiceCache={output.TotalVoiceCount}, protectedPrompt={output.ActiveSounds.Count(sound => GameSoundRules.Priority(sound) == 80)}.");

        PrepareBank();
        output.Play(GameSound.Hit, 0, TimeSpan.Zero);
        // The fake clock determines request age; actual MediaEnded still comes
        // only from WPF/Windows playback while the real dispatcher is pumped.
        // With no pump between calls, the first Hit cannot have retired yet.
        clock.Advance(.121);
        output.Play(GameSound.Hit, 0, TimeSpan.Zero);
        Assert(output.StartedClipCount == 2 && output.ActiveVoiceCount == 2 && output.CompletedClipCount == 0 &&
               output.ActiveSounds.Count(sound => sound == GameSound.Hit) == 2,
            "Two warm native Hit requests reset one active player instead of starting two overlapping voices.");
        Trace("same-Hit-overlap");
        Finish(2);
        Assert(output.StartedClipCount == 2 && output.StoppedClipCount == 0 && output.DroppedClipCount == 0,
            "The two overlapping native Hit voices did not both play through their own MediaEnded.");
        Trace("same-Hit-completed");

        PrepareBank();
        var started = output.StartedClipCount;
        var completed = output.CompletedClipCount;
        var stopped = output.StoppedClipCount;
        var dropped = output.DroppedClipCount;
        Play(GameSound.Prompt); // Deliberately oldest, but higher priority than combat.
        Play(GameSound.Card);
        Play(GameSound.Response);
        Play(GameSound.StoneAxe);
        Assert(output.ActiveVoiceCount == 4, "The native priority fixture did not fill all four effect voices.");
        clock.Advance(.121);
        Play(GameSound.Hit);
        Assert(output.ActiveVoiceCount == 4 && output.StartedClipCount == started + 5 && output.StoppedClipCount == stopped + 1 &&
               !output.ActiveSounds.Contains(GameSound.Card) && output.ActiveSounds.Contains(GameSound.Prompt) &&
               output.ActiveSounds.Contains(GameSound.Hit) && output.DroppedClipCount == dropped,
            "A new Hit must replace the lowest-priority Card while preserving the older response prompt.");
        Play(GameSound.YourTurn);
        Assert(output.ActiveVoiceCount == 4 && output.StartedClipCount == started + 6 && output.StoppedClipCount == stopped + 2 &&
               !output.ActiveSounds.Contains(GameSound.Response) && output.ActiveSounds.Contains(GameSound.YourTurn) &&
               output.ActiveSounds.Contains(GameSound.Prompt),
            "A new critical notification was delayed by ordinary combat or replaced the existing protected prompt.");
        Trace("priority-protected-prompts");
        Finish(completed + 4);
        Assert(output.StartedClipCount == started + 6 && output.DroppedClipCount == dropped,
            "Retired combat voices replayed later instead of leaving room for the new notification.");

        PrepareBank();
        started = output.StartedClipCount;
        completed = output.CompletedClipCount;
        stopped = output.StoppedClipCount;
        dropped = output.DroppedClipCount;
        Play(GameSound.Hit);
        Play(GameSound.Fire);
        Play(GameSound.Thunder);
        Play(GameSound.Prompt);
        Play(GameSound.Thunder);
        Assert(output.ActiveVoiceCount == 4 && output.StartedClipCount == started + 5 && output.StoppedClipCount == stopped + 1 &&
               !output.ActiveSounds.Contains(GameSound.Hit) && output.ActiveSounds.Count(sound => sound == GameSound.Thunder) == 2 &&
               output.ActiveSounds.Contains(GameSound.Fire) && output.ActiveSounds.Contains(GameSound.Prompt),
            "Equal-priority saturation did not retire the oldest active combat voice while preserving the prompt.");
        Trace("equal-priority-oldest");
        Finish(completed + 4);

        PrepareBank();
        started = output.StartedClipCount;
        completed = output.CompletedClipCount;
        stopped = output.StoppedClipCount;
        dropped = output.DroppedClipCount;
        Play(GameSound.Prompt);
        Play(GameSound.YourTurn);
        Play(GameSound.Dying);
        Play(GameSound.Death);
        var protectedSounds = output.ActiveSounds.Order().ToArray();
        Play(GameSound.Card);
        Assert(output.ActiveVoiceCount == 4 && output.StartedClipCount == started + 4 && output.StoppedClipCount == stopped &&
               output.DroppedClipCount == dropped + 1 && output.ActiveSounds.Order().SequenceEqual(protectedSounds),
            "A low-priority Card stole a protected notification/death voice instead of being dropped immediately.");
        Trace("low-priority-dropped");
        Finish(completed + 4);
        Assert(output.StartedClipCount == started + 4 && output.DroppedClipCount == dropped + 1,
            "A dropped low-priority sound was queued and played after the protected voices ended.");

        PrepareBank();
        started = output.StartedClipCount;
        completed = output.CompletedClipCount;
        stopped = output.StoppedClipCount;
        Play(GameSound.Hit);
        Play(GameSound.Hit);
        Assert(output.ActiveVoiceCount == 2, "The native StopAll fixture did not start both warm Hit voices.");
        output.StopAll();
        Assert(output.ActiveVoiceCount == 0 && output.StoppedClipCount == stopped + 2 && output.CompletedClipCount == completed,
            "Stopping active voices counted them as natural completions or left them active.");
        clock.Advance(.13);
        // StopAll closes active players. This new request may open a replacement
        // voice, so retain the ordinary bounded cold-open grace.
        output.Play(GameSound.Hit, 0);
        PumpUntil(() => output.StartedClipCount >= started + 3 || failures > 0, TimeSpan.FromSeconds(6));
        Assert(failures == 0 && output.StartedClipCount == started + 3 && output.ActiveVoiceCount == 1 &&
               output.CompletedClipCount == completed,
            "A retired native callback replayed a stopped Hit or prevented a genuinely new request from starting.");
        Finish(completed + 1);
        Assert(output.StartedClipCount == started + 3 && output.StoppedClipCount == stopped + 2,
            "A stopped player produced a late completion/start alongside the fresh native Hit.");
        Trace("StopAll-fresh-only-completed");

        // Use the real clock for the two new cold extras: they must satisfy the
        // ordinary 750ms start budget as well as the bounded cache policy.
        using var bounded = new MediaPlayerAudioOutput(clock: TimeProvider.System);
        var boundedFailures = 0;
        bounded.Failed += (_, _) => boundedFailures++;
        bounded.PrepareEffects();
        PumpUntil(() => bounded.LoadedClipCount == soundCount && bounded.LoadedVoiceCount == soundCount + 3 || boundedFailures > 0,
            TimeSpan.FromSeconds(6));
        Assert(boundedFailures == 0 && bounded.LoadedClipCount == soundCount && bounded.LoadedVoiceCount == soundCount + 3 &&
               bounded.TotalVoiceCount == soundCount + 3 && bounded.ActiveVoiceCount == 0,
            "The native shared-cache fixture did not warm its bank and all three idle extras.");
        for (var index = 0; index < 4; index++) bounded.Play(GameSound.Hit, 0);
        // The last two Hits may still be pending MediaOpened; pending extras
        // already own their slots and cannot be recycled for the fifth request.
        var coldStarted = bounded.StartedClipCount;
        bounded.Play(GameSound.Hit, 0);
        Assert(boundedFailures == 0 && bounded.TotalVoiceCount == soundCount + 3 && bounded.StartedClipCount == coldStarted &&
               bounded.ActiveVoiceCount <= 4 && bounded.DroppedClipCount == 1 && bounded.StoppedClipCount == 0,
            "The fifth same-sound request created a nineteenth voice, recycled a busy extra, or cut an active Hit before cold opening.");
        Console.WriteLine($"  Native audio cache-saturation-cold: started={bounded.StartedClipCount}, completed={bounded.CompletedClipCount}, " +
                          $"active={bounded.ActiveVoiceCount}, stopped={bounded.StoppedClipCount}, dropped={bounded.DroppedClipCount}, voiceCache={bounded.TotalVoiceCount}.");
        PumpUntil(() => bounded.CompletedClipCount >= 4 && bounded.ActiveVoiceCount == 0 || boundedFailures > 0, TimeSpan.FromSeconds(6));
        Assert(boundedFailures == 0 && bounded.StartedClipCount == 4 && bounded.CompletedClipCount == 4 && bounded.ActiveVoiceCount == 0 &&
               bounded.TotalVoiceCount == soundCount + 3 && bounded.LoadedVoiceCount == soundCount + 3 &&
               bounded.DroppedClipCount == 1 && bounded.StoppedClipCount == 0,
            "The four native Hit requests did not survive shared idle-extra recycling and each finish once within their ordinary start budget.");
        // Do not prepare again: all three shared extras now remain ready Hits.
        for (var index = 0; index < 4; index++) bounded.Play(GameSound.Hit, 0, TimeSpan.Zero);
        Assert(bounded.StartedClipCount == 8 && bounded.ActiveVoiceCount == 4 && bounded.CompletedClipCount == 4 &&
               bounded.ActiveSounds.Count(sound => sound == GameSound.Hit) == 4,
            "Four ready native Hit voices failed to reuse the bounded shared cache concurrently.");
        bounded.Play(GameSound.Hit, 0, TimeSpan.Zero);
        Assert(bounded.StartedClipCount == 8 && bounded.ActiveVoiceCount == 4 && bounded.DroppedClipCount == 2 &&
               bounded.StoppedClipCount == 0 && bounded.TotalVoiceCount == soundCount + 3,
            "A fifth Hit restarted or preempted a busy same-sound voice instead of dropping at the shared-cache limit.");
        Console.WriteLine($"  Native audio cache-saturation-warm: started={bounded.StartedClipCount}, completed={bounded.CompletedClipCount}, " +
                          $"active={bounded.ActiveVoiceCount}, stopped={bounded.StoppedClipCount}, dropped={bounded.DroppedClipCount}, voiceCache={bounded.TotalVoiceCount}.");
        PumpUntil(() => bounded.CompletedClipCount >= 8 && bounded.ActiveVoiceCount == 0 || boundedFailures > 0, TimeSpan.FromSeconds(6));
        Assert(boundedFailures == 0 && bounded.StartedClipCount == 8 && bounded.CompletedClipCount == 8 && bounded.ActiveVoiceCount == 0 &&
               bounded.DroppedClipCount == 2 && bounded.StoppedClipCount == 0 && bounded.TotalVoiceCount == soundCount + 3,
            "Saturated native Hit voices were truncated, replayed a dropped request, or did not all reach their own MediaEnded.");
        Console.WriteLine($"  Native audio cache-saturation-completed: started={bounded.StartedClipCount}, completed={bounded.CompletedClipCount}, " +
                          $"active={bounded.ActiveVoiceCount}, stopped={bounded.StoppedClipCount}, dropped={bounded.DroppedClipCount}, voiceCache={bounded.TotalVoiceCount}.");
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
