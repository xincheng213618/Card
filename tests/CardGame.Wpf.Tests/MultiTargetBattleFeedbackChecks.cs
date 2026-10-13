using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class MultiTargetBattleFeedbackChecks
{
    public static void NativeGroupTargets(string output)
    {
        NativeGroup(output, CardKind.ArrowBarrage, record: false);
        NativeGroup(output, CardKind.BarbarianAssault, record: false);
        foreach (var scale in new[] { 1d, 2d / 7 }) WaitingTargetLifecycle(scale);
        if (RecordMotion) NativeGroup(output, CardKind.ArrowBarrage, record: true);
    }

    private static void NativeGroup(string output, CardKind incoming, bool record)
    {
        var fixture = StableHandResponseFixture.Create(incoming == CardKind.ArrowBarrage
            ? DecisionKind.RespondDodge : DecisionKind.RespondSlash, output,
            arrowBarrage: incoming == CardKind.ArrowBarrage);
        GameCheckpoint beforeDeclaration;
        using (fixture.Model)
        {
            var original = Engine(fixture.Model);
            var declared = original.Events.Single(item => item.Payload is CardUseDeclaredEvent use && use.CardKind == incoming);
            var checkpoint = original.CreateCheckpoint();
            var prefix = GameReplay.Replay(checkpoint.Options, checkpoint.Commands.Take((int)declared.Revision - 1), fixture.Registry);
            beforeDeclaration = prefix.CreateCheckpoint();
            Assert(prefix.PendingDecision?.PlayerSeat != 0,
                "The fixed group prefix must stop immediately before its real AI declaration.");
        }
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, beforeDeclaration));
        using var vm = new MainViewModel(false, beforeDeclaration.Options.Seed, true, store,
            useExpandedContent: false, contentRegistry: fixture.Registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.HasSaveError, vm.SaveStatus);
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false; vm.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        var encoder = record ? new GifBitmapEncoder() : null;
        var tag = incoming == CardKind.ArrowBarrage ? "arrow" : "barbarian";
        var game = Engine(vm); var cursor = game.Events.Count; var accepted = game.AcceptedCommands.Count;
        var hpBefore = game.CreateSnapshot(0).Players.ToDictionary(player => player.Seat, player => player.Hp);
        var humanHpBefore = hpBefore[0];
        try
        {
            Capture("before");
            Assert(vm.CanStepAi, "The fixed native group declaration must be available to the existing AI step action.");
            vm.StepAiCommand.Execute(null);
            var declared = game.Events.Skip(cursor).Select(item => item.Payload).OfType<GroupCardUsedEvent>().Single();
            Assert(declared.CardKind == incoming && declared.SourceSeat == 1 && declared.TargetSeats.SequenceEqual([2, 3, 0]),
                "The native group fixture lost its real clockwise public target order.");
            Assert(vm.BattleCues.Single(cue => cue.Kind == BattleCueKind.Card && cue.CardKind == incoming)
                .TargetSeats.SequenceEqual(declared.TargetSeats),
                "The native group presentation lost an actually confirmed target.");
            Advance(.08); Capture("target-2");
            CheckCurrentTarget(2, [3, 0]);
            Frames(5);
            vm.StepAiCommand.Execute(null); Advance(.06); Capture("target-3");
            VerifyResponse(2);
            CheckCurrentTarget(3, [0]);
            if (incoming == CardKind.BarbarianAssault)
            {
                Advance(.36); Capture("target-2-result");
                Assert(Impacts(2) == game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent { TargetSeat: 2 }) &&
                    !Impacts(3) && !Impacts(0),
                    "The first native Barbarian result did not follow its actual response, or struck a future recipient.");
            }
            Frames(5);
            vm.StepAiCommand.Execute(null); Advance(.06); Capture("target-0");
            var responses = game.Events.Skip(cursor).Select(item => item.Payload).OfType<GroupResponseEvent>().ToArray();
            VerifyResponse(3);
            if (incoming == CardKind.BarbarianAssault)
                Assert(Impacts(3) && !Impacts(0),
                    "The second native Barbarian hit must appear after its declined response, before the pending human is struck.");
            Assert(responses.Select(response => response.ResponderSeat).SequenceEqual([2, 3]),
                "The actual group must expose exactly the two earlier responses before the human.");
            if (incoming == CardKind.ArrowBarrage)
                Assert(responses.All(response => response.UsedResponse),
                    "The fixed native Arrow Barrage lost either of its two actual physical Dodge responses.");
            CheckCurrentTarget(0, []);
            Assert(vm.CanDeclineResponse && game.CreateSnapshot(0).Players[0].Hp == humanHpBefore,
                "The human lost HP to this group while its real response was still pending.");
            Frames(5);
            vm.DeclineResponseCommand.Execute(null);
            var damage = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray();
            VerifyResponse(0);
            var humanDamage = damage.Single(hit => hit.TargetSeat == 0);
            var failedSeats = game.Events.Skip(cursor).Select(item => item.Payload).OfType<GroupResponseEvent>()
                .Where(response => !response.UsedResponse).Select(response => response.ResponderSeat).ToArray();
            Assert(damage.Select(hit => hit.TargetSeat).SequenceEqual(failedSeats) &&
                damage.All(hit => hit is { SourceSeat: 1, Amount: 1 } && hit.RemainingHp == hpBefore[hit.TargetSeat] - hit.Amount) &&
                humanDamage.RemainingHp == humanHpBefore - 1,
                "The group damage lost an actual unsuccessful response or struck a successfully defended recipient.");
            if (incoming == CardKind.ArrowBarrage)
                Assert(damage.Length == 1 && humanDamage.TargetSeat == 0,
                    "The fixed native Arrow Barrage must damage only the human that declined its actual response.");
            var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            var commandsAfter = game.AcceptedCommands.Count;
            // Inspect the current hit's brief arrival window. Earlier actual
            // hits were checked at their own response boundary, rather than
            // requiring every past impact to remain bright simultaneously.
            for (var frame = 0; frame < 12 && !Impacts(0); frame++)
            { Advance(.04); Capture(null); }
            Capture("actual-hit");
            Assert(Impacts(0) && declared.TargetSeats.Where(seat => !damage.Any(hit => hit.TargetSeat == seat)).All(seat => !Impacts(seat)),
                "The current committed group hit was missing, or a successfully defended recipient was marked as hit.");
            var meter = Find<HealthMeter>(root).Single(meter => BattleFeedbackLayer.GetHealthAnchor(meter) == 0);
            Assert(meter.Value == humanDamage.RemainingHp && vm.RecentPlays.Count(play => play.Kind == incoming && play.SourceSeat == 1) == 1,
                "A real group result lost the actual health value or duplicated the declared group card.");
            Frames(12);
            Advance(2); Capture("expired");
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning &&
                game.AcceptedCommands.Count == commandsAfter && Enumerable.Range(0, 4)
                    .All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == views[seat]),
                "Drawing the native group sequence changed committed state or retained old target effects.");
            Assert(!layer.IsHitTestVisible && !layer.Focusable,
                "Multi-target feedback intercepted the next live table interaction.");
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null); Capture("reloaded");
            Assert(!vm.HasSaveError && layer.ActiveEffectCount == 0 && Enumerable.Range(0, 4)
                .All(seat => SnapshotJson.Serialize(Engine(vm).CreateSnapshot(seat)) == views[seat]),
                "Saving and loading the native group result restarted historical effects or altered a player's view.");
            Console.WriteLine($"Native group feedback: {incoming}, seed 31, physical card {declared.CardId}, target order 2 -> 3 -> 0, commands {accepted}->{commandsAfter}; actual responses {string.Join(", ", responses.Select(response => $"{response.ResponderSeat}:{(response.UsedResponse ? "paid" : "declined")}"))}; actual hits {string.Join(", ", damage.Select(hit => $"{hit.TargetSeat}:{hpBefore[hit.TargetSeat]}->{hit.RemainingHp}"))}; four views unchanged.");
            if (encoder is not null)
            {
                var path = Path.Combine(output, "battle-group-native-arrow.gif");
                using (var file = File.Create(path)) encoder.Save(file);
                BattleEffectsChecks.SetGifTiming(path, 4);
                Console.WriteLine($"Native group recording: {encoder.Frames.Count} frames, 728x481, 40 ms; actual response and hit sequence.");
            }
        }
        finally { window.Content = null; window.Close(); }

        void Advance(double seconds)
        {
            if (encoder is null) { clock.Advance(seconds); layer.InvalidateVisual(); return; }
            // Recording samples every actual 40 ms step, including the result
            // approach. Do not compress unrecorded waits into a single GIF frame.
            for (var index = 0; index < (int)Math.Ceiling(seconds / .04 - .000001); index++)
            {
                clock.Advance(.04); layer.InvalidateVisual();
                var bitmap = Capture(null); var metadata = new BitmapMetadata("gif");
                if (encoder.Frames.Count == 0)
                {
                    metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                }
                encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null));
            }
        }
        void VerifyResponse(int seat)
        {
            var facts = Engine(vm).Events.Skip(cursor).Select(item => item.Payload).ToArray();
            var response = facts.OfType<GroupResponseEvent>().Single(item => item.ResponderSeat == seat);
            var hits = facts.OfType<DamageAppliedEvent>().Where(item => item.TargetSeat == seat).ToArray();
            if (response.UsedResponse)
                Assert(response.ResponseCardId is > 0 && facts.OfType<CardRespondedEvent>().Any(card =>
                    card.ResponderSeat == seat && card.CardId == response.ResponseCardId && card.EffectiveCardKind == response.RequiredCardKind) && hits.Length == 0,
                    "A successful native group response must pay its actual physical card and prevent that target's damage.");
            else
                Assert(response.ResponseCardId is null && hits.Length == 1 && hits[0] is { SourceSeat: 1, Amount: 1 } &&
                    hits[0].RemainingHp == hpBefore[seat] - 1,
                    "A declined native group response must produce exactly its actual public HP loss.");
            Console.WriteLine($"  Native {incoming} target {seat}: response={(response.UsedResponse ? "paid" : "declined")}, card={response.ResponseCardId}, HP={hpBefore[seat]}->{Engine(vm).CreateSnapshot(0).Players.Single(player => player.Seat == seat).Hp}.");
        }
        void CheckCurrentTarget(int seat, int[] future)
        {
            Assert(Engine(vm).CreateSnapshot(seat).PendingDecision is { } prompt && prompt.PlayerSeat == seat && prompt.IncomingCard == incoming,
                "The expected group focus must come from the current real pending response, not an inferred future target.");
            var inquiry = vm.BattleCues.LastOrDefault(cue => cue.IsInquiry && cue.Revision == Engine(vm).Revision && cue.SourceSeat == seat);
            Assert(inquiry is not null && inquiry.CardKind == incoming && GameSoundRules.FromPublicCues([inquiry]).Count == 0,
                "A current typed group inquiry lost its public incoming kind or emitted a second card/result sound.");
            Assert(Auras(seat) && future.All(next => !Auras(next)),
                "Group response effects appeared simultaneously on future recipients instead of following the current public responder.");
        }
        bool Auras(int seat)
        {
            var center = Center(Anchor(seat));
            return Shapes(VisualTreeHelper.GetDrawing(layer)).Any(shape => shape.Pen is null &&
                shape.Brush is RadialGradientBrush glow && glow.GradientStops.Any(stop => stop.Color == Color.FromArgb(160, 53, 195, 255)) &&
                (Center(shape.Geometry.Bounds) - center).Length < .01);
        }
        bool Impacts(int seat) => Shapes(VisualTreeHelper.GetDrawing(layer)).Any(shape => shape.Pen is null &&
            shape.Brush is SolidColorBrush ink && ink.Color == Color.FromRgb(255, 91, 62) && Near(shape.Geometry.Bounds, Anchor(seat)));
        Rect Anchor(int seat)
        {
            var anchor = Find<FrameworkElement>(root).Single(element => BattleFeedbackLayer.GetSeatAnchor(element) == seat);
            return anchor.TransformToVisual(layer).TransformBounds(new Rect(new Point(), anchor.RenderSize));
        }
        RenderTargetBitmap Capture(string? name)
        {
            layer.InvalidateVisual(); root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            if (!record && name is not null)
            {
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"battle-group-native-{tag}-{name}.png")); png.Save(file);
            }
            return bitmap;
        }
        void Frames(int count)
        {
            if (encoder is null) return;
            Advance(count * .04);
        }
    }

    private static void WaitingTargetLifecycle(double scale)
    {
        var root = new Canvas { Width = 520, Height = 220, Background = Brushes.Black };
        var anchors = new Dictionary<int, Rect>();
        for (var seat = 0; seat < 3; seat++)
        {
            var border = new Border { Width = 100, Height = 100, Background = Brushes.DarkSlateGray };
            Canvas.SetLeft(border, 40 + seat * 155); Canvas.SetTop(border, 60);
            BattleFeedbackLayer.SetSeatAnchor(border, seat); root.Children.Add(border);
            anchors[seat] = new Rect(40 + seat * 155, 60, 100, 100);
        }
        var cues = new ObservableCollection<BattleCue>(); var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 520, Height = 220, AnchorRoot = root,
            Cues = cues, Clock = clock, PlaybackTimeScale = scale, CommittedRevision = 1 };
        root.Children.Add(layer);
        cues.Add(new(10, BattleCueKind.Card, 0, [1, 2], "万箭齐发", "公开使用者")
            { CardKind = CardKind.ArrowBarrage, Revision = 1 });
        cues.Add(new(11, BattleCueKind.ResponseWindow, 1, [0], "等待闪响应", "当前应答者")
            { CardKind = CardKind.ArrowBarrage, IsInquiry = true, Revision = 1 });
        Advance(); Assert(Focused().SequenceEqual([1]), "A public group inquiry failed to focus only its own current responder.");
        layer.CommittedRevision = 2;
        cues.Add(new(12, BattleCueKind.ResponseWindow, 2, [0], "等待杀响应", "下一应答者")
            { CardKind = CardKind.BarbarianAssault, IsInquiry = true, Revision = 2 });
        Advance(); Assert(Focused().SequenceEqual([2]),
            "A newer typed group inquiry borrowed an older declaration or retained its previous response focus.");
        layer.CommittedRevision = 3;
        cues.Add(new(13, BattleCueKind.ResponseWindow, 1, [0], "无懈可击询问中", "公开反制参与者")
            { IsInquiry = true, Revision = 3 });
        Advance(); Assert(!Focused().Any(),
            "A counterspell inquiry was mistaken for the pending group recipient and revived an older group aura.");
        clock.Advance(2 * scale); Capture();
        cues.Add(new(14, BattleCueKind.ResponseWindow, 2, [0], "等待杀响应", "公开迟到询问")
            { CardKind = CardKind.BarbarianAssault, IsInquiry = true, Revision = 3 });
        Advance(); Assert(Focused().SequenceEqual([2]),
            "A current public group inquiry disappeared merely because its declaration animation had already expired.");
        layer.MotionEnabled = false; layer.MotionEnabled = true; Capture();
        Assert(layer.ActiveEffectCount == 0 && !Focused().Any(), "Re-enabling motion replayed an already received group inquiry.");
        cues.Add(new(15, BattleCueKind.ResponseWindow, 1, [0], "等待闪响应", "下一询问")
            { CardKind = CardKind.ArrowBarrage, IsInquiry = true, Revision = 3 });
        cues.Clear(); Capture(); Assert(layer.ActiveEffectCount == 0 && !Focused().Any(),
            "Clearing a match left a group response focus in the visual layer.");
        cues.Add(new(16, BattleCueKind.ResponseWindow, 1, [0], "等待闪响应", "最后询问")
            { CardKind = CardKind.ArrowBarrage, IsInquiry = true, Revision = 3 });
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Capture();
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
            "Unloading the table retained a group inquiry or its frame timer.");

        void Advance() { clock.Advance(.08 * scale); Capture(); }
        void Capture()
        {
            layer.InvalidateVisual(); root.Measure(new Size(520, 220)); root.Arrange(new Rect(0, 0, 520, 220)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(520, 220, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        }
        int[] Focused() => anchors.Where(anchor => Shapes(VisualTreeHelper.GetDrawing(layer)).Any(shape =>
                shape.Pen is null && shape.Brush is RadialGradientBrush glow && glow.GradientStops.Any(stop =>
                    stop.Color == Color.FromArgb(160, 53, 195, 255)) &&
                (Center(shape.Geometry.Bounds) - Center(anchor.Value)).Length < .01))
            .Select(anchor => anchor.Key).ToArray();
    }

    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .01 && Math.Abs(first.Y - second.Y) < .01 &&
        Math.Abs(first.Width - second.Width) < .01 && Math.Abs(first.Height - second.Height) < .01;
    private static IEnumerable<GeometryDrawing> Shapes(Drawing? drawing)
    {
        if (drawing is GeometryDrawing shape) yield return shape;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nested in Shapes(child)) yield return nested;
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += (long)Math.Round(seconds * TimestampFrequency);
    }
}
