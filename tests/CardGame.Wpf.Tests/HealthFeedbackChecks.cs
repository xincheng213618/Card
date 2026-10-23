using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using DrawingGeometry = System.Windows.Media.Geometry;
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
        meter.LayoutTransform = Transform.Identity;
        meter.Value = 3; meter.Maximum = 5;
        var lossSeat = new Border { Width = 120, Height = 110 };
        Canvas.SetLeft(lossSeat, 150); Canvas.SetTop(lossSeat, 5);
        BattleFeedbackLayer.SetSeatAnchor(lossSeat, 0);
        root.Children.Insert(root.Children.Count - 1, lossSeat);
        cues.Add(new(4, BattleCueKind.HpLoss, -1, [0], "−2", "测试", Detail: "失去体力")
            { HpDelta = -2, RemainingHp = 3, MaximumHp = 5 });
        clock.Advance(.08);
        var hpLoss = Capture(root, layer);
        Save(hpLoss, Path.Combine(output, "health-track-hp-loss.png"));
        Assert(meter.Value == 3 && Geometry(VisualTreeHelper.GetDrawing(layer)).Any(d =>
                d.Geometry is RectangleGeometry && d.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(225, 183, 128)) &&
            string.Concat(GlyphText(VisualTreeHelper.GetDrawing(layer))).Contains("失去体力", StringComparison.Ordinal) && RedRemainder(hpLoss) == 0,
            "HP loss must immediately show its distinct caption and warm health tail without a red damage remainder.");
        meter.Value = 4;
        cues.Add(new(5, BattleCueKind.Recovery, 0, [0], "+1", "测试")
            { HpDelta = 1, RemainingHp = 4, MaximumHp = 5 });
        clock.Advance(.08);
        var afterLossRecovery = Capture(root, layer);
        Assert(!Geometry(VisualTreeHelper.GetDrawing(layer)).Any(d => d.Geometry is RectangleGeometry &&
                d.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(225, 183, 128)) && Pixel(afterLossRecovery, 250, 145).G > 210,
            "A rapid actual recovery must replace the old HP-loss tail with the current gained segment.");
        cues.Clear();
        HpLossScheduling(root, layer, cues, clock, meter);
        Console.WriteLine("Health tracks: immediate HP, damage retreat, rapid recovery, maximum changes, critical/dying boundaries and motion off passed.");
        NativeRecovery(output);
        NativeSilverLionReplacement(output);
        NativeLihuoHpLoss(output);
    }

    private static void HpLossScheduling(Canvas root, BattleFeedbackLayer layer, ObservableCollection<BattleCue> cues,
        FrameClock clock, HealthMeter meter)
    {
        const long useId = 700;
        var sequence = 100L;
        BattleCue Damage(long revision) => new(++sequence, BattleCueKind.Damage, 1, [1], "−1", "目标")
            { Revision = revision, SourceCardUseResolutionId = useId };
        BattleCue Loss(long? completedUse, long revision) => new(++sequence, BattleCueKind.HpLoss, -1, [0], "−2", "测试", Detail: "失去体力")
            { Revision = revision, CompletedCardUseResolutionId = completedUse, HpDelta = -2, RemainingHp = 3, MaximumHp = 5 };
        void ExpectLoss(bool expected, string boundary)
        {
            Capture(root, layer);
            var drawing = VisualTreeHelper.GetDrawing(layer);
            var caption = string.Concat(GlyphText(drawing)).Contains("失去体力", StringComparison.Ordinal);
            var tail = Geometry(drawing).Any(d => d.Geometry is RectangleGeometry &&
                d.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(225, 183, 128));
            Assert(caption == expected && tail == expected && meter.Value == 3,
                $"{boundary}: HP-loss caption/tail must follow only the exact completed use, while public HP is immediate; caption={caption}, tail={tail}, HP={meter.Value}.");
        }

        meter.Value = 3; meter.Maximum = 5;
        foreach (var completedUse in new long?[] { null, useId + 1 })
        {
            cues.Clear(); layer.CommittedRevision = 30;
            cues.Add(Damage(30)); cues.Add(Loss(completedUse, 30));
            clock.Advance(.08);
            ExpectLoss(true, completedUse is null ? "Unlinked loss during another hit" : "Wrong-use loss during another hit");
        }

        cues.Clear(); layer.CommittedRevision = 31;
        cues.Add(Damage(31));
        clock.Advance(.18);
        layer.CommittedRevision = 32;
        cues.Add(Damage(32));
        layer.CommittedRevision = 33;
        cues.Add(Loss(useId, 33));
        clock.Advance(.17);
        ExpectLoss(false, "350ms: first hit finished, later same-use hit still active across revisions");
        clock.Advance(.15);
        ExpectLoss(true, "500ms: every same-use hit has finished");

        cues.Clear(); layer.CommittedRevision = 34;
        cues.Add(Damage(34));
        clock.Advance(.36);
        cues.Add(Loss(useId, 34));
        clock.Advance(.08);
        ExpectLoss(true, "Matching use whose hit already finished");
        cues.Clear();
        Console.WriteLine("HP-loss schedule: unlinked/wrong-use immediate; cross-revision same-use last hit gates both caption and tail; settled use immediate.");
    }

    private static void NativeLihuoHpLoss(string output)
    {
        foreach (var playerCount in new[] { 4, 8 })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new LihuoFixture(playerCount));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 1, PlayerCount = playerCount, HumanSeat = 0, HumanRole = Role.Lord, ModeId = LihuoFixture.Mode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game.Submit(new StartGameCommand()));
            Accept(game.Submit(new SelectGeneralCommand(0, "classic:cheng-pu", game.Revision, game.PendingDecision!.PromptId)));
            for (var step = 0; step < playerCount + 8 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The fixed native Cheng Pu fixture must reach its first Play boundary without searching seeds.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            foreach (var width in playerCount == 4 ? new[] { 1120, 1920 } : new[] { 1120 })
                NativeLihuoHpLossAtWidth(output, registry, store, width, playerCount);
        }
        static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "Native Lihuo setup failed.");
    }

    private static void NativeLihuoHpLossAtWidth(string output, ContentRegistry registry, MemorySaveStore store, int width, int playerCount)
    {
        using var model = new MainViewModel(autoAdvance: false, seed: 1, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry)
        { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Assert(!model.HasSaveError && model.BattleCues.Count == 0, model.SaveStatus);
        var game = Engine(model);
        Assert(game.PlayerCount == playerCount && model.Seats.Count == playerCount,
            "The native Lihuo fixture must restore its actual player count, rather than adding synthetic seat anchors.");
        var artifactSuffix = playerCount == 4 ? string.Empty : $"-{playerCount}-seats";
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        host.Capture(output, "health-native-lihuo-before" + artifactSuffix, width);
        var height = width == 1120 ? 740 : 1080;
        var hand = Find<HandPanel>(host.Root).Single(panel => panel.IsVisible);
        hand.Clock = host.Clock; hand.InvalidateArrange(); host.Root.UpdateLayout();
        ((GameAudioController)typeof(MainWindow).GetField("_audio", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host.Window)!).Dispose();
        model.IsSoundEnabled = true; model.SoundVolume = .4;
        var audioOutput = new BattleAudioBeatChecks.RecordingOutput(host.Clock, "native-lihuo-hp-loss");
        using var audio = new GameAudioController(model, () => audioOutput, host.Clock, host.Layer);
        audio.SetActive(true);
        Assert(audioOutput.PrepareCount > 0 && audioOutput.Events.Count == 0,
            "Native HP-loss audio must begin without replaying restored history.");

        var action = game.GetHumanLegalActions().First(action => action.ConversionSource?.SkillId == "classic:lihuo" &&
            action.PlayedCardKind == CardKind.FireSlash && action.TargetSeats.Count == 1);
        var target = action.TargetSeats.Single();
        var before = game.CreateSnapshot(0);
        var eventCursor = game.Events.Count;
        var commandCursor = game.AcceptedCommands.Count;
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:lihuo"));
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == action.CardId));
        if (!model.Seats.Single(seat => seat.Seat == target).IsSelectedTarget)
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == target));
        Assert(model.CanConfirmSelected && game.AcceptedCommands.Count == commandCursor,
            "The actual Lihuo material and target draft must wait for its native confirmation.");
        Render(host.Root, width, height, null);
        var sourceSurface = Find<CardLiftSurface>(hand).Single(surface => BattleFeedbackLayer.GetHandAnchor(surface) == action.CardId);
        var handOrigin = sourceSurface.TransformToVisual(host.Layer).TransformBounds(new Rect(sourceSurface.RenderSize));
        model.ConfirmSelectedCommand.Execute(null);
        Assert(game.AcceptedCommands.Count == commandCursor + 1 && game.AcceptedCommands.Last() is
                PlayCardCommand { CardId: var played, PlayedCardKind: CardKind.FireSlash, ConversionSource.SkillId: "classic:lihuo" } && played == action.CardId,
            "The native confirmation must commit the exact physical Slash with its actual Lihuo conversion.");
        for (var step = 0; step < 24 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            Assert(model.CanStepAi, "The fixed no-Dodge target must resolve without another human response.");
            model.StepAiCommand.Execute(null);
        }
        Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The actual Fire Slash and mandatory Lihuo penalty must complete before the native Play boundary resumes.");
        var events = game.Events.Skip(eventCursor).ToArray();
        var lost = events.Single(envelope => envelope.Payload is ProgramSkillHpLostEvent { SkillId: "classic:lihuo" });
        var fact = (ProgramSkillHpLostEvent)lost.Payload;
        var damage = events.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>().ToArray();
        var use = events.Select(envelope => envelope.Payload).OfType<CardUseDeclaredEvent>()
            .Single(declared => declared.CardId == action.CardId && declared.SourceSeat == 0);
        var after = game.CreateSnapshot(0);
        Assert(fact is { Amount: 1, TargetSeat: 0 } && fact.RemainingHp == before.Players[0].Hp - 1 &&
            after.Players[0].Hp == fact.RemainingHp && damage is [{ SourceSeat: 0, Amount: 1, Nature: DamageNature.Fire }] &&
            damage[0].TargetSeat == target && after.Players[target].Hp == before.Players[target].Hp - 1 &&
            fact.CompletedCardUseResolutionId == use.ResolutionId && damage[0].SourceCardUseResolutionId == use.ResolutionId,
            "Lihuo must inflict one actual fire damage on its target and one separate, non-damage HP loss on Cheng Pu.");
        var cue = model.BattleCues.Single(cue => cue.Sequence == lost.Sequence);
        Assert(cue is { Kind: BattleCueKind.HpLoss, SourceSeat: -1, TargetSeats: [0], Label: "−1", Detail: "失去体力",
                HpDelta: -1, SkillId: null, CardKind: null, PublicCardId: null } &&
            cue.RemainingHp == fact.RemainingHp && cue.MaximumHp == after.Players[0].MaxHp &&
            cue.CompletedCardUseResolutionId == fact.CompletedCardUseResolutionId &&
            cue.ActorName == after.Players[0].GeneralName && !model.BattleCues.Any(cue =>
                cue.Sequence >= events[0].Sequence && cue.Kind == BattleCueKind.Damage && cue.TargetSeats.Contains(0)),
            "The native Lihuo penalty must project its actual HP result without inventing an attacker, physical face or damage cue.");
        var commands = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var playCue = model.BattleCues.Single(item => item.Kind == BattleCueKind.Card && item.PublicCardId == action.CardId && !item.IsUseCompletion);
        var handoffCount = 0;
        var previousPending = true;
        var flightSamples = new List<string>();
        var movie = RecordMotion && playerCount == 4 && width == 1120 ? new GifBitmapEncoder() : null;
        var movieTime = 0;
        MovieFrame();
        SampleFlight(0);
        AdvanceTo(1); SampleFlight(1);
        for (var milliseconds = 20; milliseconds <= 360; milliseconds += 20)
        {
            AdvanceTo(milliseconds);
            SampleFlight(milliseconds);
        }
        Assert(handoffCount == 1, "The native public face must hand off exactly once, without disappearing then relaunching.");
        Console.WriteLine($"Native Lihuo flight {playerCount} seats {width}x{height}: " + string.Join("; ", flightSamples));
        AdvanceTo(500); host.Layer.InvalidateVisual();
        host.Capture(output, "health-native-lihuo-loss-500ms" + artifactSuffix, width);
        var hitDrawing = VisualTreeHelper.GetDrawing(host.Layer);
        var hitText = string.Concat(GlyphText(hitDrawing));
        Assert(hitText.Contains("火焰伤害", StringComparison.Ordinal) && !hitText.Contains("失去体力", StringComparison.Ordinal) &&
            !Geometry(hitDrawing).Any(d => d.Geometry is RectangleGeometry && d.Brush is SolidColorBrush brush &&
                brush.Color == Color.FromRgb(225, 183, 128)),
            "At 500ms the actual target's fire hit must be visible before the later Lihuo loss caption or health tail.");
        AssertCommittedState("500ms target hit");
        Assert(audioOutput.Count(GameSound.Fire) == 1 && audioOutput.Count(GameSound.Hit) == 0 &&
            audioOutput.Count(GameSound.Recover) == 0 && GameSoundRules.FromPublicCues([cue]).Count == 0,
            "Only the actual target fire damage may sound; Lihuo HP loss must add no hit, fire or healing sound.");
        AdvanceTo(800); host.Layer.InvalidateVisual();
        host.Capture(output, "health-native-lihuo-loss-800ms" + artifactSuffix, width);
        Assert(string.Concat(GlyphText(VisualTreeHelper.GetDrawing(host.Layer))).Contains("失去体力", StringComparison.Ordinal),
            "At 800ms Cheng Pu's actual HP loss must appear after the completed use's target impact.");
        AssertCommittedState("800ms subsequent HP loss");
        AdvanceTo(2000); host.Layer.InvalidateVisual();
        host.Capture(output, "health-native-lihuo-loss-2000ms" + artifactSuffix, width);
        Assert(!string.Concat(GlyphText(VisualTreeHelper.GetDrawing(host.Layer))).Contains("失去体力", StringComparison.Ordinal),
            "At 2000ms the scheduled HP-loss caption must have retired without extending its own presentation lifetime.");
        AssertCommittedState("2000ms settled");
        if (movie is not null)
        {
            var path = Path.Combine(output, "health-native-lihuo-loss.gif");
            using (var stream = File.Create(path)) movie.Save(stream);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var input = File.OpenRead(path);
            var decoded = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var durations = decoded.Frames.Select(frame => Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay"))).ToArray();
            Assert(decoded.Frames.Count == 51 && durations.All(delay => delay == 4) && durations.Sum() * 10 == 2040 &&
                decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481),
                "The native Lihuo movie must preserve 0..2000ms samples every 40ms, including a final 40ms hold, without inserting the 500ms assertion frame.");
            Assert(!Pixels(decoded.Frames[0]).AsSpan().SequenceEqual(Pixels(decoded.Frames[12])) &&
                !Pixels(decoded.Frames[12]).AsSpan().SequenceEqual(Pixels(decoded.Frames[20])) &&
                !Pixels(decoded.Frames[20]).AsSpan().SequenceEqual(Pixels(decoded.Frames[50])),
                "The native Lihuo movie must contain distinct launch, target hit, subsequent loss and settled frames rather than repeating one capture.");
            Console.WriteLine("Native Lihuo movie: 51x40ms (2040ms encoded), samples 0..2000ms, 728x481; hit/loss/retirement assertions at 500/800/2000ms.");
        }
        Assert(game.AcceptedCommands.Count == commands && Enumerable.Range(0, game.PlayerCount).All(seat =>
                views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
            "HP-loss presentation must not delay or mutate any accepted command or player-safe state.");
        Console.WriteLine($"Native Lihuo seed1 with {playerCount} seats at {width}x{height}: physical Slash {action.CardId}->FireSlash target {target}; " +
            $"owner HP {before.Players[0].Hp}->{after.Players[0].Hp}; actual loss/cue=1/1; use={use.ResolutionId}; " +
            "500ms fire hit/no loss; 800ms loss; 2000ms retired; command remains available; " +
            $"audio fire/hit/recover={audioOutput.Count(GameSound.Fire)}/{audioOutput.Count(GameSound.Hit)}/{audioOutput.Count(GameSound.Recover)}.");
        void SampleFlight(int milliseconds)
        {
            host.Layer.InvalidateVisual();
            Render(host.Root, width, height, null);
            if (milliseconds == 1)
            {
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var active = (System.Collections.IEnumerable)typeof(BattleFeedbackLayer).GetField("_active", fields)!.GetValue(host.Layer)!;
                var timed = active.Cast<object>().Single(item =>
                    ((BattleCue)item.GetType().GetProperty("Cue")!.GetValue(item)!).Sequence == playCue.Sequence);
                var type = timed.GetType();
                var origin = (Rect)type.GetProperty("HandOrigin")!.GetValue(timed)!;
                var destination = (Point)type.GetField("_flightDestination", fields)!.GetValue(timed)!;
                var size = (Size)type.GetField("_flightSize", fields)!.GetValue(timed)!;
                var routeViewport = (Rect)type.GetField("_flightViewport", fields)!.GetValue(timed)!;
                var routeObstacles = (Rect[])type.GetField("_flightObstacles", fields)!.GetValue(timed)!;
                var path = type.GetField("_flightPath", fields)!.GetValue(timed)!;
                Assert(!(bool)path.GetType().GetProperty("NeedsClip")!.GetValue(path)!,
                    $"Native {playerCount} seats at {width}px must find a complete visible route throughout flight, without fallback clipping between sampled frames.");
                File.WriteAllText(Path.Combine(output, $"health-native-lihuo-route-{width}{artifactSuffix}.json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        PlayerCount = playerCount, Width = width, Height = height, Milliseconds = milliseconds,
                        HandOrigin = new { origin.X, origin.Y, origin.Width, origin.Height },
                        FlightDestination = new { destination.X, destination.Y },
                        FlightSize = new { size.Width, size.Height },
                        FlightViewport = new { routeViewport.X, routeViewport.Y, routeViewport.Width, routeViewport.Height },
                        FlightObstacles = routeObstacles.Select(rect => new { rect.X, rect.Y, rect.Width, rect.Height }).ToArray(),
                        Detoured = (bool)path.GetType().GetProperty("Detoured")!.GetValue(path)!,
                        NeedsClip = (bool)path.GetType().GetProperty("NeedsClip")!.GetValue(path)!
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            if (milliseconds == 80) host.Capture(output, "health-native-lihuo-flight-80ms" + artifactSuffix, width);
            var row = Find<Border>(host.Root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == playCue.Sequence);
            var pending = BattleFeedbackLayer.GetIsFlightPending(row);
            var rowOpacity = ((FrameworkElement)row.Parent).Opacity;
            var landing = row.TransformToVisual(host.Layer).TransformBounds(new Rect(row.RenderSize));
            var flights = CardFlights(VisualTreeHelper.GetDrawing(host.Layer), CardArt.Get(CardKind.FireSlash)!,
                Matrix.Identity, 1, null).ToArray();
            var inFlight = milliseconds is > 0 and <= 320;
            Assert(flights.Length == (inFlight ? 1 : 0) && pending == (milliseconds <= 320) &&
                (pending ? rowOpacity < .001 : rowOpacity > .99),
                $"Native {width}px at {milliseconds}ms: the complete flight and public row must have one owner; flying={flights.Length}, pending={pending}, row-opacity={rowOpacity}.");
            if (previousPending && !pending) handoffCount++;
            Assert(previousPending || !pending, "A revealed native public face restarted its flight.");
            previousPending = pending;
            Assert(model.CanEndTurn && model.EndTurnCommand.CanExecute(null) && game.AcceptedCommands.Count == commands,
                "Avoiding the command area must not suspend or resubmit an already accepted native action.");
            if (!inFlight) { flightSamples.Add($"{milliseconds}ms row={(pending ? "pending" : "revealed")}"); return; }
            var flight = flights.Single();
            Assert(flight.Opacity > .99 && flight.Parts.Count(part => part.Name == "artwork") == 1 &&
                flight.Parts.Any(part => part.Name == "footer"),
                "An opaque, previously visible hand face and its ownership footer must remain present throughout flight.");
            var viewport = new Rect(host.Layer.RenderSize);
            foreach (var part in flight.Parts.Where(part => part.Name is "face" or "artwork" or "footer"))
            {
                Assert(viewport.Contains(part.Shape.Bounds) && (part.Clip is null ||
                    DrawingGeometry.Combine(part.Shape, part.Clip, GeometryCombineMode.Exclude, null).GetArea() < .5),
                    $"Native {width}px at {milliseconds}ms: {part.Name} was clipped instead of flying visibly around the controls; bounds={part.Shape.Bounds}, viewport={viewport}.");
            }
            var obstacles = Find<Button>((DependencyObject)host.Window.FindName("ActionBar"))
                .Concat(Find<Button>((DependencyObject)host.Window.FindName("SupplementalActionBar")))
                .Where(button => button.IsVisible && button.ActualWidth > 0 && button.ActualHeight > 0)
                .Select(button => (Name: button.Name.Length > 0 ? button.Name : "supplemental button",
                    Bounds: button.TransformToVisual(host.Layer).TransformBounds(new Rect(button.RenderSize)))).ToList();
            var otherPortraits = Find<Button>(host.Root)
                .Where(button => BattleFeedbackLayer.GetSeatAnchor(button) >= 0 &&
                    BattleFeedbackLayer.GetSeatAnchor(button) != playCue.SourceSeat &&
                    button.IsVisible && button.ActualWidth > 0 && button.ActualHeight > 0).ToArray();
            Assert(otherPortraits.Select(BattleFeedbackLayer.GetSeatAnchor).Distinct().Count() == after.Players.Count - 1,
                "The native flight guard must include every other seat's visible portrait, including its HP icons.");
            obstacles.AddRange(otherPortraits.Select(button =>
                (Name: $"seat {BattleFeedbackLayer.GetSeatAnchor(button)} portrait",
                    Bounds: button.TransformToVisual(host.Layer).TransformBounds(new Rect(button.RenderSize)))));
            var hint = (TextBlock)host.Window.FindName("ActionDockHint");
            if (hint.IsVisible)
            {
                var glyphs = DrawingParts(VisualTreeHelper.GetDrawing(hint), null, ToLayer(hint, host.Layer), 1, null)
                    .Where(part => part.Name == "glyph").Select(part => part.Shape.Bounds).ToArray();
                Assert(glyphs.Length > 0, "The native action hint must contribute its actual rendered glyphs to the flight guard.");
                obstacles.AddRange(glyphs.Select(bounds => ("ActionDockHint glyph", bounds)));
            }
            Assert(obstacles.Any(obstacle => obstacle.Name == "EndTurnButton"), "The native flight guard lost the available End button.");
            if (milliseconds == 80)
                Console.WriteLine($"Native Lihuo flight guard {playerCount} seats {width}px at 80ms: " +
                    $"play-enabled={((Button)host.Window.FindName("PlayCardButton")).IsEnabled}, " +
                    $"end-enabled={((Button)host.Window.FindName("EndTurnButton")).IsEnabled}; " +
                    string.Join(" | ", obstacles.Select(obstacle => $"{obstacle.Name}={obstacle.Bounds}")));
            foreach (var obstacle in obstacles)
                Assert(!flight.Bounds.IntersectsWith(obstacle.Bounds),
                    $"Native {width}px at {milliseconds}ms: whole public flight {flight.Bounds} obscures {obstacle.Name} {obstacle.Bounds}; clipping or hiding the face is not an acceptable route.");
            if (milliseconds == 1)
                Assert(Near(flight.Face, handOrigin, .05),
                    $"The native public flight must start at its exact selected hand surface, not a relocated safe launch; actual={flight.Face}, hand={handOrigin}.");
            if (milliseconds == 320)
                Assert((Center(flight.Face) - Center(landing)).Length < 5 &&
                    Math.Abs(flight.Face.Width - landing.Width) < 8 && Math.Abs(flight.Face.Height - landing.Height) < 8,
                    $"The complete native face must reach its own public row before handoff, rather than jumping from the detour; face={flight.Face}, row={landing}.");
            flightSamples.Add($"{milliseconds}ms face={flight.Face} whole={flight.Bounds}");
        }

        void AssertCommittedState(string boundary)
        {
            var owner = Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
            var rail = Find<Border>(owner).Single(border => border.Name == "SeatHealthRail");
            var icons = Find<ItemsControl>(owner).Single(items => items.Name == "SeatHealthIcons");
            Assert(model.CanEndTurn && model.EndTurnCommand.CanExecute(null) && game.AcceptedCommands.Count == commands &&
                Enumerable.Range(0, game.PlayerCount).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))) &&
                System.Windows.Automation.AutomationProperties.GetName(rail) == $"{fact.RemainingHp}/{after.Players[0].MaxHp}" &&
                icons.Items.Cast<string>().Count(path => path.EndsWith("gallery-hp-wu.png", StringComparison.Ordinal)) == fact.RemainingHp,
                $"{boundary}: the native HP icons/rail must show already-committed HP and leave the next command available throughout presentation.");
        }

        void AdvanceTo(int target)
        {
            if (movie is not null)
                while ((movieTime / 40 + 1) * 40 <= target)
                {
                    var next = (movieTime / 40 + 1) * 40;
                    host.Clock.Advance((next - movieTime) / 1000d); movieTime = next;
                    BattleAudioBeatChecks.Tick(host.Layer); MovieFrame();
                }
            if (movieTime < target)
            {
                host.Clock.Advance((target - movieTime) / 1000d); movieTime = target;
                BattleAudioBeatChecks.Tick(host.Layer);
            }
        }
        void MovieFrame()
        {
            if (movie is null) return;
            foreach (var surface in Find<CardLiftSurface>(hand)) surface.InvalidateVisual();
            host.Layer.InvalidateVisual();
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)4); metadata.SetQuery("/grctlext/Disposal", (byte)2);
            movie.Frames.Add(BitmapFrame.Create(host.MovieFrame(), null, metadata, null));
        }
        static byte[] Pixels(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
        }
    }

    private sealed record FlightPart(string Name, DrawingGeometry Shape, DrawingGeometry? Clip);
    private sealed record FlightVisual(Rect Face, Rect Bounds, double Opacity, IReadOnlyList<FlightPart> Parts);

    private static IEnumerable<FlightVisual> CardFlights(Drawing? drawing, ImageSource artwork,
        Matrix transform, double opacity, DrawingGeometry? clip)
    {
        if (drawing is not DrawingGroup group) yield break;
        var next = group.Transform?.Value ?? Matrix.Identity;
        next.Append(transform);
        var nextOpacity = opacity * group.Opacity;
        var nextClip = WithClip(clip, group.ClipGeometry, next);
        var face = group.Children.OfType<GeometryDrawing>().SingleOrDefault(part =>
            part.Geometry is RectangleGeometry && part.Brush is SolidColorBrush ink && ink.Color == Colors.Wheat);
        if (face is not null)
        {
            var parts = DrawingParts(group, artwork, transform, opacity, clip).ToArray();
            if (parts.Any(part => part.Name == "artwork"))
            {
                var bounds = Rect.Empty;
                foreach (var part in parts) bounds.Union(part.Shape.Bounds);
                yield return new(InScene(face.Geometry, next).Bounds, bounds, nextOpacity, parts);
                yield break;
            }
        }
        foreach (var child in group.Children)
            foreach (var flight in CardFlights(child, artwork, next, nextOpacity, nextClip)) yield return flight;
    }

    private static IEnumerable<FlightPart> DrawingParts(Drawing? drawing, ImageSource? artwork,
        Matrix transform, double opacity, DrawingGeometry? clip)
    {
        if (opacity <= .001) yield break;
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity;
            next.Append(transform);
            var nextClip = WithClip(clip, group.ClipGeometry, next);
            foreach (var child in group.Children)
                foreach (var part in DrawingParts(child, artwork, next, opacity * group.Opacity, nextClip)) yield return part;
        }
        else if (drawing is ImageDrawing image)
            yield return new(ReferenceEquals(image.ImageSource, artwork) ? "artwork" : "image",
                InScene(new RectangleGeometry(image.Rect), transform), clip);
        else if (drawing is GlyphRunDrawing glyph)
            yield return new("glyph", InScene(glyph.GlyphRun.BuildGeometry(), transform), clip);
        else if (drawing is GeometryDrawing geometry && (geometry.Brush is not null || geometry.Pen is not null))
        {
            var name = geometry.Brush is SolidColorBrush ink ? ink.Color == Colors.Wheat ? "face" :
                ink.Color == Color.FromArgb(224, 35, 27, 19) ? "footer" : "detail" : "detail";
            var shape = geometry.Geometry;
            if (geometry.Pen is not null)
            {
                var stroke = shape.GetWidenedPathGeometry(geometry.Pen);
                shape = geometry.Brush is null ? stroke : DrawingGeometry.Combine(shape, stroke, GeometryCombineMode.Union, null);
            }
            yield return new(name, InScene(shape, transform), clip);
        }
    }

    private static DrawingGeometry InScene(DrawingGeometry shape, Matrix transform)
    {
        var result = shape.CloneCurrentValue();
        var existing = result.Transform?.Value ?? Matrix.Identity;
        existing.Append(transform);
        result.Transform = new MatrixTransform(existing);
        return result;
    }

    private static DrawingGeometry? WithClip(DrawingGeometry? previous, DrawingGeometry? local, Matrix transform)
    {
        if (local is null) return previous;
        var current = InScene(local, transform);
        return previous is null ? current : DrawingGeometry.Combine(previous, current, GeometryCombineMode.Intersect, null);
    }

    private static Matrix ToLayer(Visual visual, Visual layer)
    {
        var transform = visual.TransformToVisual(layer);
        var origin = transform.Transform(new Point());
        var x = transform.Transform(new Point(1, 0)) - origin;
        var y = transform.Transform(new Point(0, 1)) - origin;
        return new(x.X, x.Y, y.X, y.Y, origin.X, origin.Y);
    }

    private static bool Near(Rect actual, Rect expected, double tolerance) =>
        Math.Abs(actual.X - expected.X) <= tolerance && Math.Abs(actual.Y - expected.Y) <= tolerance &&
        Math.Abs(actual.Width - expected.Width) <= tolerance && Math.Abs(actual.Height - expected.Height) <= tolerance;
    private static Point Center(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private sealed class LihuoFixture(int playerCount) : IGameContentPackage
    {
        public const string Mode = "identity:classic-lihuo-feedback";
        public PackageManifest Manifest { get; } = new("fixture-lihuo-feedback", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:cheng-pu");
            var peers = Enumerable.Range(1, playerCount - 1).Select(seat => $"fixture:lihuo-feedback-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "邻座", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:lihuo-feedback-deck", "固定普通杀实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(index => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, index % 13 + 1)).ToArray() });
            var roles = playerCount == 4
                ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }
                : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2, [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1 };
            builder.AddMode(new(Mode, "疠火自损反馈", playerCount, playerCount, roles,
                "fixture:lihuo-feedback-deck", GeneralCandidateCount: playerCount,
                GeneralPoolIds: ["classic:cheng-pu", .. peers]));
        }
    }

    private static void NativeSilverLionReplacement(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new SilverLionFixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = SilverLionFixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, SilverLionFixture.Owner, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 12 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed native armor fixture did not reach its first Play boundary.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry)
        { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Assert(!model.HasSaveError && model.BattleCues.Count == 0, model.SaveStatus);
        game = Engine(model);
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        host.Capture(output, "health-native-lion-before", 1120);
        var first = Equip();
        var before = game.CreateSnapshot(0).Players[0];
        Assert(before.Hp < before.MaxHp && before.Equipment.Single().Id == first,
            "A genuinely wounded native owner must equip the first physical Silver Lion.");
        host.Clock.Advance(2);
        var cursor = game.Events.Count;
        var second = Equip();
        var facts = game.Events.Skip(cursor).ToArray();
        var recovered = facts.Where(e => e.Payload is RecoveryAppliedEvent).ToArray();
        var removed = facts.Single(e => e.Payload is SilverLionRemovedRecoveryEvent);
        var after = game.CreateSnapshot(0).Players[0];
        Assert(recovered is [{ Payload: RecoveryAppliedEvent { Amount: 1, TargetSeat: 0 } }] &&
            removed.Payload is SilverLionRemovedRecoveryEvent { RecoveredAmount: 1, PlayerSeat: 0 } &&
            after.Hp == before.Hp + 1 && after.Equipment.Single().Id == second &&
            game.CardMovements.Any(move => move.CardId == first && move.From == CardLocation.Equipment(0) &&
                move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.EquipmentReplace),
            "The second native armor use must actually replace the first and recover exactly one HP.");
        var cues = model.BattleCues.Where(cue => facts.Any(e => e.Sequence == cue.Sequence)).ToArray();
        var commands = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        host.Clock.Advance(.4); host.Layer.InvalidateVisual();
        host.Capture(output, "health-native-lion-replaced-400ms", 1120);
        Console.WriteLine($"Native Silver Lion: replaced physical {first} with {second}; HP {before.Hp}->{after.Hp}; " +
            $"actual recoveries={recovered.Length}; recovery cues={cues.Count(cue => cue.Kind == BattleCueKind.Recovery)}; " +
            $"cues={string.Join(" | ", cues.Select(cue => $"{cue.Kind}:{cue.Label}/{cue.Detail}"))}.");
        Assert(game.AcceptedCommands.Count == commands && Enumerable.Range(0, 4).All(seat =>
                views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
            "Rendering the native armor removal must not change commands or player-safe state.");
        Assert(cues.Where(cue => cue.Kind == BattleCueKind.Recovery).ToArray() is
                [{ HpDelta: 1, RemainingHp: var hp, TargetSeats: [0] }] && hp == after.Hp &&
            cues.Single(cue => cue.Kind == BattleCueKind.Recovery).Sequence == recovered[0].Sequence,
            "One actual native Silver Lion recovery must produce exactly one numeric recovery cue, never a second equipment-result heal.");
        Assert(cues.Single(cue => cue.Sequence == removed.Sequence) is
                { Kind: BattleCueKind.ResponseWindow, IsInquiry: false, SourceSeat: 0, TargetSeats: [0],
                    Label: "白银狮子 · 移除", HpDelta: 0, RemainingHp: null } &&
            string.Concat(GlyphText(VisualTreeHelper.GetDrawing(host.Layer))).Contains("移除", StringComparison.Ordinal),
            "The actual native frame must retain a readable Silver Lion removal explanation beside the one real HP result.");

        int Equip()
        {
            var card = model.Hand.First(card => card.Kind == CardKind.SilverLion && card.IsPlayable);
            var id = card.Id;
            var count = game.AcceptedCommands.Count;
            model.SelectCardCommand.Execute(card);
            Assert(model.CanConfirmSelected, "A real Silver Lion in the native hand must be confirmable.");
            model.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == count + 1 && game.AcceptedCommands.Last() is PlayCardCommand { CardId: var played } && played == id,
                "The native armor selection must commit exactly its physical card through the view-model command.");
            for (var step = 0; step < 12 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            {
                Assert(model.CanStepAi, "The paid native armor use must resume without another human choice.");
                model.StepAiCommand.Execute(null);
            }
            Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The paid native armor use did not return to the current player's Play boundary.");
            return id;
        }
        static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "Native armor setup failed.");
    }

    private static IEnumerable<string> GlyphText(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing { GlyphRun.Characters: { } characters }) yield return new string(characters.ToArray());
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var text in GlyphText(child)) yield return text;
    }

    private sealed class SilverLionFixture : IGameContentPackage
    {
        public const string Mode = "identity:classic-lion-feedback", Owner = "fixture:lion-feedback-owner";
        public PackageManifest Manifest { get; } = new("fixture-lion-feedback", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "换甲本人", "supporter", "standard:none", "wu", 4) { InitialHp = 2 });
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:lion-feedback-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "邻座", "supporter", "standard:none", "wei", 4));
            builder.AddCard(new("fixture:lion-feedback-card", "白银狮子", "装备牌", "原生防具移除回复", CardKind.SilverLion));
            builder.AddDeck(new("fixture:lion-feedback-deck", "固定换甲实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(index => new ContentDeckPhysicalCard("fixture:lion-feedback-card", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "换甲反馈", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:lion-feedback-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, .. peers]));
        }
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
            var portrait = Find<Button>(root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
            var control = Find<ItemsControl>(portrait).Single(items => items.Name == "SeatHealthIcons");
            var healthRail = Find<Border>(portrait).Single(border => border.Name == "SeatHealthRail");
            Assert(control.Visibility == Visibility.Visible && control.ActualWidth > 0 && control.Items.Count == Math.Clamp(recovered.MaxHp, 0, 8) &&
                control.Items.Cast<string>().Count(path => path.EndsWith("gallery-hp-wu.png", StringComparison.Ordinal)) == Math.Clamp(recovered.Hp, 0, 8) &&
                healthRail.Visibility == Visibility.Visible && healthRail.ActualHeight > 0 &&
                System.Windows.Automation.AutomationProperties.GetName(healthRail) == $"{recovered.Hp}/{recovered.MaxHp}",
                $"The real portrait icons are missing or detached from current committed public HP: icons={control.Visibility}/{control.ActualWidth}/{control.Items.Count}, " +
                $"rail={healthRail.Visibility}/{healthRail.ActualHeight}/{System.Windows.Automation.AutomationProperties.GetName(healthRail)}, HP={recovered.Hp}/{recovered.MaxHp}.");
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null);
            Render(root, 1120, 740, Path.Combine(output, "health-native-recovery-loaded.png"));
            var loaded = Engine(vm);
            Assert(layer.ActiveEffectCount == 0 && !vm.HasSaveError && loaded.AcceptedCommands.Count == accepted &&
                   Enumerable.Range(0, 4).All(seat => views[seat] == SnapshotJson.Serialize(loaded.CreateSnapshot(seat))),
                "Saving/loading HP feedback replayed an animation or changed a player-safe snapshot.");
            var loadedPortrait = Find<Button>(root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
            var loadedIcons = Find<ItemsControl>(loadedPortrait).Single(items => items.Name == "SeatHealthIcons");
            var loadedRail = Find<Border>(loadedPortrait).Single(border => border.Name == "SeatHealthRail");
            Assert(loadedIcons.Items.Cast<string>().SequenceEqual(control.Items.Cast<string>()) &&
                loadedRail.Visibility == Visibility.Visible && loadedRail.ActualHeight > 0 &&
                System.Windows.Automation.AutomationProperties.GetName(loadedRail) == $"{recovered.Hp}/{recovered.MaxHp}",
                "A real load reverted the current portrait's HP icons.");
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
