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

    internal static void Tick(BattleFeedbackLayer layer) => FrameTick.Invoke(layer, [null, EventArgs.Empty]);
    internal static void BeginNativeEvidence() => NativeEvents.Clear();
    internal static void RecordNativeEvidence(RecordingOutput output) => NativeEvents.AddRange(output.Events);
    internal static void WriteNativeEvidence(string output) => File.WriteAllText(Path.Combine(output, "audio-beat-events.json"),
        JsonSerializer.Serialize(NativeEvents, new JsonSerializerOptions { WriteIndented = true }));
    private static MainViewModel NewModel() => new(autoAdvance: false, seed: 721019, showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: false)
    { IsMotionEnabled = true, IsSoundEnabled = true, IsMusicEnabled = false, SoundVolume = .4 };
    private static BattleCue Card(long sequence) => new(sequence, BattleCueKind.Card, 0, [1], "杀", "公开来源") { CardKind = CardKind.Slash };

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
