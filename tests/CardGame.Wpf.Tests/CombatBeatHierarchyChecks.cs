using System.IO;
using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class CombatBeatHierarchyChecks
{
    private static readonly Color NumberInk = Color.FromRgb(179, 173, 255);

    public static void NativeChainHierarchy(string output)
    {
        var (registry, fixture, slash) = LongChainFeedbackChecks.CreateFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, fixture.CreateCheckpoint()));
        using var vm = new MainViewModel(false, fixture.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.HasSaveError, vm.SaveStatus);
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false;
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        var width = 1120; var height = 740;
        try
        {
            Capture("before");
            var game = Engine(vm); var cursor = game.Events.Count; var accepted = game.AcceptedCommands.Count;
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == slash.Id));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 1));
            Assert(vm.CanConfirmSelected, "The native hierarchy fixture lost its actual physical eight-seat Slash.");
            Capture("selected");
            vm.ConfirmSelectedCommand.Execute(null);
            var committed = game.Events.Skip(cursor).ToArray();
            var damage = committed.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>().ToArray();
            var chains = committed.Select(envelope => envelope.Payload).OfType<ChainedDamagePropagatedEvent>().ToArray();
            Assert(game.AcceptedCommands.Count == accepted + 1 && damage.Length == 8 && chains.Length == 7 &&
                vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.Damage) == 8 &&
                vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7,
                "The hierarchy fixture must retain all eight actual hits and seven actual relay steps.");
            var views = Enumerable.Range(0, 8).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            Assert(layer.PhaseHud is not null, "The real table has no bound central phase/information HUD anchor.");
            // The number has completed entry at hit + .10s; the previous beat is + .46s.
            clock.Advance(.52);
            for (var recipient = 0; recipient < damage.Length; recipient++)
            {
                Capture($"hit-{recipient + 1}");
                var seats = Find<FrameworkElement>(root).Where(element => BattleFeedbackLayer.GetSeatAnchor(element) >= 0)
                    .ToDictionary(element => BattleFeedbackLayer.GetSeatAnchor(element), element => Bounds(element));
                var numbers = VisibleNumbers(layer).ToArray();
                var owner = seats[damage[recipient].TargetSeat]; var neighborhood = owner; neighborhood.Inflate(40, 40);
                var current = numbers.Where(number => neighborhood.Contains(Center(number.Bounds))).ToArray();
                Console.WriteLine($"Native beat {recipient + 1}, target={damage[recipient].TargetSeat}, pace={layer.PlaybackTimeScale}, owner={owner}; numbers=" +
                    string.Join("; ", numbers.Select(number => $"{number.Bounds}, alpha={number.Opacity:F3}")));
                Assert(current.Length == 1 && current[0].Opacity >= .7,
                    $"The native current hit {recipient + 1} has no clear number near its own portrait.");
                Assert(numbers.Length <= 2,
                    $"The native chain's .36-second beat retained {numbers.Length} foreground numbers; earlier recipients must retire promptly.");
                Assert(numbers.Where(number => !number.Equals(current[0])).All(number =>
                    number.Opacity <= current[0].Opacity + .02 && number.Bounds.Height <= current[0].Bounds.Height + 1),
                    "A preceding chain hit visually outweighs the current confirmed hit.");
                foreach (var number in numbers)
                {
                    Assert(seats.Values.Any(seat => { var local = seat; local.Inflate(40, 40); return local.Contains(Center(number.Bounds)); }),
                        "A chain number wandered into open table space away from its public owner.");
                    foreach (var anchor in new[] { layer.PhaseHud, layer.ActionBar, layer.SupplementalActions, layer.Sidebar })
                        if (anchor is not null)
                        {
                            var bounds = Bounds(anchor);
                            Assert(bounds.IsEmpty || !bounds.IntersectsWith(number.Bounds),
                                "A native number covered the central phase HUD, sidebar or current command area.");
                        }
                }
                if (recipient == 2)
                {
                    SameClock(PlaybackSpeed.All.Single(speed => speed.Id == "slow"), "slow");
                    SameClock(PlaybackSpeed.Fast, "fast");
                }
                if (recipient == 4)
                {
                    width = 1440; height = 880; Capture("resize");
                    var resizedOwner = Bounds(Find<FrameworkElement>(root).Single(element =>
                        BattleFeedbackLayer.GetSeatAnchor(element) == damage[recipient].TargetSeat));
                    resizedOwner.Inflate(40, 40);
                    Assert(VisibleNumbers(layer).Any(number => resizedOwner.Contains(Center(number.Bounds))),
                        "Resizing the native eight-seat table detached its current number from its portrait.");
                }
                clock.Advance(.36 * layer.PlaybackTimeScale);
            }
            clock.Advance(2 * layer.PlaybackTimeScale); Capture("expired");
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && !VisibleNumbers(layer).Any() &&
                game.AcceptedCommands.Count == accepted + 1 && Enumerable.Range(0, 8)
                    .All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Native hierarchy playback retained effects or changed an accepted command/player-safe snapshot.");
            Console.WriteLine("Native combat hierarchy: all eight ordered hits stay near their portraits, current hit dominates, at most two chain numbers, HUD clear, pace/resize continuity and expiry passed.");

            void SameClock(PlaybackSpeed speed, string stage)
            {
                var before = VisibleNumbers(layer).OrderBy(number => number.Bounds.X).ThenBy(number => number.Bounds.Y).ToArray();
                vm.SelectedPlaybackSpeed = speed; Capture($"switch-{stage}");
                var after = VisibleNumbers(layer).OrderBy(number => number.Bounds.X).ThenBy(number => number.Bounds.Y).ToArray();
                Assert(before.Length == after.Length && before.Zip(after).All(pair => Near(pair.First.Bounds, pair.Second.Bounds) &&
                    Math.Abs(pair.First.Opacity - pair.Second.Opacity) < .005),
                    "Changing playback speed at the same public frame moved, restarted or changed a number's prominence.");
            }
        }
        finally { window.Content = null; window.Close(); }

        Rect Bounds(FrameworkElement element)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return Rect.Empty;
            return element.TransformToVisual(layer).TransformBounds(new Rect(new Point(), element.RenderSize));
        }
        void Capture(string stage)
        {
            layer.InvalidateVisual(); Render(root, width, height, Path.Combine(output, $"combat-beat-hierarchy-native-{stage}.png"));
        }
    }

    private readonly record struct NumberDrawing(Rect Bounds, double Opacity);
    private static IEnumerable<NumberDrawing> VisibleNumbers(BattleFeedbackLayer layer) =>
        Shapes(VisualTreeHelper.GetDrawing(layer), Matrix.Identity, 1)
            .Where(shape => shape.Drawing.Pen is null && shape.Drawing.Brush is SolidColorBrush ink &&
                ink.Color == NumberInk && shape.Opacity * ink.Opacity * ink.Color.A / 255d >= .08)
            .Select(shape => new NumberDrawing(shape.Bounds, shape.Opacity * shape.Drawing.Brush!.Opacity));
    private static IEnumerable<(GeometryDrawing Drawing, Rect Bounds, double Opacity)> Shapes(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GeometryDrawing shape)
            yield return (shape, new MatrixTransform(transform).TransformBounds(shape.Geometry.Bounds), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in Shapes(child, next, opacity * group.Opacity)) yield return nested;
        }
    }
    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .05 && Math.Abs(first.Y - second.Y) < .05 &&
        Math.Abs(first.Width - second.Width) < .05 && Math.Abs(first.Height - second.Height) < .05;
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += (long)Math.Round(seconds * TimestampFrequency);
    }
}
