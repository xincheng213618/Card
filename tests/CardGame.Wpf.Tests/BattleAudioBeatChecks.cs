using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class BattleAudioBeatChecks
{
    private static readonly MethodInfo FrameTick = typeof(BattleFeedbackLayer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo VmSounds = typeof(MainViewModel).GetField("SoundsRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo VmReset = typeof(MainViewModel).GetField("SoundsReset", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly List<BeatEvent> NativeEvents = [];

    public static void VisualBeats(string output)
    {
        var trace = new List<BeatEvent>();
        ArrivalAndIndependentNotifications(trace);
        LifeOutcomeBeats(trace);
        JudgmentBeats(trace);
        DrawBeats(trace);
        PaceAndThrottle(trace);
        Interruptions(trace);
        LegacyFallback(trace);
        File.WriteAllText(Path.Combine(output, "audio-beat-component-events.json"), JsonSerializer.Serialize(trace, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Audio beat mechanism: Card immediate; Hit420ms, Dodge320ms, Recovery280ms, Axe240ms; pace changes preserve age; notifications independent; interrupted/disabled cues never replay. Component cue/envelope inputs do not submit rules commands.");
    }

    private static void ArrivalAndIndependentNotifications(List<BeatEvent> trace)
    {
        foreach (var (label, first, result, sound, delay) in new[]
        {
            ("hit", Card(1), new BattleCue(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"), GameSound.Hit, .42),
            ("dodge", Card(1), new BattleCue(2, BattleCueKind.Response, 1, [], "打出闪", "公开应答") { CardKind = CardKind.Dodge }, GameSound.Response, .32),
            ("recover", new BattleCue(1, BattleCueKind.Card, 0, [1], "桃", "公开来源") { CardKind = CardKind.Peach },
                new BattleCue(2, BattleCueKind.Recovery, 0, [1], "+1", "公开回复"), GameSound.Recover, .28),
            ("axe", new BattleCue(1, BattleCueKind.Weapon, 0, [1], "贯石斧", "公开来源")
                { WeaponKind = CardKind.StoneAxe, WeaponResolutionId = 10, IsWeaponActivated = true },
                new BattleCue(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"), GameSound.Hit, .24)
        })
        {
            using var fixture = new Fixture("component-" + label);
            fixture.Cues.Add(first); fixture.Cues.Add(result);
            var startup = label == "axe" ? GameSound.StoneAxe : GameSound.Card;
            Assert(fixture.Output.Count(startup) == 1 && fixture.Output.Count(sound) == 0,
                "The public action sound must start immediately while its actual result waits for visual arrival.");
            // This explicit envelope injection exercises only the controller's
            // notification contract. Genuine VM prompts are tested by the
            // fixed native weapon fixture, without fabricated rules outcomes.
            Emit(fixture.Model, [startup, sound, GameSound.Prompt], [GameSound.Prompt]);
            Assert(fixture.Output.Count(startup) == 1 && fixture.Output.Count(sound) == 0 && fixture.Output.Count(GameSound.Prompt) == 1,
                "A new response notification dropped the card/result beat or replayed the VM's immediate combat copy.");
            fixture.Advance(delay - .001);
            Assert(fixture.Output.Count(sound) == 0, "A result sound preceded its public visual arrival.");
            fixture.Clock.Advance(.002);
            var beforeRender = fixture.Output.Events.Count;
            fixture.RenderOnly();
            Assert(fixture.Output.Events.Count == beforeRender, "OnRender published a due sound instead of leaving it to the timeline tick.");
            Tick(fixture.Layer);
            Assert(fixture.Output.Count(sound) == 1 && fixture.Output.Events.Single(item => item.Sound == sound.ToString()).Milliseconds >= delay * 1000 &&
                fixture.Output.Events.Where(item => item.Sound == sound.ToString()).All(item => item.MaximumStartDelayMilliseconds <= 120.001),
                "The due result sound did not follow its visual tick exactly once with a bounded audio start budget.");
            Tick(fixture.Layer); fixture.RenderOnly();
            Assert(fixture.Output.Count(sound) == 1, "Rendering or ticking the same committed result replayed its sound.");
            fixture.AssertUnchanged(); trace.AddRange(fixture.Output.Events);
        }
        using var late = new Fixture("component-late-response");
        late.Cues.Add(Card(1)); late.Advance(1);
        late.Cues.Add(new(2, BattleCueKind.Response, 1, [], "打出闪", "公开应答") { CardKind = CardKind.Dodge });
        Assert(late.Output.Count(GameSound.Response) == 1, "A late paid response waited for an already-expired declaration sound.");
        late.AssertUnchanged(); trace.AddRange(late.Output.Events);
    }

    private static void DrawBeats(List<BeatEvent> trace)
    {
        using var fixture = new Fixture("component-public-draw");
        var draw = new BattleCue(1, BattleCueKind.Draw, 1, [1], "摸牌 +2", "公开摸牌者") { DrawCount = 2 };
        fixture.Cues.Add(draw);
        Assert(fixture.Output.Count(GameSound.Card) == 1, "A committed count-only draw did not sound once at launch.");
        fixture.Advance(.22); fixture.RenderOnly(); Tick(fixture.Layer);
        Assert(fixture.Output.Count(GameSound.Card) == 1, "A draw frame replayed its launch sound.");
        fixture.Layer.MotionEnabled = false; fixture.Layer.MotionEnabled = true;
        fixture.Advance(1);
        Assert(fixture.Output.Count(GameSound.Card) == 1 && fixture.Layer.ActiveEffectCount == 0 && !fixture.Layer.IsFrameTimerRunning,
            "Interrupted draw feedback replayed after motion resumed.");
        Assert(GameSoundRules.FromPublicCues(new[] { draw with { DrawCount = 0 }, draw with { DrawCount = -1 },
            draw with { IsInquiry = true }, draw with { SourceSeat = -1 } }).Count == 0,
            "An empty, invalid or inquiry draw emitted a card sound.");
        fixture.AssertUnchanged(); trace.AddRange(fixture.Output.Events);
    }

    private static void LifeOutcomeBeats(List<BeatEvent> trace)
    {
        using (var dying = new Fixture("component-life-dying"))
        {
            dying.Cues.Add(Card(1));
            dying.Cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"));
            dying.Cues.Add(new(3, BattleCueKind.Dying, 1, [1], "濒死", "公开角色") { LifeResolutionId = 40 });
            dying.Advance(.421);
            Assert(dying.Output.Count(GameSound.Hit) == 1 && dying.Output.Count(GameSound.Dying) == 0,
                "The dying sound preceded its final committed hit.");
            dying.Advance(.298);
            Assert(dying.Output.Count(GameSound.Dying) == 0, "Dying sounded before the portrait impact finished.");
            dying.Advance(.002); Tick(dying.Layer);
            Assert(dying.Output.Count(GameSound.Dying) == 1, "The dying visual beat did not sound exactly once.");
            dying.AssertUnchanged(); trace.AddRange(dying.Output.Events);
        }
        using (var death = new Fixture("component-life-death"))
        {
            death.Cues.Add(Card(1));
            death.Cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"));
            death.Cues.Add(new(3, BattleCueKind.Dying, 1, [1], "濒死", "公开角色") { LifeResolutionId = 41 });
            death.Cues.Add(new(4, BattleCueKind.Death, 1, [1], "阵亡", "公开角色"));
            death.Advance(.421);
            Assert(death.Output.Count(GameSound.Hit) == 1 && death.Output.Count(GameSound.Dying) == 0 &&
                death.Output.Count(GameSound.Death) == 0, "An immediate death sounded an obsolete dying cue or preceded damage.");
            death.Advance(.538);
            Assert(death.Output.Count(GameSound.Death) == 0, "Death sounded before its pending dying boundary.");
            death.Advance(.002); Tick(death.Layer);
            Assert(death.Output.Count(GameSound.Death) == 1 && death.Output.Count(GameSound.Dying) == 0,
                "A committed death missed its visual beat or replayed the superseded dying sound.");
            death.AssertUnchanged(); trace.AddRange(death.Output.Events);
        }
        using (var rescued = new Fixture("component-life-rescued"))
        {
            rescued.Cues.Add(new(1, BattleCueKind.Card, 0, [1], "桃", "公开来源") { CardKind = CardKind.Peach });
            rescued.Cues.Add(new(2, BattleCueKind.Recovery, 0, [1], "+1", "公开回复"));
            rescued.Cues.Add(new(3, BattleCueKind.Rescued, 1, [1], "脱离濒死", "公开角色") { LifeResolutionId = 42 });
            rescued.Advance(.281);
            Assert(rescued.Output.Count(GameSound.Recover) == 1, "Rescue completion swallowed its real recovery sound.");
            rescued.Advance(1);
            Assert(rescued.Output.Count(GameSound.Recover) == 1 && rescued.Output.Count(GameSound.Dying) == 0 &&
                rescued.Output.Count(GameSound.Death) == 0 && rescued.Output.Events.Count == 2,
                "Survival invented an extra sound or duplicated the actual Peach recovery.");
            rescued.AssertUnchanged(); trace.AddRange(rescued.Output.Events);
        }
        Console.WriteLine("Life sound component beats: final Hit420ms -> Dying720ms or Death960ms; superseded dying silent; rescue retains one Recovery280ms.");
    }

    private static void PaceAndThrottle(List<BeatEvent> trace)
    {
        using (var fast = new Fixture("component-fast"))
        {
            fast.Model.SelectedPlaybackSpeed = PlaybackSpeed.Fast;
            fast.Layer.PlaybackTimeScale = fast.Model.BattleAnimationTimeScale;
            var scale = fast.Layer.PlaybackTimeScale;
            fast.Cues.Add(Card(1)); fast.Cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"));
            fast.Advance(.42 * scale - .001);
            Assert(fast.Output.Count(GameSound.Hit) == 0, "Fast playback sounded damage before its scaled visual arrival.");
            fast.Advance(.002);
            Assert(fast.Output.Count(GameSound.Hit) == 1 && Math.Abs(fast.Output.Events.Last().MaximumStartDelayMilliseconds!.Value - 120 * scale) < .01,
                "Fast playback did not scale both the visual sound beat and its maximum native start delay.");
            fast.AssertUnchanged(); trace.AddRange(fast.Output.Events);
        }
        using (var changing = new Fixture("component-mid-beat-speed"))
        {
            changing.Cues.Add(Card(1)); changing.Cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"));
            changing.Advance(.18);
            changing.Model.SelectedPlaybackSpeed = PlaybackSpeed.Fast;
            changing.Layer.PlaybackTimeScale = changing.Model.BattleAnimationTimeScale;
            var remaining = (.42 - .18) * changing.Layer.PlaybackTimeScale;
            changing.Advance(remaining - .001);
            Assert(changing.Output.Count(GameSound.Card) == 1 && changing.Output.Count(GameSound.Hit) == 0,
                "Switching speed replayed the launch or moved an unstarted hit before its preserved visual age.");
            changing.Advance(.002);
            Assert(changing.Output.Count(GameSound.Hit) == 1, "Switching speed restarted the entire arrival delay.");
            changing.AssertUnchanged(); trace.AddRange(changing.Output.Events);
        }
        using (var throttle = new Fixture("component-fast-throttle"))
        {
            throttle.Model.SelectedPlaybackSpeed = PlaybackSpeed.Fast; throttle.Layer.PlaybackTimeScale = throttle.Model.BattleAnimationTimeScale;
            throttle.Cues.Add(Card(1));
            throttle.Advance(.12 * throttle.Layer.PlaybackTimeScale + .001); throttle.Cues.Add(Card(2));
            Assert(throttle.Output.Count(GameSound.Card) == 2, "The old normal throttle swallowed a genuinely new fast combat beat.");
            Emit(throttle.Model, [GameSound.Prompt], [GameSound.Prompt]); throttle.Advance(.1);
            Emit(throttle.Model, [GameSound.Prompt], [GameSound.Prompt]);
            Assert(throttle.Output.Count(GameSound.Prompt) == 1, "Fast combat shortened the independent .35s decision notification throttle.");
            throttle.Advance(.251); Emit(throttle.Model, [GameSound.Prompt], [GameSound.Prompt]);
            Assert(throttle.Output.Count(GameSound.Prompt) == 2, "A genuinely new later notification remained throttled after .35s.");
            throttle.AssertUnchanged(); trace.AddRange(throttle.Output.Events);
        }
    }

    private static void JudgmentBeats(List<BeatEvent> trace)
    {
        var reveal = (double)typeof(BattleFeedbackLayer).Assembly.GetType("CardGame.Wpf.Controls.BattleEffectPainter")!
            .GetField("JudgmentRevealTime", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var requested = new BattleCue(10, BattleCueKind.Judgment, 1, [1], "乐不思蜀 · 判定中", "公开目标")
            { JudgmentStage = JudgmentCueStage.Requested, JudgmentResolutionId = 70, JudgmentTitle = "乐不思蜀", IsInquiry = true };
        var replaced = requested with { Sequence = 11, IsInquiry = false, JudgmentStage = JudgmentCueStage.Replaced, Label = "鬼才改判 · ♥Q" };
        var resolved = requested with { Sequence = 12, IsInquiry = false, JudgmentStage = JudgmentCueStage.Resolved,
            Label = "乐不思蜀 · ♥Q", Detail = "红桃 · 不跳过出牌阶段",
            JudgmentCard = new(41, CardKind.Peach, Suit.Heart, 12, "桃", "Q") };
        using (var fixture = new Fixture("component-judgment-reveal"))
        {
            fixture.Cues.Add(requested); fixture.Cues.Add(replaced); fixture.Cues.Add(resolved);
            Assert(fixture.Layer.ActiveEffectCount == 1 && fixture.Output.Events.Count == 0,
                "One judgment result must retire its request and replacement without premature sound.");
            fixture.Advance(reveal - .001);
            Assert(fixture.Output.Count(GameSound.Card) == 0, "Judgment sounded while its face was still turning.");
            fixture.Advance(.002); Tick(fixture.Layer);
            Assert(fixture.Output.Count(GameSound.Card) == 1, "Revealing one real judgment face must sound once.");
            fixture.Cues.Add(replaced); fixture.Cues.Add(requested); fixture.Advance(.25);
            Assert(fixture.Layer.ActiveEffectCount == 1 && fixture.Output.Count(GameSound.Card) == 1,
                "Stale judgment stages replaced or replayed a newer resolved face.");
            fixture.AssertUnchanged(); trace.AddRange(fixture.Output.Events);
        }
        using (var exhausted = new Fixture("component-judgment-exhausted"))
        {
            exhausted.Cues.Add(resolved with { JudgmentCard = null }); exhausted.Advance(.5);
            Assert(exhausted.Output.Events.Count == 0, "An exhausted judgment invented a card reveal sound.");
            exhausted.AssertUnchanged();
        }
        using (var interrupted = new Fixture("component-judgment-interrupted"))
        {
            interrupted.Cues.Add(resolved); interrupted.Advance(.05);
            interrupted.Layer.MotionEnabled = false; interrupted.Layer.MotionEnabled = true;
            interrupted.Advance(.5);
            Assert(interrupted.Output.Events.Count == 0 && interrupted.Layer.ActiveEffectCount == 0,
                "Re-enabling motion replayed an interrupted judgment reveal.");
            interrupted.AssertUnchanged();
        }
    }

    private static void Interruptions(List<BeatEvent> trace)
    {
        foreach (var reason in new[] { "mute", "zero", "background", "failure", "motion", "unload", "reset", "dispose", "outcome" })
        {
            using var fixture = new Fixture("component-interrupted-" + reason);
            fixture.Cues.Add(Card(1)); fixture.Cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"));
            fixture.Advance(.1); var before = fixture.Output.Events.Count;
            var interruptedOutput = fixture.Output;
            switch (reason)
            {
                case "mute": fixture.Model.IsSoundEnabled = false; break;
                case "zero": fixture.Model.SoundVolume = 0; break;
                case "background": fixture.Audio.SetActive(false); break;
                case "failure": fixture.Output.Fail(); break;
                case "motion": fixture.Model.IsMotionEnabled = false; fixture.Layer.MotionEnabled = false; break;
                case "unload": fixture.Layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
                case "reset": Reset(fixture.Model); fixture.Cues.Clear(); break;
                case "dispose": fixture.Audio.Dispose(); break;
                case "outcome": Emit(fixture.Model, [GameSound.Victory], [GameSound.Victory]); break;
            }
            if (reason is "mute" or "zero" or "background" or "failure" or "motion" or "unload" or "dispose")
            {
                // A result created while silent must also be consumed; merely
                // discarding the old pending result would replay this on resume.
                fixture.Cues.Add(Card(3)); fixture.Cues.Add(new(4, BattleCueKind.Damage, 0, [1], "−1", "公开受伤"));
                Assert(fixture.Output.Events.Count == before, "A new cue created during an audio interruption reached the output.");
            }
            switch (reason)
            {
                case "mute": fixture.Model.IsSoundEnabled = true; break;
                case "zero": fixture.Model.SoundVolume = .4; break;
                case "background": fixture.Audio.SetActive(true); break;
                case "failure": fixture.Model.IsSoundEnabled = false; fixture.Model.IsSoundEnabled = true; break;
                case "motion": fixture.Model.IsMotionEnabled = true; fixture.Layer.MotionEnabled = true; break;
                case "unload": fixture.Layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); break;
            }
            if (reason == "failure")
            {
                Assert(interruptedOutput.Disposed && !ReferenceEquals(interruptedOutput, fixture.Output) && !fixture.Output.Disposed &&
                    fixture.OutputCreationCount == 2 && !fixture.Model.IsAudioUnavailable,
                    "Audio failure recovery reused the disposed output instead of constructing one fresh prepared instance.");
                trace.AddRange(interruptedOutput.Events);
            }
            fixture.Advance(.5);
            Assert(fixture.Output.Count(GameSound.Hit) == 0,
                $"{reason} restored an old pending hit or a delayed result committed while sound was unavailable.");
            if (reason == "outcome")
                Assert(fixture.Output.Count(GameSound.Victory) == 1 && fixture.Output.StopCount > 0,
                    "The outcome notification failed to replace the preceding combat and consume its queued result.");
            if (reason != "dispose")
            {
                var count = fixture.Output.Count(GameSound.Card); fixture.Cues.Add(Card(5));
                Assert(fixture.Output.Count(GameSound.Card) == count + 1,
                    $"{reason} prevented a genuinely new sound after recovery instead of only discarding interrupted history.");
            }
            else Assert(fixture.Output.Disposed && fixture.Output.Events.Count == before, "A disposed controller received later presentation beats.");
            fixture.AssertUnchanged(); trace.AddRange(fixture.Output.Events);
        }
    }

    private static void LegacyFallback(List<BeatEvent> trace)
    {
        using var model = NewModel(); var clock = new BeatClock(); var output = new RecordingOutput(clock, "component-immediate-fallback");
        using (var audio = new GameAudioController(model, () => output, clock))
        {
            audio.SetActive(true); Emit(model, [GameSound.Card, GameSound.Hit], []);
            Assert(output.Count(GameSound.Card) == 1 && output.Count(GameSound.Hit) == 1,
                "A controller without the loaded presentation layer lost the existing immediate sound contract.");
        }
        using var fixture = new Fixture("component-motion-off-fallback");
        fixture.Model.IsMotionEnabled = false; fixture.Layer.MotionEnabled = false;
        Emit(fixture.Model, [GameSound.Card, GameSound.Hit], []);
        Assert(fixture.Output.Count(GameSound.Card) == 1 && fixture.Output.Count(GameSound.Hit) == 1,
            "Motion-disabled play waited for a visual timeline that cannot run.");
        fixture.Model.IsMotionEnabled = true; fixture.Layer.MotionEnabled = true; fixture.Advance(.5);
        Assert(fixture.Output.Count(GameSound.Card) == 1 && fixture.Output.Count(GameSound.Hit) == 1,
            "Enabling motion replayed the sounds already delivered by the immediate fallback.");
        fixture.Cues.Add(Card(3));
        Assert(fixture.Output.Count(GameSound.Card) == 2 && fixture.Output.Count(GameSound.Hit) == 1,
            "Restoring the VM motion flag before the layer binding left its sound gate disabled for a genuinely new card.");
        fixture.AssertUnchanged(); trace.AddRange(output.Events); trace.AddRange(fixture.Output.Events);
        var sounds = new[] { GameSound.Card }; var notifications = new[] { GameSound.Prompt };
        var envelope = new GameSoundsEventArgs(sounds, notifications); sounds[0] = GameSound.Hit; notifications[0] = GameSound.Death;
        Assert(envelope.Sounds.SequenceEqual(new[] { GameSound.Card }) && envelope.NotificationSounds.SequenceEqual(new[] { GameSound.Prompt }),
            "The sound envelope exposed mutable combat or notification arrays.");
        var legacy = new LegacyOutput(); IGameAudioOutput compatible = legacy;
        compatible.PrepareEffects(); compatible.Play(GameSound.Card, .4, TimeSpan.FromMilliseconds(50));
        Assert(legacy.PlayCount == 1, "Default interface additions broke an output implementing only the existing Play method.");
    }

    internal static void Tick(BattleFeedbackLayer layer) => FrameTick.Invoke(layer, [null, EventArgs.Empty]);
    internal static void BeginNativeEvidence() => NativeEvents.Clear();
    internal static void RecordNativeEvidence(RecordingOutput output) => NativeEvents.AddRange(output.Events);
    internal static void WriteNativeEvidence(string output) => File.WriteAllText(Path.Combine(output, "audio-beat-events.json"),
        JsonSerializer.Serialize(NativeEvents, new JsonSerializerOptions { WriteIndented = true }));
    private static MainViewModel NewModel() => new(autoAdvance: false, seed: 721019, showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: false)
    { IsMotionEnabled = true, IsSoundEnabled = true, IsMusicEnabled = false, SoundVolume = .4 };
    private static BattleCue Card(long sequence) => new(sequence, BattleCueKind.Card, 0, [1], "杀", "公开来源") { CardKind = CardKind.Slash };
    private static void Emit(MainViewModel model, IEnumerable<GameSound> sounds, IEnumerable<GameSound> notifications) =>
        ((EventHandler<GameSoundsEventArgs>?)VmSounds.GetValue(model))?.Invoke(model, new GameSoundsEventArgs(sounds, notifications));
    private static void Reset(MainViewModel model) => ((EventHandler?)VmReset.GetValue(model))?.Invoke(model, EventArgs.Empty);

    internal sealed record BeatEvent(string Scenario, string Sound, double Milliseconds, double Volume, double? MaximumStartDelayMilliseconds);
    internal sealed class RecordingOutput(TimeProvider clock, string scenario) : IGameAudioOutput
    {
        private readonly long _start = clock.GetTimestamp();
        public event EventHandler? Failed;
        public List<BeatEvent> Events { get; } = [];
        public int StopCount { get; private set; }
        public int PrepareCount { get; private set; }
        public bool Disposed { get; private set; }
        public int Count(GameSound sound) => Events.Count(item => item.Sound == sound.ToString());
        public double Milliseconds => clock.GetElapsedTime(_start, clock.GetTimestamp()).TotalMilliseconds;
        public void Play(GameSound sound, double volume) => Events.Add(new(scenario, sound.ToString(), Milliseconds, volume, null));
        public void Play(GameSound sound, double volume, TimeSpan maximumStartDelay) => Events.Add(new(scenario, sound.ToString(), Milliseconds, volume, maximumStartDelay.TotalMilliseconds));
        public void PrepareEffects() => PrepareCount++;
        public void Fail() => Failed?.Invoke(this, EventArgs.Empty);
        public void PlayVoice(string path, double volume) { }
        public void StopVoice() { }
        public void SetMusic(string? path, double volume) { }
        public void SetVolume(double volume) { }
        public void StopAll() => StopCount++;
        public void Dispose() => Disposed = true;
    }
    private sealed class LegacyOutput : IGameAudioOutput
    {
        public event EventHandler? Failed { add { } remove { } }
        public int PlayCount { get; private set; }
        public void Play(GameSound sound, double volume) => PlayCount++;
        public void PlayVoice(string path, double volume) { }
        public void StopVoice() { }
        public void SetMusic(string? path, double volume) { }
        public void SetVolume(double volume) { }
        public void StopAll() { }
        public void Dispose() { }
    }
    private sealed class Fixture : IDisposable
    {
        public MainViewModel Model { get; } = NewModel();
        public BeatClock Clock { get; } = new();
        public ObservableCollection<BattleCue> Cues { get; } = [];
        public BattleFeedbackLayer Layer { get; }
        public RecordingOutput Output { get; private set; }
        public int OutputCreationCount { get; private set; } = 1;
        public GameAudioController Audio { get; }
        private readonly long _revision;
        private readonly int _commands, _events, _movements;
        private readonly string[] _views;
        public Fixture(string label)
        {
            Layer = new BattleFeedbackLayer { Clock = Clock, Cues = Cues, MotionEnabled = true };
            Layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Output = new RecordingOutput(Clock, label);
            Audio = new GameAudioController(Model, () =>
            {
                if (Output.Disposed) { Output = new RecordingOutput(Clock, label); OutputCreationCount++; }
                return Output;
            }, Clock, Layer);
            Audio.SetActive(true);
            Assert(Output.PrepareCount > 0 && Layer.IsPresentationActive, "Active audio failed to prepare the effect bank or enable the loaded presentation gate.");
            var game = Engine(Model); _revision = game.Revision; _commands = game.AcceptedCommands.Count; _events = game.Events.Count; _movements = game.CardMovements.Count;
            _views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        }
        public void Advance(double seconds) { Clock.Advance(seconds); Tick(Layer); }
        public void RenderOnly()
        {
            Layer.InvalidateVisual(); Layer.Measure(new Size(1120, 740)); Layer.Arrange(new Rect(0, 0, 1120, 740)); Layer.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(Layer);
        }
        public void AssertUnchanged()
        {
            var game = Engine(Model);
            Assert(game.Revision == _revision && game.AcceptedCommands.Count == _commands && game.Events.Count == _events && game.CardMovements.Count == _movements &&
                Enumerable.Range(0, _views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == _views[seat]),
                "A component audio/timeline operation changed accepted commands, public facts, physical movement or a player-safe snapshot.");
        }
        public void Dispose() { Audio.Dispose(); Layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Model.Dispose(); }
    }
    private sealed class BeatClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
