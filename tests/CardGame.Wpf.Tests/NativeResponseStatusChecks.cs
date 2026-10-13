using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class NativeResponseStatusChecks
{
    public static void PersistentResponses(string output)
    {
        var errors = new BindingListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            ConsecutiveDodges(output);
            CounterNullification(output);
            DifferentRescueActor(output);
            Assert(errors.Errors.Count == 0, "Native response status binding errors: " + string.Join(Environment.NewLine, errors.Errors));
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
    }

    private static void ConsecutiveDodges(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, wushuang: true);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true;
        vm.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
        using var host = new NativeHost(vm);
        var game = Engine(vm);
        var initial = game.CreateSnapshot(0).PendingDecision!;
        var hp = game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hp;
        var cursor = game.Events.Count;
        var accepted = game.AcceptedCommands.Count;
        Assert(initial is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0, SourceSeat: 1, IncomingCard: CardKind.Slash } &&
            vm.Hand.Where(card => card.Kind == CardKind.Dodge && card.IsPlayable).Select(card => card.Id).Order().SequenceEqual(new[] { 24, 32 }),
            "The optional seed31 Wushuang fixture did not retain its native AI1 Slash and two physical human Dodges; do not search another seed.");
        host.Capture(output, "response-status-wushuang-before", 1120);
        AssertStatus(host, "待打闪", "等待你打出【闪】");
        Pay(vm, 32);
        var first = game.Events.Skip(cursor).Select(item => item.Payload).OfType<RequiredResponseProgressEvent>().Single();
        var secondPrompt = game.CreateSnapshot(0).PendingDecision!;
        Assert(first is { SkillOwnerSeat: 1, ResponderSeat: 0, IncomingCard: CardKind.Slash,
                RequiredCardKind: CardKind.Dodge, ResponseCount: 1, RequiredResponseCount: 2 } &&
            secondPrompt is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0, SourceSeat: 1, RequiredCardKind: CardKind.Dodge } &&
            secondPrompt.PromptId != initial.PromptId && game.AcceptedCommands.Count == accepted + 1 &&
            game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardRespondedEvent>()
                .Single(item => item.ResponderSeat == 0).CardId == 32 &&
            !vm.Hand.Any(card => card.Id == 32) && vm.Hand.Single(card => card.Id == 24).IsPlayable &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hp == hp &&
            !game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent { TargetSeat: 0 }),
            "Paying the first physical Dodge must publish actual 1/2 progress and another native response, without declaring or granting full avoidance.");
        Assert(vm.BattleCues.Any(cue => cue.IsInquiry && cue.CardKind == CardKind.Slash && cue.SourceSeat == 0),
            "The second actual Dodge prompt must publish its own incoming response inquiry before the long wait.");
        var firstPixels = PaidResponseFrames(host, output, "paid-1", first, 32, waiting: true, clearMotion: true, record: false);
        WaitAndCapture(host, output, "response-status-wushuang-second", "待打闪", "等待你打出【闪】", bothSizes: true);
        var paidBoundary = Boundary(game);
        vm.SaveGameCommand.Execute(null);
        Assert(!vm.HasSaveError && Same(game, paidBoundary), "Saving the actual first-paid checkpoint committed or changed its pending second Dodge.");

        Pay(vm, 24);
        var progress = game.Events.Skip(cursor).Select(item => item.Payload).OfType<RequiredResponseProgressEvent>().ToArray();
        Assert(progress.Length == 2 && progress[1].ResolutionId == first.ResolutionId &&
            progress[1] is { ResponseCount: 2, RequiredResponseCount: 2 } &&
            game.AcceptedCommands.Count == accepted + 2 &&
            game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardRespondedEvent>()
                .Where(item => item.ResponderSeat == 0).Select(item => item.CardId).SequenceEqual(new[] { 32, 24 }) &&
            !game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent { TargetSeat: 0 }) &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hp == hp,
            "Two actual paid Dodges must finish this plain physical Slash fixture once, with matching public progress and no native damage.");
        var secondPixels = PaidResponseFrames(host, output, "paid-2", progress[1], 24, waiting: false, clearMotion: false, record: RecordMotion);
        Assert(firstPixels.Length == secondPixels.Length && !firstPixels.AsSpan().SequenceEqual(secondPixels),
            "Actual 1/2 and 2/2 payments produced an identical native portrait effect at the same 180 ms sample.");
        host.Capture(output, "response-status-wushuang-completed", 1120);
        AssertCleared(host);

        // Restore the exact already-paid checkpoint for the alternate native
        // branch; no card or private resolution frame is injected or rewound.
        vm.LoadManualGameCommand.Execute(null);
        game = Engine(vm);
        Assert(!vm.HasSaveError && Same(game, paidBoundary) && host.Layer.ActiveEffectCount == 0 &&
            game.Events.Select(item => item.Payload).OfType<RequiredResponseProgressEvent>()
                .Single(item => item.ResolutionId == first.ResolutionId).ResponseCount == 1 &&
            !vm.HasSelection && !vm.CanConfirmSelected,
            "Loading the actual first-paid checkpoint must retire the completed branch and historical flight without paying or carrying over its second response.");
        host.Capture(output, "response-status-wushuang-restored-second", 1120);
        AssertStatus(host, "待打闪", "等待你打出【闪】");
        host.Clock.Advance(.18); host.Layer.InvalidateVisual();
        host.Capture(output, "response-progress-loaded-no-replay", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && !HasDodgeFlight(host.Layer) && !ResponseInk(host).Any(),
            "Restoring first-paid progress replayed a historical Dodge or response tick body.");
        var declineCursor = game.Events.Count;
        var declineCommands = game.AcceptedCommands.Count;
        var declineCueSequences = vm.BattleCues.Select(cue => cue.Sequence).ToHashSet();
        vm.DeclineResponseCommand.Execute(null);
        var damage = game.Events.Skip(declineCursor).Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .Single(item => item.TargetSeat == 0);
        Assert(damage is { SourceSeat: 1, Amount: 1 } && damage.RemainingHp == hp - damage.Amount &&
            game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hp == damage.RemainingHp &&
            game.AcceptedCommands.Count == declineCommands + 1 && vm.Hand.Any(card => card.Id == 24) &&
            !game.Events.Skip(declineCursor).Any(item => item.Payload is CardRespondedEvent { ResponderSeat: 0 } or RequiredResponseProgressEvent),
            "Declining only the native second Dodge must apply the actual damage once while retaining its unplayed physical card and first paid progress.");
        var declineCues = vm.BattleCues.Where(cue => !declineCueSequences.Contains(cue.Sequence)).ToArray();
        Assert(declineCues.Count(cue => cue.Kind == BattleCueKind.Damage && cue.TargetSeats.Contains(0) && cue.HpDelta == -damage.Amount) == 1 &&
            !declineCues.Any(cue => cue.HasResponseProgress || cue.Kind == BattleCueKind.Response &&
                (cue.PublicCardId is not null || cue.CardKind is not null)),
            "The actual second-response decline invented a paid response/progress cue or lost its single public damage result.");
        var declineBoundary = Boundary(game);
        host.Clock.Advance(.04); host.Layer.InvalidateVisual();
        host.Capture(output, "response-progress-second-declined-hit", 1120);
        Assert(!ResponseInk(host).Any() && !HasDodgeFlight(host.Layer) && Same(game, declineBoundary),
            "The native decline struck its actual target while replaying a Dodge/progress body or changing the committed result.");
        host.Capture(output, "response-status-wushuang-declined", 1120);
        AssertCleared(host);
        Console.WriteLine($"Persistent native Wushuang: seed31, AI1 Slash, Dodge32->actual1/2->{secondPrompt.PromptId}, " +
            $"2s visible at1120/1920; Dodge24->actual2/2 and HP{hp}; same paid checkpoint decline->HP{damage.RemainingHp}, commands +1.");
    }

    private static byte[] PaidResponseFrames(NativeHost host, string output, string label,
        RequiredResponseProgressEvent progress, int physicalId, bool waiting, bool clearMotion, bool record)
    {
        var game = Engine(host.Model); var boundary = Boundary(game);
        var envelope = game.Events.Single(item => item.Payload is RequiredResponseProgressEvent actual && actual == progress);
        var cue = host.Model.BattleCues.Single(item => item.Sequence == envelope.Sequence);
        var physical = host.Model.BattleCues.Single(item => item.PublicCardId == physicalId && item.CardKind == CardKind.Dodge);
        Assert(cue is { Kind: BattleCueKind.ResponseWindow, IsInquiry: false, HasResponseProgress: true, CardKind: null,
                PublicCardId: null, PublicCard: null } && cue.Revision == envelope.Revision &&
            cue.ResponseResolutionId == progress.ResolutionId && cue.SourceSeat == progress.ResponderSeat &&
            cue.TargetSeats.SequenceEqual(new[] { progress.SkillOwnerSeat }) &&
            cue.ResponseCount == progress.ResponseCount && cue.RequiredResponseCount == progress.RequiredResponseCount &&
            cue.RequiredResponseCardKind == progress.RequiredCardKind &&
            physical is { Kind: BattleCueKind.Response, SourceSeat: 0, PublicCard: not null, HasResponseProgress: false } &&
            physical.PublicCard.Id == physicalId && physical.PublicCard.Kind == CardKind.Dodge &&
            !new[] { "免伤", "躲过", "闪避成功", "伤害已防止", "无效" }
                .Any(claim => (cue.Label + cue.Detail + physical.Label + physical.Detail).Contains(claim, StringComparison.Ordinal)),
            "The native paid response must project the exact public progress fields and physical cost, without inventing a final avoidance outcome.");
        var stages = new Dictionary<long, string>
        {
            [TimeSpan.FromSeconds(.04).Ticks] = "first-40ms",
            [TimeSpan.FromSeconds(.18).Ticks] = "middle-180ms",
            [TimeSpan.FromSeconds(.4).Ticks] = "late-400ms",
            [TimeSpan.FromSeconds(1.5).Ticks] = "retired-1500ms"
        };
        var samples = new SortedSet<long>(stages.Keys);
        if (record) foreach (var frame in Enumerable.Range(1, 40)) samples.Add(TimeSpan.FromMilliseconds(frame * 40).Ticks);
        var movie = record ? new GifBitmapEncoder() : null;
        byte[]? middle = null;
        long elapsed = 0;
        foreach (var sample in samples)
        {
            host.Clock.Advance(TimeSpan.FromTicks(sample - elapsed).TotalSeconds); elapsed = sample;
            host.Layer.InvalidateVisual();
            if (stages.TryGetValue(sample, out var stage))
            {
                host.Capture(output, $"response-progress-{label}-{stage}", 1120);
                if (waiting) AssertStatus(host, "待打闪", "等待你打出【闪】"); else AssertCleared(host);
                Assert(Same(game, boundary), "Native response animation sampling changed a command, physical cost, public event or filtered snapshot.");
                var ink = ResponseInk(host).ToArray();
                if (sample < TimeSpan.FromSeconds(1).Ticks)
                {
                    var responder = Bounds(Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == progress.ResponderSeat), host.Layer);
                    var artwork = new Rect(responder.Left + responder.Width * .24, responder.Top + responder.Height * .2,
                        responder.Width * .6, responder.Height * .46);
                    var opaqueCaptions = Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1).Where(shape =>
                        shape.Drawing.Brush is SolidColorBrush fill && fill.Color.R == 19 && fill.Color.G == 31 && fill.Color.B == 34 &&
                        shape.Opacity * fill.Opacity * fill.Color.A / 255d > .2 && shape.Bounds.IntersectsWith(artwork)).ToArray();
                    Assert(opaqueCaptions.Length == 0,
                        $"A dark opaque inquiry/progress caption covered the actual responder artwork at {TimeSpan.FromTicks(sample).TotalMilliseconds:F0}ms: " +
                        string.Join("; ", opaqueCaptions.Select(shape => shape.Bounds.ToString())));
                    var commandBounds = Find<System.Windows.Controls.Primitives.ButtonBase>(host.Root)
                        .Where(button => button.IsVisible && button.ActualWidth > 0 && button.ActualHeight > 0)
                        .Select(button => Bounds(button, host.Layer)).ToArray();
                    Assert(!Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1).Any(shape =>
                        shape.Drawing.Brush is SolidColorBrush fill && fill.Color.R == 19 && fill.Color.G == 31 && fill.Color.B == 34 &&
                        shape.Opacity * fill.Opacity * fill.Color.A / 255d > .2 &&
                        commandBounds.Any(bounds => shape.Bounds.IntersectsWith(bounds))),
                        "A real response caption covered a visible command, hand card or portrait button.");
                    Assert(ink.Length > 0 && ink.All(shape => shape.Drawing.Geometry.GetFlattenedPathGeometry().Figures.All(figure => !figure.IsClosed)) &&
                        !ClosedShield(host).Any(),
                        "Actual response motion lost its visible open side-step/ticks or drew a closed portrait shield on a partial payment.");
                    var otherSeats = Find<Button>(host.Root).Where(button => BattleFeedbackLayer.GetSeatAnchor(button) is >= 0 and not 0)
                        .Select(button => Bounds(button, host.Layer)).ToArray();
                    Assert(ink.All(shape => otherSeats.All(bounds => !shape.Bounds.IntersectsWith(bounds))),
                        "A real human's paid response body/ticks painted over another player's portrait.");
                    if (sample == TimeSpan.FromSeconds(.18).Ticks) middle = SourceEffectPixels(host);
                }
                else Assert(host.Layer.ActiveEffectCount == 0 && ink.Length == 0 && !HasDodgeFlight(host.Layer),
                    "The actual paid response retained its portrait body, ticks or face flight beyond the short native timeline.");
                Console.WriteLine($"Native response progress sample: {label}, public {cue.ResponseCount}/{cue.RequiredResponseCount}, " +
                    $"sequence={cue.Sequence}, physical={physicalId}, age={TimeSpan.FromTicks(sample).TotalMilliseconds:F0}ms, " +
                    $"open cyan strokes={ink.Length}, pending responder={game.CreateSnapshot(0).PendingDecision?.PlayerSeat}, safeviews unchanged.");
            }
            if (movie is not null && sample % TimeSpan.FromMilliseconds(40).Ticks == 0)
            {
                var bitmap = NativeBitmap(host);
                var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", (ushort)4);
                metadata.SetQuery("/grctlext/Disposal", (byte)2);
                if (movie.Frames.Count == 0)
                {
                    metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                }
                movie.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
            }
            if (clearMotion && sample == TimeSpan.FromSeconds(.4).Ticks)
            {
                host.Model.IsMotionEnabled = false;
                host.Capture(output, "response-progress-paid-1-motion-off", 1120);
                AssertStatus(host, "待打闪", "等待你打出【闪】");
                Assert(host.Layer.ActiveEffectCount == 0 && !ResponseInk(host).Any() && !HasDodgeFlight(host.Layer),
                    "Disabling response motion did not clear the current paid body/ticks while preserving its actual second-response status.");
                host.Model.IsMotionEnabled = true;
                host.Clock.Advance(.04); elapsed += TimeSpan.FromSeconds(.04).Ticks;
                host.Layer.InvalidateVisual(); host.Capture(output, "response-progress-paid-1-motion-on-no-replay", 1120);
                AssertStatus(host, "待打闪", "等待你打出【闪】");
                Assert(host.Layer.ActiveEffectCount == 0 && !ResponseInk(host).Any() && !HasDodgeFlight(host.Layer) && Same(game, boundary),
                    "Re-enabling motion replayed an already-paid response/progress or changed its exact native pending state.");
            }
        }
        if (movie is not null)
        {
            var path = Path.Combine(output, $"response-progress-native-{label}.gif");
            using (var file = File.Create(path)) movie.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var fileRead = File.OpenRead(path);
            var decoded = new GifBitmapDecoder(fileRead, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(decoded.Frames.Count == 40 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                frame.Metadata is BitmapMetadata metadata && Convert.ToInt32(metadata.GetQuery("/grctlext/Delay")) == 4),
                "The actual second-paid response movie must decode as 40 native 40 ms frames at 728x481.");
            Assert(!DecodedPixels(0).AsSpan().SequenceEqual(DecodedPixels(4)) &&
                !DecodedPixels(9).AsSpan().SequenceEqual(DecodedPixels(39)),
                "The decoded native response movie lost the actual moving and retired frames.");
            Assert(Same(game, boundary), "Recording the native response movie changed its actual command or player-safe views.");
            Console.WriteLine($"Native response progress movie: {path}, 40x40ms, 728x481, actual Dodge{physicalId}, " +
                $"public {cue.ResponseCount}/{cue.RequiredResponseCount}, one native payment, sampled continuously before expiry; safeviews unchanged.");
            byte[] DecodedPixels(int index)
            {
                var bitmap = new FormatConvertedBitmap(decoded.Frames[index], PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                return pixels;
            }
        }
        return middle ?? throw new InvalidOperationException("The actual response's 180 ms portrait sample was not captured.");
    }

    private static readonly Color ResponseCyan = Color.FromRgb(116, 231, 255);
    private static IEnumerable<(GeometryDrawing Drawing, Rect Bounds, double Opacity)> ResponseInk(NativeHost host)
    {
        var portrait = Bounds(Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0), host.Layer);
        return Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1).Where(shape =>
            shape.Drawing.Pen?.Brush is SolidColorBrush brush && brush.Color.R == ResponseCyan.R &&
            brush.Color.G == ResponseCyan.G && brush.Color.B == ResponseCyan.B && shape.Opacity * brush.Opacity * brush.Color.A / 255d > .001 &&
            shape.Bounds.IntersectsWith(portrait));
    }
    private static IEnumerable<GeometryDrawing> ClosedShield(NativeHost host)
    {
        var portrait = Bounds(Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0), host.Layer);
        return Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1).Where(shape =>
            shape.Drawing.Pen?.Brush is SolidColorBrush brush && brush.Color.R == 255 && brush.Color.G == 249 && brush.Color.B == 218 &&
            shape.Opacity > .001 && shape.Bounds.IntersectsWith(portrait) && shape.Bounds.Width > portrait.Width * .4 &&
            shape.Bounds.Height > portrait.Height * .35 && shape.Drawing.Geometry.GetFlattenedPathGeometry().Figures.Any(figure => figure.IsClosed))
            .Select(shape => shape.Drawing);
    }
    private static byte[] SourceEffectPixels(NativeHost host)
    {
        var portrait = Bounds(Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0), host.Layer);
        var artwork = new Rect(portrait.Left + portrait.Width * .24, portrait.Top + portrait.Height * .2, portrait.Width * .6, portrait.Height * .46);
        var width = (int)Math.Ceiling(host.Layer.ActualWidth); var height = (int)Math.Ceiling(host.Layer.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host.Layer);
        var crop = new Int32Rect((int)Math.Floor(artwork.Left), (int)Math.Floor(artwork.Top),
            (int)Math.Ceiling(artwork.Width), (int)Math.Ceiling(artwork.Height));
        var pixels = new byte[crop.Width * crop.Height * 4]; bitmap.CopyPixels(crop, pixels, crop.Width * 4, 0);
        return pixels;
    }
    private static bool HasDodgeFlight(BattleFeedbackLayer layer) => Images(VisualTreeHelper.GetDrawing(layer))
        .Any(image => ReferenceEquals(image.ImageSource, CardArt.Get(CardKind.Dodge)));
    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var nested in Images(child)) yield return nested;
    }
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
    private static RenderTargetBitmap NativeBitmap(NativeHost host)
    {
        host.Root.UpdateLayout(); host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        Assert(host.Root.RenderSize == new Size(1120, 740), "The continuous native movie frame lost the exact existing root layout.");
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(.65, .65));
            dc.DrawRectangle(new VisualBrush(host.Root), null, new Rect(host.Root.RenderSize));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(728, 481, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        return bitmap;
    }

    private static void CounterNullification(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.Nullification, output);
        using var vm = fixture.Model;
        using var host = new NativeHost(vm);
        var game = Engine(vm);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var requested = game.Events.Select(item => item.Payload).OfType<NullificationRequestedEvent>().Last();
        var prior = game.Events.Select(item => item.Payload).OfType<NullificationRespondedEvent>()
            .Single(item => item.ResolutionId == requested.ResolutionId && item.ChainDepth == 1);
        Assert(prompt is { Kind: DecisionKind.Nullification, PlayerSeat: 0, IncomingCard: CardKind.PeachGarden } &&
            requested is { ResponderSeat: 0, ChainDepth: 1, EffectCurrentlyNullified: true } &&
            prior.ResponderSeat != 0 && prior.EffectNullified &&
            prompt.Choices.Where(choice => choice.Parameters.GetValueOrDefault("response") == "nullification")
                .All(choice => choice.Parameters.GetValueOrDefault("chain-depth") == "1"),
            "The unchanged seed31 fixture must reach its real second-layer human counterspell after an actual AI's first paid Nullification.");
        var boundary = Boundary(game);
        var counterCard = vm.Hand.First(card => card.Kind == CardKind.Nullification && card.IsPlayable);
        vm.SelectCardCommand.Execute(counterCard);
        host.Capture(output, "response-status-nullification-selected", 1120);
        Assert(vm.HasSelection && vm.CanConfirmSelected && Same(game, boundary), "Selecting a real counterspell committed its public effect or cost.");
        vm.ClearSelectionCommand.Execute(null);
        Assert(!vm.HasSelection && !vm.CanConfirmSelected && Same(game, boundary),
            "Cancelling only the selected counterspell must retain the same actual second-layer prompt and all four safe views.");
        WaitAndCapture(host, output, "response-status-nullification-second", "待无懈", "等待你的无懈 · 第2层", bothSizes: true);
        Assert(!vm.IsMotionEnabled, "The initial native fixture should retain its real motion-disabled preference.");
        vm.IsMotionEnabled = true;
        host.Clock.Advance(2); host.Layer.InvalidateVisual();
        host.Capture(output, "response-status-nullification-motion-on", 1120);
        AssertStatus(host, "待无懈", "等待你的无懈 · 第2层");
        vm.IsMotionEnabled = false;
        host.Capture(output, "response-status-nullification-motion-off", 1120);
        AssertStatus(host, "待无懈", "等待你的无懈 · 第2层");
        Assert(Same(game, boundary), "Response motion preferences changed an actual counterspell, accepted command or filtered snapshot.");

        vm.LoadManualGameCommand.Execute(null);
        game = Engine(vm);
        Assert(!vm.HasSaveError && Same(game, boundary) && host.Layer.ActiveEffectCount == 0,
            "Loading the unchanged actual counterspell checkpoint must clear historical animations and preserve its current public depth.");
        host.Capture(output, "response-status-nullification-loaded", 1120);
        AssertStatus(host, "待无懈", "等待你的无懈 · 第2层");
        var cursor = game.Events.Count;
        var accepted = game.AcceptedCommands.Count;
        var pass = vm.NullificationChoices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "pass");
        vm.SelectNullificationChoiceCommand.Execute(pass);
        var resolved = game.Events.Skip(cursor).Select(item => item.Payload).OfType<NullificationResolvedEvent>()
            .Single(item => item.ResolutionId == requested.ResolutionId);
        Assert(game.AcceptedCommands.Count == accepted + 1 && resolved is { EffectNullified: true, ChainDepth: 1 } &&
            !game.Events.Skip(cursor).Any(item => item.Payload is NullificationRespondedEvent { ResponderSeat: 0 }) &&
            vm.Hand.Any(card => card.Id == counterCard.Id), "The current native pass must finish the actual second layer once without paying the selected human counterspell.");
        host.Capture(output, "response-status-nullification-next-prompt", 1120);
        AssertCleared(host);

        vm.StartNewGameCommand.Execute(null);
        vm.ContinueFromIdentityRevealCommand.Execute(null);
        Assert(!ReferenceEquals(game, Engine(vm)) && vm.BattleCues.Count == 0,
            "Starting the configured real new match must replace its prior counterspell engine and cue journal.");
        host.Capture(output, "response-status-nullification-reset", 1120);
        AssertCleared(host);
        Console.WriteLine($"Persistent native Nullification: seed31, AI{prior.ResponderSeat} physical{prior.NullificationCardId}, " +
            $"human second-layer depth1, effect{requested.EffectCardId}, native pass terminal depth{resolved.ChainDepth}; draft/motion/load/reset boundaries preserved.");
    }

    private static void DifferentRescueActor(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RescueDying, output, jijiu: true);
        using var vm = fixture.Model;
        using var host = new NativeHost(vm);
        var game = Engine(vm);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var context = DecisionContext.From(game.CreateSnapshot(0))!;
        Assert(prompt is { Kind: DecisionKind.RescueDying, PlayerSeat: 0, SourceSeat: 1, TargetSeat: 1 } &&
            context is { SourceSeat: null, TargetSeat: 1, TargetLabel: "等待救援" } &&
            vm.Seats.Single(seat => seat.Seat == 0).HasResponseStatus && !vm.Seats.Single(seat => seat.Seat == 1).HasResponseStatus &&
            vm.Seats.Single(seat => seat.Seat == 1).DecisionRoleLabel == "等待救援",
            "The genuine Jijiu dying fixture must separate Human0's current response from AI1's actual dying role.");
        WaitAndCapture(host, output, "response-status-jijiu-current-actor", "待救援", "等待你的救援", bothSizes: true);
        var boundary = Boundary(game);
        var rescueChoice = vm.DyingChoices.First(choice => choice.Parameters.GetValueOrDefault("response") == "peach" &&
            choice.Parameters.GetValueOrDefault("physical-card-kind") == nameof(CardKind.Dodge));
        var physicalId = rescueChoice.Cards.Single();
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == physicalId));
        Assert(vm.HasSelection && vm.CanConfirmSelected && Same(game, boundary),
            "Preparing the native red Dodge rescue conversion changed the public victim or hidden hand boundary.");
        vm.ClearSelectionCommand.Execute(null);
        Assert(Same(game, boundary) && !vm.HasSelection, "Cancelling the actual rescue draft submitted a dying response.");
        var cursor = game.Events.Count;
        var accepted = game.AcceptedCommands.Count;
        Pay(vm, physicalId);
        var response = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DyingResponseEvent>()
            .Single(item => item.PeachCardId == physicalId);
        Assert(response is { ResponderSeat: 0, UsedPeach: true, UsedPeachPhysicalCardKind: CardKind.Dodge } &&
            game.AcceptedCommands.Count == accepted + 1 && game.CreateSnapshot(0).Players.Single(player => player.Seat == 1).Hp > 0 &&
            !vm.Hand.Any(card => card.Id == physicalId), "The actual Jijiu response must rescue its distinct native victim with the one real conversion cost.");
        host.Capture(output, "response-status-jijiu-completed", 1120);
        AssertCleared(host);
        Console.WriteLine($"Persistent native Jijiu: seed31, actor0 != victim1, actual physicalDodge{physicalId} conversion paid once; 2s safe wait at1120/1920.");
    }

    private static void Pay(MainViewModel vm, int id)
    {
        var game = Engine(vm); var accepted = game.AcceptedCommands.Count;
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var publishedChoice = prompt.Choices.Single(choice => choice.Cards.SequenceEqual(new[] { id }));
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == id));
        Assert(vm.CanConfirmSelected, "The current actual physical response could not be prepared through the native hand command.");
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(game.AcceptedCommands.Count == accepted + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == prompt.PromptId && answer.Choice == publishedChoice.Id,
            "The prepared native response must submit exactly its current published choice once.");
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(game.AcceptedCommands.Count == accepted + 1, "A repeated response confirmation paid the next prompt or physical cost twice.");
    }

    private static void WaitAndCapture(NativeHost host, string output, string label, string status, string headline, bool bothSizes)
    {
        var game = Engine(host.Model); var boundary = Boundary(game);
        host.Clock.Advance(2); host.Layer.InvalidateVisual();
        foreach (var width in bothSizes ? new[] { 1120, 1920 } : new[] { 1120 })
        {
            host.Capture(output, label + "-wait", width);
            AssertStatus(host, status, headline);
            Assert(host.Layer.ActiveEffectCount == 0 && Same(game, boundary),
                "Expired transient response effects must leave the current native status visible without submitting or altering any viewer-safe state.");
        }
    }

    private static void AssertStatus(NativeHost host, string status, string headline)
    {
        var vm = host.Model; var root = host.Root;
        DecisionContextChecks.VerifyLive(vm);
        var context = vm.CurrentDecisionContext!;
        var own = Engine(vm).CreateSnapshot(0).PendingDecision!;
        var banner = (Border)host.Window.FindName("ResponseStatusBanner");
        var heading = (TextBlock)host.Window.FindName("ResponseStatusText");
        var relationship = (TextBlock)host.Window.FindName("DecisionTitleText");
        var description = (TextBlock)host.Window.FindName("DecisionDescriptionText");
        Assert(host.Window.IsLoaded && host.Window.IsVisible && banner.IsVisible && heading.IsVisible && description.IsVisible &&
            context.ResponseStatusTitle == headline && heading.Text == headline && description.Text == context.Description &&
            !banner.IsHitTestVisible && !banner.Focusable && !string.IsNullOrWhiteSpace(description.Text),
            "The current native response actor headline and actual engine prompt description must remain visibly bound after the wait.");
        Assert(relationship.IsVisible && relationship.Text == context.Title,
            "The compact response row must preserve its complete actual effect source and recipient title.");
        Fits(heading); Fits(relationship); Fits(description);
        var bannerBounds = Bounds(banner, root); var descriptionBounds = Bounds(description, root);
        var relationshipBounds = Bounds(relationship, root);
        Assert(new Rect(root.RenderSize).Contains(relationshipBounds) && !relationshipBounds.IntersectsWith(bannerBounds) &&
            !relationshipBounds.IntersectsWith(descriptionBounds),
            "The compact native response row clipped its relationship title or allowed the response status to overpaint it.");
        Assert(new Rect(root.RenderSize).Contains(bannerBounds) && new Rect(root.RenderSize).Contains(descriptionBounds) &&
            !bannerBounds.IntersectsWith(descriptionBounds), "The persistent native heading and prompt description clipped or overlapped each other.");
        foreach (var element in Find<Button>(root).Where(button => BattleFeedbackLayer.GetSeatAnchor(button) >= 0)
            .Cast<FrameworkElement>().Concat(new[]
            {
                (FrameworkElement)host.Window.FindName("HandViewport"),
                (FrameworkElement)host.Window.FindName("PrimaryActionButtons"),
                (FrameworkElement)host.Window.FindName("TablePlaysViewport")
            }).Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0))
        {
            var bounds = Bounds(element, root);
            Assert(!bannerBounds.IntersectsWith(bounds) && !descriptionBounds.IntersectsWith(bounds),
                "The added native response heading or preserved engine explanation obscured an actual portrait, public play, hand or action control.");
        }
        var buttons = Find<Button>(root).Where(button => BattleFeedbackLayer.GetSeatAnchor(button) >= 0).ToArray();
        Assert(buttons.Length == 4, "The native response fixture lost a real player portrait.");
        foreach (var button in buttons)
        {
            var seat = (SeatViewModel)button.DataContext;
            var badge = Find<Border>(button).Single(border => border.Name == "ResponseStatusBadge");
            var rails = Find<Border>(button).Single(border => border.Name == "SeatResponseOutline");
            var expected = seat.Seat == own.PlayerSeat;
            Assert(badge.IsVisible == expected && rails.IsVisible == expected &&
                seat.ResponseStatusText == (expected ? status : string.Empty) &&
                seat.IsCurrent == (seat.Seat == Engine(vm).CreateSnapshot(0).CurrentSeat),
                "Only the current viewer-safe responder may retain a status; the active turn and original effect roles must remain distinct.");
            if (!expected) continue;
            var text = Find<TextBlock>(badge).Single();
            Assert(text.Text == status && !badge.IsHitTestVisible && !rails.IsHitTestVisible &&
                rails.BorderThickness == new Thickness(1, 0, 1, 0), "Persistent responder decoration must use the declared native status and avoid blocking input or filling the portrait.");
            Fits(text);
            var badgeBounds = Bounds(badge, button);
            Assert(new Rect(button.RenderSize).Contains(badgeBounds), "The response caption left its unchanged native portrait bounds.");
            foreach (var protectedElement in Find<FrameworkElement>(button).Where(element => element.IsVisible &&
                element.Name is "SeatVerticalName" or "SingleGeneralTitle" or "SeatHealthMeter" or "DecisionRoleBadge" or "TargetBadge"))
                Assert(!badgeBounds.IntersectsWith(Bounds(protectedElement, button)),
                    "The persistent responder badge obscured the actual name, health, target or original decision role.");
            var turn = Find<Border>(button).Single(border => border.Name == "TurnLine");
            if (turn.IsVisible)
                Assert(badgeBounds.Bottom <= Bounds(turn, button).Bottom - turn.BorderThickness.Bottom + .01,
                    "The current turn line overpainted the persistent responder badge.");
        }
    }

    private static void AssertCleared(NativeHost host)
    {
        DecisionContextChecks.VerifyLive(host.Model);
        Assert(host.Model.CurrentDecisionContext?.HasResponseStatus != true &&
            !((Border)host.Window.FindName("ResponseStatusBanner")).IsVisible &&
            host.Model.Seats.All(seat => !seat.HasResponseStatus && seat.ResponseStatusText.Length == 0) &&
            Find<Border>(host.Root).Where(border => border.Name is "ResponseStatusBadge" or "SeatResponseOutline")
                .All(border => !border.IsVisible),
            "A completed, declined or replaced real prompt retained a stale persistent response actor or headline.");
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToVisual(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static void Fits(TextBlock text)
    {
        // FrameworkElement.DesiredSize includes Margin; the TextBlock's actual
        // arranged content size and RenderSize exclude that outer spacing.
        var desiredContent = new Size(Math.Max(0, text.DesiredSize.Width - text.Margin.Left - text.Margin.Right),
            Math.Max(0, text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom));
        var diagnostic = $"{text.Name}: actual={text.ActualWidth:F2}x{text.ActualHeight:F2}, " +
            $"desired={text.DesiredSize.Width:F2}x{text.DesiredSize.Height:F2}, margin={text.Margin}, " +
            $"desired content={desiredContent.Width:F2}x{desiredContent.Height:F2}";
        Console.WriteLine("Native response text measurement: " + diagnostic + ".");
        Assert(text.ActualWidth + .01 >= desiredContent.Width && text.ActualHeight + .01 >= desiredContent.Height,
            "Native response text was measured larger than its visible arranged content area: " + diagnostic);
    }
    private static MatchBoundary Boundary(GameEngine game) => new(game.Revision, game.AcceptedCommands.Count,
        game.Events.Count, game.CardMovements.Count, Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray());
    private static bool Same(GameEngine game, MatchBoundary boundary) => game.Revision == boundary.Revision &&
        game.AcceptedCommands.Count == boundary.Commands && game.Events.Count == boundary.Events && game.CardMovements.Count == boundary.Movements &&
        Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == boundary.Views[seat]);
    private sealed record MatchBoundary(long Revision, int Commands, int Events, int Movements, string[] Views);

    internal sealed class NativeHost : IDisposable
    {
        public MainViewModel Model { get; }
        public MainWindow Window { get; }
        public FrameworkElement Root { get; }
        public BattleFeedbackLayer Layer { get; }
        public FrameClock Clock { get; } = new();
        private readonly nint _handle;
        private const int ExtendedStyle = -20;
        private const int NoActivate = 0x08000000;
        public NativeHost(MainViewModel model)
        {
            Model = model; model.IsSoundEnabled = false; model.IsMusicEnabled = false;
            Window = new MainWindow(model)
            {
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000, Top = -30000, Width = 1120, Height = 820
            };
            Root = (FrameworkElement)Window.Content;
            Layer = (BattleFeedbackLayer)Window.FindName("BattleFeedback"); Layer.Clock = Clock;
            _handle = new WindowInteropHelper(Window).EnsureHandle();
            // ShowActivated alone does not prevent later native activation.
            // Match the existing keyboard fixtures on this owned HWND before
            // loading its real WPF tree; logical focus remains testable.
            SetWindowLong(_handle, ExtendedStyle, GetWindowLong(_handle, ExtendedStyle) | NoActivate);
            Assert((GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                "The shared hidden native fixture could not disable activation on its owned HWND.");
            var previousParent = SetParent(_handle, new nint(-3));
            Assert(previousParent != 0 || Marshal.GetLastPInvokeError() == 0,
                "The native response fixture could not attach its owned message-only HWND.");
            Window.Show(); ShowWindow(_handle, 0);
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert(Window.IsLoaded && Window.IsVisible && !IsWindowVisible(_handle) && GetForegroundWindow() != _handle &&
                (GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                $"The actual WPF response fixture must remain loaded without becoming a visible or foreground desktop window: " +
                $"loaded={Window.IsLoaded}, WPF visible={Window.IsVisible}, native visible={IsWindowVisible(_handle)}, " +
                $"owns foreground={GetForegroundWindow() == _handle}, no activate={(GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0}.");
        }
        public void Capture(string output, string label, int width)
        {
            var height = width == 1120 ? 740 : 1080;
            var outputSize = new Size(width, height);
            // A loaded Window can arrange its content again at Render priority.
            // Pin the real root rather than relying on Render's earlier Arrange;
            // SizeToContent also gives the owned host room for that exact root.
            Root.Width = width; Root.Height = height;
            Window.SizeToContent = SizeToContent.WidthAndHeight;
            Window.UpdateLayout();
            Render(Root, width, height, Path.Combine(output, label + $"-{width}.png"));
            var diagnostic = $"{label}: output={outputSize}, actual root={Root.RenderSize}, " +
                $"actual window={Window.ActualWidth:F2}x{Window.ActualHeight:F2}";
            Console.WriteLine("Native response output measurement: " + diagnostic + ".");
            Assert(Root.RenderSize == outputSize,
                "The loaded native parent replaced the exact PNG layout size: " + diagnostic);
            var outputBounds = new Rect(outputSize);
            foreach (var element in Find<FrameworkElement>(Root).Where(element => element.IsVisible &&
                element.ActualWidth > 0 && element.ActualHeight > 0 &&
                (element.Name is "ResponseStatusBanner" or "DecisionDescriptionText" or "HandViewport" or "PrimaryActionButtons" ||
                 element is Button button && BattleFeedbackLayer.GetSeatAnchor(button) >= 0)))
                Assert(outputBounds.Contains(Bounds(element, Root)),
                    $"The actual PNG clips native {element.Name}, bounds={Bounds(element, Root)}; " + diagnostic);
            Assert(Window.IsLoaded && Window.IsVisible && Root.IsVisible && !IsWindowVisible(_handle) && GetForegroundWindow() != _handle &&
                (GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                "Rendering native response status activated or exposed the owned test window.");
        }
        public void Dispose() { Window.Content = null; Window.DataContext = null; Window.Close(); }
    }
    internal sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message);
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint handle, int index, int value);
}
