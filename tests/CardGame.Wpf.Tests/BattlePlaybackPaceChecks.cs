using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class BattlePlaybackPaceChecks
{
    public static void NativeFastFeedback(string output)
    {
        NativeFastSlash(output);
        NativeSpeedSwitch(output);
        HealthPaceAndContinuity();
        ResultBeatOrder(output);
        if (RecordMotion)
            foreach (var speed in new[] { PlaybackSpeed.Normal, PlaybackSpeed.Fast }) RecordNativeSlash(output, speed);
    }

    private static void NativeFastSlash(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        StartLordFixture(vm); vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]); AdvanceToDecision(vm);
        vm.SelectedPlaybackSpeed = PlaybackSpeed.Fast; vm.IsMotionEnabled = true; vm.IsSoundEnabled = false;
        var game = Engine(vm);
        var action = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.CardId is > 0 && action.ConversionSource is null && vm.Hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Slash));
        var card = vm.Hand.Single(card => card.Id == action.CardId);
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-native-fast-before.png"));
            var accepted = game.AcceptedCommands.Count;
            vm.SelectCardCommand.Execute(card); vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeat));
            vm.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == accepted + 1, "The real paced Slash must still commit exactly once.");
            var cue = vm.BattleCues.Last(cue => cue.PublicCardId == card.Id && cue.Kind == BattleCueKind.Card);
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            clock.Advance(.12); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-native-fast-after120ms.png"));
            var face = Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == cue.Sequence);
            Console.WriteLine($"Native fast pace: speed={vm.SelectedPlaybackSpeed.Id}, model={vm.BattleAnimationTimeScale}, layer={layer.PlaybackTimeScale}, effects={layer.ActiveEffectCount}, face pending={BattleFeedbackLayer.GetIsFlightPending(face)}, opacity={((FrameworkElement)face.Parent).Opacity}, overlays={FaceBounds(layer, card.Artwork).Count()}.");
            Assert(!FaceBounds(layer, card.Artwork).Any() && !BattleFeedbackLayer.GetIsFlightPending(face) &&
                ((FrameworkElement)face.Parent).Opacity > .99,
                "Fast playback retained a normal-length flying card instead of handing off before the next real feedback beat.");
            clock.Advance(.5); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-native-fast-expired.png"));
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && game.AcceptedCommands.Count == accepted + 1 &&
                Enumerable.Range(0, views.Length).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Fast feedback accumulated old effects or changed command/player-view boundaries.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void NativeSpeedSwitch(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, arrowBarrage: true);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false; vm.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-before.png"));
            var game = Engine(vm); var accepted = game.AcceptedCommands.Count;
            var card = vm.Hand.First(card => card.Kind == CardKind.Dodge && card.IsPlayable);
            vm.SelectCardCommand.Execute(card); vm.ConfirmSelectedCommand.Execute(null);
            var cue = vm.BattleCues.Last(cue => cue.PublicCardId == card.Id && cue.Kind == BattleCueKind.Response);
            var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            clock.Advance(.1); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-standard.png"));
            var before = FaceBounds(layer, card.Artwork).Single();
            vm.SelectedPlaybackSpeed = PlaybackSpeed.All.Single(speed => speed.Id == "slow");
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-slow-same-frame.png"));
            Assert(Near(before, FaceBounds(layer, card.Artwork).Single()) && layer.PlaybackTimeScale == 2,
                "Switching to relaxed playback moved or restarted an accepted moving face.");
            clock.Advance(.2); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-slow-moving.png"));
            var later = FaceBounds(layer, card.Artwork).Single();
            Assert(!Near(before, later), "A slowed accepted face stopped progressing.");
            vm.SelectedPlaybackSpeed = PlaybackSpeed.Fast;
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-fast-same-frame.png"));
            Assert(Near(later, FaceBounds(layer, card.Artwork).Single()),
                "Switching an in-flight response to fast playback skipped its current position.");
            clock.Advance(.07); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-fast-arrived.png"));
            var face = Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == cue.Sequence);
            Assert(!FaceBounds(layer, card.Artwork).Any() && !BattleFeedbackLayer.GetIsFlightPending(face),
                "The accelerated remaining response failed to hand off once.");
            clock.Advance(.6); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-pace-switch-expired.png"));
            Assert(layer.ActiveEffectCount == 0 && game.AcceptedCommands.Count == accepted + 1 &&
                Enumerable.Range(0, 4).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Changing animation speed altered committed state or accumulated older feedback.");
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null);
            Assert(!vm.HasSaveError && vm.SelectedPlaybackSpeed == PlaybackSpeed.Fast && layer.ActiveEffectCount == 0 &&
                layer.PlaybackTimeScale == vm.BattleAnimationTimeScale,
                "Loading the selected playback pace restarted history or lost the table's animation binding.");
            Console.WriteLine($"Native speed switch: normal -> slow -> fast, card={card.Id}, command={accepted}->{game.AcceptedCommands.Count}; moving bounds continuous, single arrival, four views unchanged.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void HealthPaceAndContinuity()
    {
        var root = new Canvas { Width = 420, Height = 180, Background = Brushes.Black };
        var clock = new FrameClock(); var cues = new ObservableCollection<BattleCue>();
        var meter = new HealthMeter { Width = 240, Height = 10, Maximum = 5, Value = 3 };
        Canvas.SetLeft(meter, 70); Canvas.SetTop(meter, 140); BattleFeedbackLayer.SetHealthAnchor(meter, 0); root.Children.Add(meter);
        var layer = new BattleFeedbackLayer { Width = 420, Height = 180, AnchorRoot = root, Cues = cues, Clock = clock,
            PlaybackTimeScale = PlaybackSpeed.Fast.IntervalMilliseconds / (double)PlaybackSpeed.Normal.IntervalMilliseconds };
        root.Children.Add(layer);
        cues.Add(new(1, BattleCueKind.Damage, 1, [0], "−2", "公开结果") { HpDelta = -2, RemainingHp = 3, MaximumHp = 5 });
        clock.Advance(.08 * layer.PlaybackTimeScale); Prepare();
        var before = RedRemainder().Single();
        layer.PlaybackTimeScale = 2; Prepare();
        Assert(Near(before, RedRemainder().Single()), "A pace change reset the public HP remainder.");
        clock.Advance(1.6); Prepare();
        Assert(!RedRemainder().Any() && meter.Value == 3,
            "A scaled HP remainder outlived its result beat or changed current public HP.");
        clock.Advance(.6); Prepare();
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "The slowed HP result never expired.");
        layer.MotionEnabled = false; layer.PlaybackTimeScale = 1; layer.MotionEnabled = true; Prepare();
        Assert(layer.ActiveEffectCount == 0, "Changing pace after motion off replayed old HP history.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Console.WriteLine("HP pace: same remainder geometry across fast -> slow; scaled retreat, expiry and motion boundaries passed.");

        void Prepare()
        {
            layer.InvalidateVisual(); root.Measure(new Size(420, 180)); root.Arrange(new Rect(0, 0, 420, 180)); root.UpdateLayout();
            new RenderTargetBitmap(420, 180, 96, 96, PixelFormats.Pbgra32).Render(root);
        }
        IEnumerable<Rect> RedRemainder() => Shapes(VisualTreeHelper.GetDrawing(layer))
            .Where(shape => shape.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 127, 91)).Select(shape => shape.Geometry.Bounds);
    }

    private static IEnumerable<GeometryDrawing> Shapes(Drawing? drawing)
    {
        if (drawing is GeometryDrawing shape) yield return shape;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nested in Shapes(child)) yield return nested;
    }

    private static void ResultBeatOrder(string output)
    {
        using var standard = new BattleEffectsPreview(); using var fast = new BattleEffectsPreview();
        var standardClock = new FrameClock(); var fastClock = new FrameClock();
        standard.FeedbackLayer.Clock = standardClock; fast.FeedbackLayer.Clock = fastClock;
        fast.FeedbackLayer.PlaybackTimeScale = PlaybackSpeed.Fast.IntervalMilliseconds / (double)PlaybackSpeed.Normal.IntervalMilliseconds;
        standard.Play(BattleEffectPreviewKind.HitSequence); fast.Play(BattleEffectPreviewKind.HitSequence);
        Capture(standard); Capture(fast);
        var phase = 0;
        foreach (var step in new[] { .182, .469, .546 })
        {
            phase++;
            standardClock.Advance(step); fastClock.Advance(step * fast.FeedbackLayer.PlaybackTimeScale);
            var normalPixels = Capture(standard); var fastPixels = Capture(fast);
            Assert(normalPixels.Length == fastPixels.Length, "Paced combat changed the shared scene size.");
            var maximumDifference = 0; var maximumIndex = 0; var differentChannels = 0;
            for (var index = 0; index < normalPixels.Length; index++)
            {
                var difference = Math.Abs(normalPixels[index] - fastPixels[index]);
                if (difference > 2) differentChannels++;
                if (difference > maximumDifference) { maximumDifference = difference; maximumIndex = index; }
            }
            if (maximumDifference > 2)
            {
                SaveFrame(standard, Path.Combine(output, $"battle-pace-beat-{phase}-normal.png"));
                SaveFrame(fast, Path.Combine(output, $"battle-pace-beat-{phase}-fast.png"));
            }
            Assert(maximumDifference <= 2,
                $"Fast combat beat {phase} differs at ({maximumIndex / 4 % 880},{maximumIndex / 4 / 880}), channel {maximumIndex % 4}: delta {maximumDifference}, {differentChannels} channels above tolerance.");
        }
        Console.WriteLine("Public combat pace: attack approach, confirmed impact, HP remainder and damage text match standard beats at compressed fast times.");

        static byte[] Capture(BattleEffectsPreview preview)
        {
            preview.FeedbackLayer.InvalidateVisual();
            preview.Scene.Measure(new Size(880, 470)); preview.Scene.Arrange(new Rect(0, 0, 880, 470)); preview.Scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap(880, 470, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview.Scene);
            var pixels = new byte[880 * 470 * 4]; bitmap.CopyPixels(pixels, 880 * 4, 0); return pixels;
        }
        static void SaveFrame(BattleEffectsPreview preview, string path)
        {
            var bitmap = new RenderTargetBitmap(880, 470, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview.Scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path); encoder.Save(file);
        }
    }

    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .01 && Math.Abs(first.Y - second.Y) < .01 &&
        Math.Abs(first.Width - second.Width) < .01 && Math.Abs(first.Height - second.Height) < .01;

    private static void RecordNativeSlash(string output, PlaybackSpeed speed)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        StartLordFixture(vm); vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]); AdvanceToDecision(vm);
        vm.SelectedPlaybackSpeed = speed; vm.IsMotionEnabled = true; vm.IsSoundEnabled = false;
        var action = Engine(vm).GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.CardId is > 0 && action.ConversionSource is null && vm.Hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Slash));
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        try
        {
            root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == action.TargetSeat));
            Render(root, 1120, 740, Path.Combine(output, $"battle-pace-native-{speed.Id}-selected.png"));
            vm.ConfirmSelectedCommand.Execute(null);
            var encoder = new GifBitmapEncoder();
            for (var frame = 0; frame < 30; frame++)
            {
                clock.Advance(.04); layer.InvalidateVisual(); root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var metadata = new BitmapMetadata("gif");
                if (frame == 0)
                {
                    metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                }
                encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null));
            }
            var path = Path.Combine(output, $"battle-pace-native-{speed.Id}.gif");
            using (var file = File.Create(path)) encoder.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
            Console.WriteLine($"Native paced recording: {speed.Id}, 30 frames, 728x481, 40 ms.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static IEnumerable<Rect> FaceBounds(BattleFeedbackLayer layer, ImageSource? artwork) =>
        Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Where(item => ReferenceEquals(item.Image.ImageSource, artwork)).Select(item => item.Bounds);
    private static IEnumerable<(ImageDrawing Image, Rect Bounds)> Images(Drawing? drawing, Matrix transform)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect));
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var nested in Images(child, next)) yield return nested;
        }
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        // Round to the injected clock's resolution, including fractional playback scales.
        public void Advance(double seconds) => _ticks += (long)Math.Round(seconds * TimestampFrequency);
    }
}
