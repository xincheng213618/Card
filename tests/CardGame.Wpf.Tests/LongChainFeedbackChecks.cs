using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class LongChainFeedbackChecks
{
    // Eight living identity seats, four legal IronChains and one physical Slash.
    // No checkpoint mutation, private-hand inference, event injection or seed search.
    public static void NativeEightSeats(string output)
    {
        NativeEightSeats(output, record: false);
        if (RecordMotion) NativeEightSeats(output, record: true);
    }

    internal static (ContentRegistry Registry, GameEngine Game, CardSnapshot Slash) CreateFixture()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 2, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 8,
            ModeId = FixturePackage.Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Assert(game.Submit(new StartGameCommand()).Accepted, "The native eight-seat chain fixture did not start.");
        var setup = game.PendingDecision!;
        Assert(game.Submit(new SelectGeneralCommand(0, setup.Choices[0].ContentIds[0], game.Revision, setup.PromptId)).Accepted,
            "The native eight-seat fixture failed its actual general selection.");
        Assert(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "The native eight-seat fixture failed to enter play.");
        var publicHand = game.CreateSnapshot(0).Players[0].Hand;
        Console.WriteLine("Native eight-seat initial hand: " + string.Join(", ", publicHand.Select(card => $"{card.Id}:{card.Kind}")));
        Assert(publicHand.Count(card => card.Kind == CardKind.IronChain) >= 4 && publicHand.Any(card => card.Kind == CardKind.ThunderSlash),
            "The fixed native deck does not expose four actual chain cards and an actual elemental Slash.");
        for (var pair = 0; pair < 4; pair++)
        {
            var card = game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind == CardKind.IronChain);
            var action = game.GetHumanLegalActions().Single(candidate => candidate.Kind == LegalActionKind.IronChain &&
                candidate.CardId == card.Id && candidate.TargetSeats.SequenceEqual([pair * 2, pair * 2 + 1]));
            var decision = game.PendingDecision!;
            var result = game.Submit(new PlayCardCommand(0, card.Id, action.TargetSeats, game.Revision, decision.PromptId));
            Assert(result.Accepted, result.Error?.Message ?? "A native two-seat IronChain was rejected.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
                Assert(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "The actual IronChain failed to return to play.");
        }
        var before = game.CreateSnapshot(0);
        Assert(before.Players.Count == 8 && before.Players.All(player => player.IsAlive && player.IsChained),
            "The native identity fixture did not legally chain all eight living seats.");
        var allPlayers = game.CreateSnapshot(0, revealAll: true).Players;
        Assert(allPlayers.All(player => player.Role is not null), "The diagnostic identity view omitted a native role.");
        var roles = allPlayers.GroupBy(player => player.Role!.Value).ToDictionary(group => group.Key, group => group.Count());
        Assert(roles.GetValueOrDefault(Role.Lord) == 1 && roles.GetValueOrDefault(Role.Loyalist) == 2 &&
            roles.GetValueOrDefault(Role.Rebel) == 4 && roles.GetValueOrDefault(Role.Renegade) == 1,
            "The eight-seat feedback fixture changed the required native identity counts.");
        var slash = before.Players[0].Hand.First(card => card.Kind == CardKind.ThunderSlash);
        return (registry, game, slash);
    }

    private static void NativeEightSeats(string output, bool record)
    {
        var (registry, game, slash) = CreateFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = new MainViewModel(false, game.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        vm.LoadManualGameCommand.Execute(null);
        Assert(!vm.HasSaveError, vm.SaveStatus);
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false;
        var window = new MainWindow(vm); var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback"); var clock = new FrameClock(); layer.Clock = clock;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, "battle-long-chain-native-before.png"));
            var actual = Engine(vm); var cursor = actual.Events.Count; var accepted = actual.AcceptedCommands.Count;
            vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == slash.Id));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 1));
            Assert(vm.CanConfirmSelected, "The actual eight-seat Slash could not target its chained adjacent recipient.");
            Render(root, 1120, 740, Path.Combine(output, "battle-long-chain-native-selected.png"));
            vm.ConfirmSelectedCommand.Execute(null);
            var committed = actual.Events.Skip(cursor).ToArray();
            var damage = committed.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>().ToArray();
            var chains = committed.Select(envelope => envelope.Payload).OfType<ChainedDamagePropagatedEvent>().ToArray();
            Console.WriteLine($"Native eight-seat chain: seed={actual.Seed}, commands={accepted}->{actual.AcceptedCommands.Count}, damage={damage.Length}, relays={chains.Length}, cues={vm.BattleCues.Count}.");
            Assert(damage.Length == 8 && chains.Length == 7 && actual.AcceptedCommands.Count == accepted + 1,
                "One actual Slash did not commit all eight native chain recipients exactly once.");
            var projected = BattleCueProjector.Project(committed, actual.CreateSnapshot(0));
            Assert(projected.Any(cue => cue.Kind == BattleCueKind.Card && cue.PublicCardId == slash.Id) &&
                projected.Count(cue => cue.Kind == BattleCueKind.Damage) == 8 &&
                projected.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7,
                "The eight-seat committed chain lost its card, original hit or an early propagation at the presentation bound.");
            Assert(vm.BattleCues.Any(cue => cue.Kind == BattleCueKind.Card && cue.PublicCardId == slash.Id) &&
                vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.Damage) == 8 &&
                vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7 && layer.ActiveEffectCount <= 24,
                "The native VM or active layer clipped the current chain or escaped the existing active bound.");
            var views = Enumerable.Range(0, 8).Select(seat => SnapshotJson.Serialize(actual.CreateSnapshot(seat))).ToArray();
            if (record) RecordNative(root, layer, clock, output);
            else
            {
                clock.Advance(.46);
                for (var recipient = 0; recipient < damage.Length; recipient++)
                {
                    layer.InvalidateVisual();
                    Render(root, 1120, 740, Path.Combine(output, $"battle-long-chain-native-hit-{recipient + 1}.png"));
                    Assert(Impacts(layer, Anchor(root, layer, damage[recipient].TargetSeat)),
                        $"The native chained recipient {recipient + 1} lost its confirmed hit after arrival.");
                    if (recipient + 1 < damage.Length)
                        Assert(!Impacts(layer, Anchor(root, layer, damage[recipient + 1].TargetSeat)),
                            $"The native chained recipient {recipient + 2} struck before its incoming relay.");
                    clock.Advance(.36);
                }
            }
            clock.Advance(1.5); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "battle-long-chain-native-expired.png"));
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && actual.AcceptedCommands.Count == accepted + 1 &&
                Enumerable.Range(0, 8).All(seat => views[seat] == SnapshotJson.Serialize(actual.CreateSnapshot(seat))),
                "The completed eight-seat playback retained effects or changed a command/player-safe snapshot.");
            BulkBoundary(actual.CreateSnapshot(0));
            CausalBoundaries(actual.CreateSnapshot(0));
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void RecordNative(FrameworkElement root, BattleFeedbackLayer layer, FrameClock clock, string output)
    {
        var encoder = new GifBitmapEncoder();
        for (var frame = 0; frame < 110; frame++)
        {
            clock.Advance(.04); layer.InvalidateVisual();
            root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var metadata = new BitmapMetadata("gif");
            if (frame == 0)
            {
                metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
            }
            encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.65, .65)), null, metadata, null));
        }
        var path = Path.Combine(output, "battle-long-chain-native-thunder.gif");
        using (var file = File.Create(path)) encoder.Save(file);
        BattleEffectsChecks.SetGifTiming(path, 4);
        Assert(layer.ActiveEffectCount == 0, "The native eight-seat recording retained an uncompleted result.");
        Console.WriteLine("Native eight-seat recording: 110 frames, 728x481, 40 ms; one physical Thunder Slash, all seven public relay paths, final expiry.");
    }

    private static void BulkBoundary(GameSnapshot view)
    {
        long sequence = 0;
        EventEnvelope Wrap(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 900, "synthetic-bulk-presentation", payload);
        var events = new List<EventEnvelope>();
        long damageFrame = 1_000;
        void Hit(int target)
        {
            var id = ++damageFrame;
            events.Add(Wrap(new DamageRequestedEvent(id, 0, target, 1, CardKind.ThunderSlash, DamageNature.Thunder)));
            events.Add(Wrap(new DamageAppliedEvent(0, target, 1, 3, DamageNature.Thunder)));
            events.Add(Wrap(new AfterDamageEvent(id, 0, target, 1, 3, DamageNature.Thunder)));
        }
        // All share one revision, as a native RunToHuman operation can do.
        for (var round = 0; round < 40; round++)
        {
            var resolution = round + 1;
            events.Add(Wrap(new CardUseDeclaredEvent(resolution, round + 1, CardKind.ThunderSlash, 0)));
            events.Add(Wrap(new TargetsConfirmedEvent(resolution, [1])));
            Hit(1);
            for (var seat = 2; seat < 8; seat++)
            {
                events.Add(Wrap(new ChainedDamagePropagatedEvent(resolution, 0, seat - 1, seat, 1, DamageNature.Thunder)));
                Hit(seat);
            }
            events.Add(Wrap(new ChainedDamagePropagatedEvent(resolution, 0, 7, 0, 1, DamageNature.Thunder)));
            Hit(0);
        }
        var cues = BattleCueProjector.Project(events, view);
        Assert(cues.Count <= 24 && cues.Any(cue => cue.Kind == BattleCueKind.Card && cue.PublicCardId == 40) &&
            cues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7 && cues.Count(cue => cue.Kind == BattleCueKind.Damage) == 8,
            "The synthetic same-revision bulk stream lost its latest complete chain or retained old chain history.");
        for (var turn = 0; turn < 100; turn++) events.Add(Wrap(new TurnStartedEvent(turn + 1, 0)));
        cues = BattleCueProjector.Project(events, view);
        Assert(cues.Count == 12 && cues.All(cue => cue.Kind == BattleCueKind.Turn),
            "An old chain broadened the ordinary latest-action bulk bound.");
        Console.WriteLine("Synthetic bulk projection: 40 same-revision chains retain the current complete resolution; unrelated later history remains latest 12.");
    }

    private static void CausalBoundaries(GameSnapshot view)
    {
        SourceLessPendingChild();
        PreventedChild(2);
        PreventedChild(0);

        void SourceLessPendingChild()
        {
            long sequence = 0;
            EventEnvelope Wrap(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 901, "synthetic-source-less-chain", payload);
            var events = new List<EventEnvelope>(); var hits = new HashSet<long>();
            events.Add(Wrap(new DamageRequestedEvent(101, 0, 1, 1, CardKind.Lightning, DamageNature.Thunder) { SourceLess = true }));
            // A public nested occurrence has identical seats/nature but a source.
            events.Add(Wrap(new DamageRequestedEvent(201, 0, 1, 1, CardKind.ThunderSlash, DamageNature.Thunder)));
            events.Add(Wrap(new DamageAppliedEvent(0, 1, 1, 2, DamageNature.Thunder)));
            events.Add(Wrap(new AfterDamageEvent(201, 0, 1, 1, 2, DamageNature.Thunder)));
            var origin = Wrap(new DamageAppliedEvent(0, 1, 1, 3, DamageNature.Thunder) { SourceLess = true });
            events.Add(origin); hits.Add(origin.Sequence);
            for (var response = 0; response < 40; response++)
                events.Add(Wrap(new ArmorEffectAppliedEvent(700, CardKind.SilverLion, 0, 1, CardKind.ThunderSlash)));
            events.Add(Wrap(new AfterDamageEvent(101, 0, 1, 1, 3, DamageNature.Thunder) { SourceLess = true }));
            var from = 1;
            foreach (var target in Enumerable.Range(2, 6).Append(0))
            {
                events.Add(Wrap(new ChainedDamagePropagatedEvent(700, 0, from, target, 1, DamageNature.Thunder)));
                events.Add(Wrap(new DamageRequestedEvent(102 + target, 0, target, 1, CardKind.Lightning, DamageNature.Thunder) { SourceLess = true }));
                var hit = Wrap(new DamageAppliedEvent(0, target, 1, 3, DamageNature.Thunder) { SourceLess = true });
                events.Add(hit); hits.Add(hit.Sequence);
                // The final public HP result can still own an after-damage choice.
                if (target != 0) events.Add(Wrap(new AfterDamageEvent(102 + target, 0, target, 1, 3, DamageNature.Thunder) { SourceLess = true }));
                from = target;
            }
            var cues = BattleCueProjector.Project(events, view);
            Assert(cues.Count == 15 && cues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7 &&
                cues.Where(cue => cue.Kind == BattleCueKind.Damage).Select(cue => cue.Sequence).ToHashSet().SetEquals(hits) &&
                cues.Where(cue => cue.Kind == BattleCueKind.Damage).All(cue => cue.SourceSeat == -1),
                "A source-less chain lost its actual origin/pending public child or attached a nested sourced result.");
            Console.WriteLine("Synthetic source-less boundary: eight public hits/seven relays; nested sourced child excluded, last applied child retained before AfterDamage.");
        }

        void PreventedChild(int deniedTarget)
        {
            long sequence = 0;
            EventEnvelope Wrap(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 902, "synthetic-prevented-chain", payload);
            var events = new List<EventEnvelope>(); var actualHits = new HashSet<long>(); long unrelatedSequence = 0;
            events.Add(Wrap(new CardUseDeclaredEvent(800, 91, CardKind.ThunderSlash, 0)));
            events.Add(Wrap(new TargetsConfirmedEvent(800, [1])));
            Hit(1, 301, confirmed: true);
            var from = 1;
            foreach (var target in Enumerable.Range(2, 6).Append(0))
            {
                events.Add(Wrap(new ChainedDamagePropagatedEvent(800, 0, from, target, 1, DamageNature.Thunder)));
                Hit(target, 302 + target, confirmed: target != deniedTarget);
                if (target == deniedTarget && target != 0)
                {
                    // A later independent child has the same public seats/nature.
                    unrelatedSequence = UnrelatedHit(target);
                    for (var response = 0; response < 40; response++)
                        events.Add(Wrap(new ArmorEffectAppliedEvent(800, CardKind.SilverLion, 0, 1, CardKind.ThunderSlash)));
                }
                from = target;
            }
            events.Add(Wrap(new CardUseFinishedEvent(800, 91, CardKind.ThunderSlash)));
            if (deniedTarget == 0) unrelatedSequence = UnrelatedHit(0);
            var cues = BattleCueProjector.Project(events, view);
            var retainedHits = cues.Where(cue => cue.Kind == BattleCueKind.Damage).Select(cue => cue.Sequence).ToHashSet();
            Assert(cues.Count <= 24 && cues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7 &&
                actualHits.IsSubsetOf(retainedHits), "A prevented child broke the current bounded chain's confirmed public hits.");
            if (deniedTarget == 2)
                Assert(cues.Count == 15 && retainedHits.SetEquals(actualHits) && !cues.Any(cue => cue.Sequence == unrelatedSequence),
                    "An independent same-target result was retained as the prevented relay's confirmed recipient.");
            else
                Assert(retainedHits.Count == actualHits.Count + 1 && retainedHits.Contains(unrelatedSequence) &&
                    cues.Count(cue => cue.Kind == BattleCueKind.Damage && cue.TargetSeats.Contains(0)) == 1,
                    "A last prevented relay invented a hit or swallowed a later independent public hit after completion.");
            Console.WriteLine($"Synthetic prevented boundary: denied target={deniedTarget}, seven real chain hits; same-target independent result remains ordinary public feedback.");

            void Hit(int target, long frame, bool confirmed)
            {
                events.Add(Wrap(new DamageRequestedEvent(frame, 0, target, 1, CardKind.ThunderSlash, DamageNature.Thunder)));
                if (confirmed)
                {
                    var hit = Wrap(new DamageAppliedEvent(0, target, 1, 3, DamageNature.Thunder));
                    events.Add(hit); actualHits.Add(hit.Sequence);
                }
                events.Add(Wrap(new AfterDamageEvent(frame, 0, target, confirmed ? 1 : 0, confirmed ? 3 : 4, DamageNature.Thunder)));
            }
            long UnrelatedHit(int target)
            {
                events.Add(Wrap(new DamageRequestedEvent(900, 0, target, 1, CardKind.ThunderSlash, DamageNature.Thunder)));
                var hit = Wrap(new DamageAppliedEvent(0, target, 1, 2, DamageNature.Thunder)); events.Add(hit);
                events.Add(Wrap(new AfterDamageEvent(900, 0, target, 1, 2, DamageNature.Thunder)));
                return hit.Sequence;
            }
        }
    }

    private static Rect Anchor(FrameworkElement root, BattleFeedbackLayer layer, int seat)
    {
        var anchor = Find<FrameworkElement>(root).Single(element => BattleFeedbackLayer.GetSeatAnchor(element) == seat);
        return anchor.TransformToVisual(layer).TransformBounds(new Rect(new Point(), anchor.RenderSize));
    }
    private static bool Impacts(BattleFeedbackLayer layer, Rect seat) => Shapes(VisualTreeHelper.GetDrawing(layer)).Any(shape =>
        shape.Pen is null && shape.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(175, 151, 255) &&
        Math.Abs(shape.Geometry.Bounds.X - seat.X) < .01 && Math.Abs(shape.Geometry.Bounds.Y - seat.Y) < .01 &&
        Math.Abs(shape.Geometry.Bounds.Width - seat.Width) < .01 && Math.Abs(shape.Geometry.Bounds.Height - seat.Height) < .01);
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
    private sealed class FixturePackage : IGameContentPackage
    {
        public const string Mode = "identity:long-chain-feedback";
        public PackageManifest Manifest { get; } = new("long-chain-feedback", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var generals = Enumerable.Range(0, 8).Select(seat => $"fixture:long-chain-{seat}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new(id, "公开连环目标", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:long-chain-deck", "真实长连环实体牌", 8, 0, [])
            {
                // Verified seed 2 deals IDs 37,65,10,16,4,94,48,88 to Human0.
                // Retain physical Thunder37; make exactly the needed four chain costs 65,10,16,48.
                PhysicalCards = Enumerable.Range(0, 96).Select(index => new ContentDeckPhysicalCard(
                    index + 1 is 10 or 16 ? "standard:iron_chain" :
                    index % 3 == 0 ? "standard:thunder_slash" : "standard:iron_chain", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "八人身份长连环反馈", 8, 8,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1 },
                "fixture:long-chain-deck", GeneralCandidateCount: 8, GeneralPoolIds: generals));
        }
    }
}
