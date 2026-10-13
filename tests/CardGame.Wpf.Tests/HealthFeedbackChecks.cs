using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using static Program;

internal static class HealthFeedbackChecks
{
    public static void CommittedHealth(string output)
    {
        var cues = new ObservableCollection<CardGame.Wpf.Presentation.BattleCue>();
        var clock = new FrameClock();
        var root = new Canvas { Width = 420, Height = 180, Background = Brushes.Black };
        var meter = new HealthMeter { Width = 240, Height = 10, Value = 5, Maximum = 5 };
        Canvas.SetLeft(meter, 70); Canvas.SetTop(meter, 140);
        BattleFeedbackLayer.SetHealthAnchor(meter, 0);
        root.Children.Add(meter);
        var layer = new BattleFeedbackLayer { Width = 420, Height = 180, Cues = cues, AnchorRoot = root, Clock = clock };
        root.Children.Add(layer);
        Capture(root, layer);
        Assert(!meter.Focusable && !meter.IsHitTestVisible && !layer.IsHitTestVisible,
            "Health feedback stole focus or intercepted a table command.");
        Assert(new HealthMeterPeerProbe(meter).Name == "体力 5/5", "The current public HP is not accessible.");
        meter.Value = 3;
        cues.Add(new(1, CardGame.Wpf.Presentation.BattleCueKind.Damage, 1, [0], "−2", "测试")
            { HpDelta = -2, RemainingHp = 3, MaximumHp = 5 });
        clock.Advance(.08);
        var lost = Capture(root, layer);
        Save(lost, Path.Combine(output, "health-track-damage-early.png"));
        Assert(meter.Value == 3 && RedRemainder(lost) > 80, "Committed HP did not update immediately with a visible damage remainder.");
        clock.Advance(.5);
        var retreat = Capture(root, layer);
        Assert(RedRemainder(retreat) > 0 && RedRemainder(retreat) < RedRemainder(lost) / 2,
            "The damage remainder did not retreat toward the current HP.");
        Save(retreat, Path.Combine(output, "health-track-damage-late.png"));
        meter.Value = 4;
        cues.Add(new(2, CardGame.Wpf.Presentation.BattleCueKind.Recovery, 0, [0], "+1", "测试")
            { HpDelta = 1, RemainingHp = 4, MaximumHp = 5 });
        clock.Advance(.08);
        var recovered = Capture(root, layer);
        Assert(RedRemainder(recovered) == 0 && Pixel(recovered, 250, 145).G > 210,
            "A rapid recovery retained an old red remainder or failed to light the gained segment.");
        Save(recovered, Path.Combine(output, "health-track-recovery.png"));
        meter.Maximum = 6;
        var maximumChanged = Capture(root, layer);
        Assert(Pixel(maximumChanged, 210, 145).G < 210,
            "A result from the old maximum painted over the new HP limit.");
        layer.MotionEnabled = false;
        var still = Capture(root, layer);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && meter.Value == 4 &&
               TrackPixels(still).SequenceEqual(TrackPixels(maximumChanged)),
            "Motion off removed current HP or changed a settled track with no applicable result.");
        foreach (var (hp, maximum) in new[] { (-2, 4), (1, 4), (6, 12), (20, 12), (0, 0), (1, 100) })
        {
            meter.Value = hp; meter.Maximum = maximum;
            var frame = Capture(root, layer);
            Assert(new HealthMeterPeerProbe(meter).Name == $"体力 {hp}/{maximum}",
                "Clamping a visual bar hid the public HP value from accessibility.");
            Assert(Pixel(frame, 68, 145) is { R: 0, G: 0, B: 0 } && Pixel(frame, 312, 145) is { R: 0, G: 0, B: 0 },
                "Negative, over-limit or large maximum HP drew outside the track.");
            if (hp <= 0 || maximum <= 0)
                Assert(Pixel(frame, 100, 145).G < 70, "Zero/negative HP rendered as a filled track.");
            if (hp == 1 && maximum == 4)
                Assert(Pixel(frame, 95, 145).R > Pixel(frame, 95, 145).G,
                    "Critical HP lost its warm warning color.");
        }
        cues.Clear();
        meter.Value = 3; meter.Maximum = 5;
        meter.LayoutTransform = new ScaleTransform(.5, .5);
        layer.MotionEnabled = true;
        cues.Add(new(3, CardGame.Wpf.Presentation.BattleCueKind.Damage, 1, [0], "−2", "测试")
            { HpDelta = -2, RemainingHp = 3, MaximumHp = 5 });
        clock.Advance(.08);
        Capture(root, layer);
        var background = Geometry(VisualTreeHelper.GetDrawing(meter)).Single(d => d.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(25, 35, 29));
        var inner = meter.TransformToVisual(layer).TransformBounds(background.Geometry.Bounds);
        var remainder = Geometry(VisualTreeHelper.GetDrawing(layer)).Single(d => d.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 127, 91)).Geometry.Bounds;
        Assert(Math.Abs(remainder.Left - (inner.Left + inner.Width * .6)) < .01 &&
               Math.Abs(remainder.Right - inner.Right) < .01 && Math.Abs(remainder.Top - inner.Top) < .01 &&
               Math.Abs(remainder.Height - inner.Height) < .01,
            "A scaled seat's animated remainder did not align with its actual rendered HP track.");
        layer.ActionBar = meter;
        Capture(root, layer);
        Assert(!Geometry(VisualTreeHelper.GetDrawing(layer)).Any(d => d.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 127, 91)),
            "The HP remainder painted inside a protected command area.");
        layer.ActionBar = null;
        cues.Clear();
        Console.WriteLine("Health tracks: immediate HP, damage retreat, rapid recovery, maximum changes, critical/dying boundaries and motion off passed.");
        NativeRecovery(output);
    }

    private static void NativeRecovery(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RescueDying, output);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false; vm.IsMusicEnabled = false;
        var window = new MainWindow(vm);
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock();
        layer.Clock = clock;
        try
        {
            var root = (FrameworkElement)window.Content;
            Render(root, 1120, 740, Path.Combine(output, "health-native-dying.png"));
            var game = Engine(vm);
            var human = game.CreateSnapshot(0).Players[0];
            Assert(human.Hp <= 0, "The native recovery fixture must begin at actual dying HP.");
            vm.SelectCardCommand.Execute(vm.Hand.First(card => card.Kind == CardKind.Peach && card.IsPlayable));
            Assert(vm.CanConfirmSelected, "The native physical Peach is not confirmable.");
            vm.ConfirmSelectedCommand.Execute(null);
            for (var step = 0; step < 12 && game.CreateSnapshot(0).Players[0].Hp <= human.Hp && vm.CanStepAi; step++)
                vm.StepAiCommand.Execute(null);
            var recovered = game.CreateSnapshot(0).Players[0];
            var fact = game.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Last(e => e.TargetSeat == 0);
            var cue = vm.BattleCues.Last(e => e.Kind == CardGame.Wpf.Presentation.BattleCueKind.Recovery && e.TargetSeats.Contains(0));
            Assert(recovered.Hp > human.Hp && cue.HpDelta == fact.Amount && cue.RemainingHp == fact.RemainingHp && cue.MaximumHp == recovered.MaxHp,
                "The HP track inferred recovery values instead of using the committed public fact.");
            var accepted = game.AcceptedCommands.Count;
            var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            clock.Advance(.2); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "health-native-recovery.png"));
            var control = Find<HealthMeter>(root).Single(m => BattleFeedbackLayer.GetHealthAnchor(m) == 0);
            Assert(control.Value == recovered.Hp && control.Maximum == recovered.MaxHp && control.ActualWidth > 80,
                "The real portrait track is missing, unreadable or detached from current public HP.");
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null);
            Render(root, 1120, 740, Path.Combine(output, "health-native-recovery-loaded.png"));
            var loaded = Engine(vm);
            Assert(layer.ActiveEffectCount == 0 && !vm.HasSaveError && loaded.AcceptedCommands.Count == accepted &&
                   Enumerable.Range(0, 4).All(seat => views[seat] == SnapshotJson.Serialize(loaded.CreateSnapshot(seat))),
                "Saving/loading HP feedback replayed an animation or changed a player-safe snapshot.");
            Assert(control.Value == recovered.Hp && control.Maximum == recovered.MaxHp,
                "A real load reverted the current portrait's HP track.");
            Console.WriteLine($"Native health recovery: HP {human.Hp} -> {recovered.Hp}/{recovered.MaxHp}; public delta {fact.Amount}; 4 cold-loaded views unchanged.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static BitmapSource Capture(Canvas root, BattleFeedbackLayer layer)
    {
        layer.InvalidateVisual();
        root.Measure(new Size(420, 180)); root.Arrange(new Rect(0, 0, 420, 180)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(420, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); return bitmap;
    }
    private static (byte R, byte G, byte B) Pixel(BitmapSource bitmap, int x, int y)
    {
        var bytes = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return (bytes[2], bytes[1], bytes[0]);
    }
    private static byte[] TrackPixels(BitmapSource bitmap)
    {
        var bytes = new byte[240 * 10 * 4]; bitmap.CopyPixels(new Int32Rect(70, 140, 240, 10), bytes, 240 * 4, 0); return bytes;
    }
    private static int RedRemainder(BitmapSource bitmap)
    {
        var count = 0;
        for (var x = 71; x < 309; x++)
        {
            var pixel = Pixel(bitmap, x, 145);
            if (pixel.R > 150 && pixel.R > pixel.G * 1.5 && pixel.G > 40) count++;
        }
        return count;
    }
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static IEnumerable<GeometryDrawing> Geometry(Drawing? drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Geometry(child)) yield return nested;
    }
    private sealed class HealthMeterPeerProbe(HealthMeter meter)
    {
        public string Name => UIElementAutomationPeer.CreatePeerForElement(meter)!.GetName();
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
