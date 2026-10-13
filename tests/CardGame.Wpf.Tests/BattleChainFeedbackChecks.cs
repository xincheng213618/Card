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

internal static class BattleChainFeedbackChecks
{
    public static void PublicChain(string output)
    {
        NativeChain(output, record: false);
        foreach (var nature in new[] { DamageNature.Fire, DamageNature.Thunder })
            foreach (var scale in new[] { 1d, 2d / 7 }) RelayBeats(nature, scale);
        RelayBeats(DamageNature.Thunder, 1, sourceLess: true);
        PreviewChains();
        if (RecordMotion) NativeChain(output, record: true);
    }

    private static void NativeChain(string output, bool record)
    {
        var fixture = ZhuqueFanScenario.FindHumanChainedSlash(knownSeed: 2);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, fixture.BeforeSlash));
        using var vm = new MainViewModel(false, fixture.Game.Seed, true, store, useExpandedContent: false,
            contentRegistry: fixture.Registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.HasSaveError, vm.SaveStatus);
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false;
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, "battle-chain-native-before.png"));
            var game = Engine(vm); var accepted = game.AcceptedCommands.Count; var cursor = game.Events.Count;
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == fixture.ThunderSlashAction.CardId));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == fixture.PrimaryTargetSeat));
            Assert(vm.CanConfirmSelected, "The native physical Thunder Slash must target the actual chained recipient.");
            vm.ConfirmSelectedCommand.Execute(null);
            for (var step = 0; !game.Events.Skip(cursor).Any(e => e.Payload is ChainedDamagePropagatedEvent); step++)
            {
                Assert(step < 8 && vm.CanStepAi, "The real Thunder Slash did not reach its native chain return.");
                vm.StepAiCommand.Execute(null);
            }
            var fact = game.Events.Skip(cursor).Select(e => e.Payload).OfType<ChainedDamagePropagatedEvent>().Single();
            Console.WriteLine($"Native chain: seed={game.Seed}, commands={accepted}->{game.AcceptedCommands.Count}, attacker={fact.SourceSeat}, path={fact.FromSeat}->{fact.TargetSeat}, nature={fact.Nature}.");
            var paths = vm.BattleCues.Where(cue => cue.Kind == BattleCueKind.ChainPropagation).ToArray();
            Assert(paths.Length == 1 && paths[0].SourceSeat == fact.SourceSeat && paths[0].OriginSeat == fact.FromSeat &&
                paths[0].TargetSeats.SequenceEqual([fact.TargetSeat]) && paths[0].Nature == fact.Nature && paths[0].PublicCard is null,
                "The real committed chain step has no exact public origin-to-recipient visual cue.");
            Assert(GameSoundRules.FromPublicCues(paths).Count == 0,
                "A propagation announcement must not create a second confirmed-hit sound.");
            var damage = game.Events.Skip(cursor).Select(e => e.Payload).OfType<DamageAppliedEvent>().ToArray();
            Assert(damage.Length == 2 && damage.All(e => e.Nature == DamageNature.Thunder) && game.AcceptedCommands.Count == accepted + 1,
                "The fixed native Slash did not commit exactly the two actual chain recipients once.");
            var views = Enumerable.Range(0, 5).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            if (record)
            {
                var encoder = new GifBitmapEncoder();
                for (var frame = 0; frame < 52; frame++)
                {
                    clock.Advance(.04); Prepare(root, layer);
                    var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    var metadata = new BitmapMetadata("gif");
                    if (frame == 0)
                    {
                        metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                        metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                    }
                    encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null));
                }
                var path = Path.Combine(output, "battle-chain-native-thunder.gif");
                using (var file = File.Create(path)) encoder.Save(file);
                BattleEffectsChecks.SetGifTiming(path, 4);
                Assert(layer.ActiveEffectCount == 0, "The recorded native relay never reached its final public state.");
                Console.WriteLine("Native chain recording: 52 frames, 728x481, 40 ms; one physical Thunder Slash, actual public 1 -> 2 relay.");
            }
            else
            {
                var origin = Anchor(root, layer, fact.FromSeat); var recipient = Anchor(root, layer, fact.TargetSeat);
                clock.Advance(.5); Capture("primary-hit");
                Assert(Impacts(layer, origin, DamageNature.Thunder) && !Impacts(layer, recipient, DamageNature.Thunder) && !Paths(layer).Any(),
                    "The native propagated recipient struck before the first confirmed hit and relay.");
                clock.Advance(.12); Capture("in-flight");
                var route = Paths(layer).Single(); var start = PathGeometry.CreateFromGeometry(route.Geometry).Figures[0].StartPoint;
                Assert((start - Center(origin)).Length < (start - Center(recipient)).Length && !Impacts(layer, recipient, DamageNature.Thunder),
                    "The native relay did not leave the public FromSeat, or confirmed the recipient before arrival.");
                clock.Advance(.2); Capture("recipient-hit");
                Assert(Impacts(layer, recipient, DamageNature.Thunder), "The actual recipient's committed hit failed to follow the arriving relay.");
                foreach (var factDamage in damage)
                {
                    var meter = Find<HealthMeter>(root).Single(meter => BattleFeedbackLayer.GetHealthAnchor(meter) == factDamage.TargetSeat);
                    Assert(meter.Value == factDamage.RemainingHp, "Relay timing delayed the authoritative public HP update.");
                }
                clock.Advance(1.4); Capture("expired");
                Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "A native chain effect accumulated after completion.");
            }
            Assert(game.AcceptedCommands.Count == accepted + 1 && Enumerable.Range(0, 5)
                .All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Rendering the public chain changed accepted commands or any of five player-safe views.");
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null);
            Assert(!vm.HasSaveError && layer.ActiveEffectCount == 0, "Cold loading replayed an earlier chain path.");
            Console.WriteLine("Native chain: relay follows origin hit and precedes recipient hit; HP current, five views unchanged, expiry/load boundaries passed.");

            void Capture(string stage)
            {
                layer.InvalidateVisual(); Render(root, 1120, 740, Path.Combine(output, $"battle-chain-native-{stage}.png"));
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void RelayBeats(DamageNature nature, double scale, bool sourceLess = false)
    {
        var root = new Canvas { Width = 880, Height = 470, Background = Brushes.Black };
        var seats = new[] { new Rect(40, 310, 100, 130), new Rect(730, 50, 100, 130),
            new Rect(430, 40, 100, 130), new Rect(80, 50, 100, 130) };
        var anchors = new List<Border>();
        for (var seat = 0; seat < seats.Length; seat++)
        {
            var anchor = new Border { Width = 100, Height = 130, Background = Brushes.DimGray };
            Canvas.SetLeft(anchor, seats[seat].X); Canvas.SetTop(anchor, seats[seat].Y);
            BattleFeedbackLayer.SetSeatAnchor(anchor, seat); root.Children.Add(anchor);
            anchors.Add(anchor);
        }
        var cues = new ObservableCollection<BattleCue>(); var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 880, Height = 470, AnchorRoot = root, Cues = cues, Clock = clock, PlaybackTimeScale = scale };
        root.Children.Add(layer);
        if (!sourceLess)
            cues.Add(new(1, BattleCueKind.Card, 0, [1], "属性杀", "公开攻击者")
                { CardKind = nature == DamageNature.Fire ? CardKind.FireSlash : CardKind.ThunderSlash });
        var attacker = sourceLess ? -1 : 0;
        cues.Add(new(2, BattleCueKind.Damage, attacker, [1], "−1", "首个目标", nature));
        cues.Add(new(3, BattleCueKind.ChainPropagation, 0, [2], "连环传导", "首个目标", nature) { OriginSeat = 1 });
        cues.Add(new(4, BattleCueKind.Damage, attacker, [2], "−1", "第二目标", nature));
        cues.Add(new(5, BattleCueKind.ChainPropagation, 0, [3], "连环传导", "第二目标", nature) { OriginSeat = 2 });
        cues.Add(new(6, BattleCueKind.Damage, attacker, [3], "−1", "第三目标", nature));
        Tick(sourceLess ? .08 : .5);
        Assert(Impacts(layer, seats[1], nature) && !Paths(layer).Any() && !Impacts(layer, seats[2], nature), "The first relay preceded its origin hit.");
        Tick(.12);
        var first = Paths(layer).Single().Geometry.Bounds;
        Assert(!Impacts(layer, seats[2], nature), "The second target struck during relay travel.");
        anchors[1].Visibility = Visibility.Collapsed; Prepare(root, layer, 880, 470);
        Assert(!Paths(layer).Any(), "A relay invented an origin for a hidden public anchor.");
        anchors[1].Visibility = Visibility.Visible; anchors[2].Visibility = Visibility.Collapsed; Prepare(root, layer, 880, 470);
        Assert(!Paths(layer).Any(), "A relay invented a destination for a hidden public anchor.");
        anchors[2].Visibility = Visibility.Visible; Prepare(root, layer, 880, 470);
        layer.PlaybackTimeScale = 2; Prepare(root, layer, 880, 470);
        Assert(Near(first, Paths(layer).Single().Geometry.Bounds), "Changing pace jumped or restarted a moving relay.");
        Tick(.2);
        Assert(Impacts(layer, seats[2], nature) && !Impacts(layer, seats[3], nature), "Relay recipients lost public order.");
        Tick(.21);
        Assert(Paths(layer).Count() == 2 && !Impacts(layer, seats[3], nature), "The second relay was omitted or its recipient struck early.");
        Tick(.15);
        Assert(Impacts(layer, seats[3], nature), "The third target did not receive its confirmed hit after relay arrival.");
        Tick(1.6);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "A paced multi-hop relay did not expire.");
        layer.MotionEnabled = false; layer.MotionEnabled = true; Prepare(root, layer, 880, 470);
        Assert(layer.ActiveEffectCount == 0, "Restoring motion replayed public chain history.");
        cues.Clear();
        cues.Add(new(20, BattleCueKind.Damage, 0, [1], "−1", "普通伤害"));
        cues.Add(new(21, BattleCueKind.Damage, 0, [2], "−1", "普通伤害"));
        Tick(.04);
        Assert(!Paths(layer).Any() && Impacts(layer, seats[1], DamageNature.Normal) && Impacts(layer, seats[2], DamageNature.Normal),
            "Separate ordinary hits invented a chain or acquired an unrelated relay delay.");
        cues.Clear();
        cues.Add(new(30, BattleCueKind.Damage, 0, [1], "−1", "已确认伤害", nature));
        cues.Add(new(31, BattleCueKind.ChainPropagation, 0, [2], "连环传导", "来源", nature) { OriginSeat = 1 });
        cues.Add(new(32, BattleCueKind.Damage, 0, [2], "−1", "后续伤害", nature));
        layer.MotionEnabled = false; layer.MotionEnabled = true; Prepare(root, layer, 880, 470);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "Motion off retained a queued relay or recipient result.");
        cues.Clear();
        cues.Add(new(40, BattleCueKind.ChainPropagation, 0, [2], "连环传导", "来源", nature) { OriginSeat = 1 });
        Tick(.27);
        Assert(Paths(layer).Count() == 1 && !Impacts(layer, seats[2], nature), "A propagation announcement invented an uncommitted recipient hit.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "Unloading retained chain/impact playback.");
        Console.WriteLine($"Public relay beats: {nature}, initial scale={scale}, source-less={sourceLess}; three recipients in order, pace continuity, public-anchor boundaries, ordinary-hit independence and lifecycle passed.");

        void Tick(double seconds) { clock.Advance(seconds * layer.PlaybackTimeScale); Prepare(root, layer, 880, 470); }
    }

    private static void PreviewChains()
    {
        using var preview = new BattleEffectsPreview(); var clock = new FrameClock(); preview.FeedbackLayer.Clock = clock;
        foreach (var (name, kind) in new[] { ("火焰连环", BattleEffectPreviewKind.FireChain), ("雷电连环", BattleEffectPreviewKind.ThunderChain) })
        {
            Find<Button>(preview).Single(button => Equals(button.Content, name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            clock.Advance(.65); Prepare(preview.Scene, preview.FeedbackLayer, 880, 470);
            Assert(preview.SelectedEffect == kind && Paths(preview.FeedbackLayer).Count() == 1 &&
                Find<HealthMeter>(preview.Scene).Where(meter => BattleFeedbackLayer.GetHealthAnchor(meter) is 1 or 2).All(meter => meter.Value == 3),
                "The preview's native chain button failed to present the same relay layer and current public HP.");
        }
        Console.WriteLine("Elemental chain preview: both native replay buttons present origin-to-recipient paths with current HP.");
    }

    private static void Prepare(FrameworkElement root, BattleFeedbackLayer layer, int width = 1120, int height = 740)
    {
        layer.InvalidateVisual(); root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
    }
    private static Rect Anchor(FrameworkElement root, BattleFeedbackLayer layer, int seat)
    {
        var anchor = Find<FrameworkElement>(root).Single(element => BattleFeedbackLayer.GetSeatAnchor(element) == seat);
        return anchor.TransformToVisual(layer).TransformBounds(new Rect(new Point(), anchor.RenderSize));
    }
    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .01 && Math.Abs(first.Y - second.Y) < .01 &&
        Math.Abs(first.Width - second.Width) < .01 && Math.Abs(first.Height - second.Height) < .01;
    private static bool Impacts(BattleFeedbackLayer layer, Rect seat, DamageNature nature) => Shapes(VisualTreeHelper.GetDrawing(layer))
        .Any(shape => shape.Pen is null && shape.Brush is SolidColorBrush ink &&
            ink.Color == (nature == DamageNature.Thunder ? Color.FromRgb(175, 151, 255) : Color.FromRgb(255, 91, 62)) && Near(shape.Geometry.Bounds, seat));
    private static IEnumerable<GeometryDrawing> Paths(BattleFeedbackLayer layer) => Shapes(VisualTreeHelper.GetDrawing(layer))
        .Where(shape => shape.Pen?.Thickness == 3.4);
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
