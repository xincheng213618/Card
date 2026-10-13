using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;
using NativeHost = NativeResponseStatusChecks.NativeHost;

internal static class NativeDrawFeedbackChecks
{
    private static readonly MethodInfo RefreshHand = typeof(CardLiftSurface).GetMethod("RefreshMotion", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void NativeDraw(string output)
    {
        var errors = new BindingListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            var (prepared, registry) = PopulationTopReorderScenario.Start(players: 4, remaining: 32, draw: 2, pausePrepare: true);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
            using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null); Assert(!model.HasSaveError, model.SaveStatus);
            model.IsMotionEnabled = true; model.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
            using var host = new NativeHost(model);
            host.Capture(output, "draw-native-before", 1120); ShareClock(host);
            var pending = Boundary(Engine(model));
            Assert(Same(Engine(model), Boundary(prepared)) && host.Layer.ActiveEffectCount == 0,
                "The cold native draw setup changed four safe views or replayed its initial deal.");
            var (own, cursor, commands, steps) = DrawOwn(host);
            var ownBoundary = Boundary(Engine(model));
            Frames(host, output, "own", own, ownBoundary, record: RecordMotion);
            WriteEvidence(host, output, "draw-native-own-events", cursor, commands, steps, ownBoundary);
            var (other, otherCursor, otherCommands, otherSteps) = DrawNextPlayer(host);
            var otherBoundary = Boundary(Engine(model));
            Frames(host, output, "other", other, otherBoundary, record: false);
            WriteEvidence(host, output, "draw-native-other-events", otherCursor, otherCommands, otherSteps, otherBoundary);
            InterruptAndRestore(host, store, pending, output);
            Assert(errors.Errors.Count == 0, "Native draw binding diagnostics: " + string.Join(Environment.NewLine, errors.Errors));
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
    }

    private static (BattleCue Cue, int Cursor, int Commands, int Steps) DrawOwn(NativeHost host)
    {
        var model = host.Model; var game = Engine(model); var cursor = game.Events.Count; var movements = game.CardMovements.Count;
        var commands = game.AcceptedCommands.Count; var before = game.CreateSnapshot(0).Players[0].HandCount;
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Assert(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } && prompt.SkillPrompt?.SkillId == PopulationTopReorderScenario.Skill,
            "The fixed seed31 native draw must begin at its actual optional preparation skill prompt.");
        var skip = model.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        model.SelectSkillChoiceCommand.Execute(skip);
        Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == prompt.PromptId && answer.Choice == skip.Id,
            "The native preparation skip did not submit exactly its current public choice once.");
        model.SelectSkillChoiceCommand.Execute(skip);
        Assert(game.AcceptedCommands.Count == commands + 1, "A stale preparation skip submitted a second native command.");
        var steps = 0;
        for (; steps < 4 && DrawEvents(game, cursor, 0).Length == 0; steps++)
        {
            Assert(model.CanStepAi && game.CreateSnapshot(0).PendingDecision is null,
                "The fixed preparation skip required an unexpected extra human choice before drawing.");
            model.StepAiCommand.Execute(null);
        }
        Assert(game.AcceptedCommands.Count == commands + 1 + steps && game.CreateSnapshot(0).Players[0].HandCount == before + 2 &&
            game.CreateSnapshot(0).Players.Count == 4 && game.CardMovements.Skip(movements).Count(move =>
                move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.Draw) == 2,
            "The real normal draw did not commit its two physical gains immediately with the recorded native command count.");
        return (AssertCue(host, cursor, 0, 2), cursor, commands, steps);
    }

    private static (BattleCue Cue, int Cursor, int Commands, int Steps) DrawNextPlayer(NativeHost host)
    {
        var model = host.Model; var game = Engine(model); var cursor = game.Events.Count; var commands = game.AcceptedCommands.Count;
        var before = game.CreateSnapshot(0).Players[1].HandCount;
        var steps = 0;
        for (; steps < 12 && DrawEvents(game, cursor, 1).Length == 0; steps++)
        {
            var count = game.AcceptedCommands.Count;
            if (model.CanEndTurn) model.EndTurnCommand.Execute(null);
            else if (model.IsDiscardSelectionPending) ResolveDiscard(model);
            else
            {
                Assert(model.CanStepAi && game.CreateSnapshot(0).PendingDecision is null,
                    "The fixed all-Dodge deck stopped at an unexpected choice before AI1's normal draw.");
                model.StepAiCommand.Execute(null);
            }
            Assert(game.AcceptedCommands.Count == count + 1, "The bounded next-player preparation did not submit exactly one real native command.");
        }
        Assert(steps < 12 && game.CreateSnapshot(0).CurrentSeat == 1 && game.CreateSnapshot(0).Players[1].HandCount == before + 2 &&
            game.AcceptedCommands.Count == commands + steps,
            "The next actual player did not complete its fixed two-card draw before playing.");
        return (AssertCue(host, cursor, 1, 2), cursor, commands, steps);
    }

    private static BattleCue AssertCue(NativeHost host, int cursor, int seat, int count)
    {
        var game = Engine(host.Model); var events = DrawEvents(game, cursor, seat);
        Assert(events.Length == count && events.Select(item => item.Revision).Distinct().Count() == 1,
            "The native draw evidence must be exactly its real same-commit draw movements.");
        var cue = host.Model.BattleCues.Single(item => item.Kind == BattleCueKind.Draw && item.Sequence == events[^1].Sequence);
        Assert(cue.HasDrawCards && cue.DrawCount == count && cue.Revision == events[^1].Revision && cue.SourceSeat == seat &&
            cue.TargetSeats.SequenceEqual(new[] { seat }) && cue.Label == $"摸牌 +{count}" && !cue.IsInquiry &&
            cue.PublicCard is null && cue.PublicCardId is null && cue.CardKind is null && cue.JudgmentCard is null &&
            cue.WeaponKind is null && cue.RequiredResponseCardKind is null && cue.HpDelta == 0,
            "The actual native draw did not aggregate its public count or leaked a private entity/face/result field.");
        var actualIds = events.Select(item => ((CardMovedEvent)item.Payload).CardId).ToArray();
        foreach (var viewer in Enumerable.Range(0, 4).Where(viewer => viewer != seat))
            Assert(game.CreateSnapshot(viewer).Players[seat].Hand.Count == 0,
                "A real count-only draw revealed the owner's actual private cards to another safe viewer.");
        Assert(host.Model.RecentPlays.All(play => play.PublicCardId is not { } id || !actualIds.Contains(id)) &&
            host.Model.BattleCues.All(item => item.PublicCardId is not { } id || !actualIds.Contains(id)),
            "A private incoming draw card became an invented public play or card-flight entity.");
        var replay = BattleCueProjector.Project(game.Events.Skip(cursor), game.CreateSnapshot(0)).Where(item => item.Kind == BattleCueKind.Draw && item.SourceSeat == seat).ToArray();
        Assert(replay.Length == 1 && replay[0].Sequence == cue.Sequence && replay[0].DrawCount == count,
            "The actual public draw projection duplicated the same movement batch or changed its aggregate count.");
        Console.WriteLine($"Native draw committed: seed31 owner{seat}, actual DrawPile->Hand count{count}, last public sequence{cue.Sequence}, revision{cue.Revision}, " +
            $"hand count{game.CreateSnapshot(0).Players[seat].HandCount}; four private safe projections, no cue IDs/faces.");
        return cue;
    }

    private static EventEnvelope[] DrawEvents(GameEngine game, int cursor, int seat) => game.Events.Skip(cursor).Where(item =>
        item.Payload is CardMovedEvent move && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(seat) && move.Reason == CardMoveReasons.Draw).ToArray();

    private static void Frames(NativeHost host, string output, string label, BattleCue cue, SafeBoundary boundary, bool record)
    {
        var game = Engine(host.Model); var start = host.Clock.GetTimestamp(); var movie = record ? new GifBitmapEncoder() : null; var movieIndex = 0;
        Point? first = null; double? firstDistance = null;
        foreach (var sample in new[] { .04, .22, .32, .44, .90, 1.96 })
        {
            AdvanceTo(sample);
            if (sample == 1.96) continue;
            host.Capture(output, $"draw-native-{label}-{sample * 1000:0000}ms", 1120);
            var back = AssertFrame(host, cue, sample);
            var destination = Destination(host, cue); var pile = Center(Bounds(host.Layer.DrawPileAnchor!, host.Layer));
            if (sample == .04)
            {
                first = Center(back); firstDistance = (first.Value - destination).Length;
                Assert((first.Value - pile).Length < firstDistance,
                    $"The first real draw backs did not begin at the real pile rather than their actual destination: pile={pile}, back={back}, destination={destination}.");
            }
            if (sample == .22) Assert(firstDistance is not null && (Center(back) - destination).Length < firstDistance,
                "The actual draw backs did not move from their real pile toward the corresponding hand or inward portrait edge.");
            if (sample == .44) Assert((Center(back) - destination).Length < 24 && first is not null && (Center(back) - first.Value).Length > 25,
                $"The arrived actual draw backs missed their corresponding hand or inward portrait edge, or remained static at the pile: back={back}, destination={destination}.");
            Assert(Same(game, boundary), "Rendering native draw changed an accepted command, movement, event or any of four player-safe views.");
            if (sample == .22)
            {
                host.Capture(output, $"draw-native-{label}-readable", 1920); AssertFrame(host, cue, sample);
                Assert(Same(game, boundary), "Changing native draw output size changed hidden cards or command state.");
            }
        }
        Assert(!ActiveDraw(host.Layer) && Same(game, boundary), "Expired draw playback retained its own old flight or changed a safe view.");
        if (movie is not null)
        {
            var path = Path.Combine(output, "draw-native-own.gif");
            using (var file = File.Create(path)) movie.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var stream = File.OpenRead(path);
            var decoded = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(decoded.Frames.Count == 50 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                frame.Metadata is BitmapMetadata metadata && (ushort)metadata.GetQuery("/grctlext/Delay") == 4),
                "The native draw film lost its 50 forward-clock frames, compact full root or 40ms timing.");
            Assert(!Pixels(decoded.Frames[1]).SequenceEqual(Pixels(decoded.Frames[6])) && !Pixels(decoded.Frames[11]).SequenceEqual(Pixels(decoded.Frames[30])),
                "The native draw movie repeated static pile/arrival/expired frames.");
        }

        void AdvanceTo(double seconds)
        {
            if (movie is not null)
                while (movieIndex < 50 && movieIndex * .04 <= seconds + 1e-8)
                {
                    MoveTo(movieIndex * .04); SetSize(host, 1120);
                    var native = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); native.Render(host.Root);
                    var compact = new RenderTargetBitmap(728, 481, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual();
                    using (var dc = drawing.RenderOpen()) dc.DrawImage(native, new Rect(0, 0, 728, 481));
                    compact.Render(drawing); movie.Frames.Add(BitmapFrame.Create(compact)); movieIndex++;
                }
            MoveTo(seconds);
        }
        void MoveTo(double seconds)
        {
            var now = host.Clock.GetElapsedTime(start, host.Clock.GetTimestamp()).TotalSeconds;
            Assert(seconds + 1e-8 >= now, "Actual native draw recording moved its controlled clock backwards.");
            host.Clock.Advance(Math.Max(0, seconds - now)); host.Layer.InvalidateVisual(); Flush(host);
        }
    }

    private static Rect AssertFrame(NativeHost host, BattleCue cue, double seconds)
    {
        var anchor = host.Layer.DrawPileAnchor;
        Assert(anchor is { IsLoaded: true, IsVisible: true } && ReferenceEquals(anchor, host.Window.FindName("DrawPileVisual")) &&
            anchor.ActualWidth > 0 && anchor.ActualHeight > 0 && Bounds(anchor, host.Layer).Width < 80,
            "The native draw did not bind a visible real stacked-card pile, independently of the deck counter.");
        var drawing = VisualTreeHelper.GetDrawing(host.Layer); var viewport = new Rect(host.Layer.RenderSize);
        Assert(!Images(drawing).Any(), "A native draw-back flight exposed an actual private card/rank/suit artwork.");
        var backs = Shapes(drawing, Matrix.Identity, 1).Where(item => item.Opacity > .02 && item.Shape.Brush is SolidColorBrush brush &&
            brush.Color == Color.FromRgb(48, 39, 29)).ToArray();
        var captions = Shapes(drawing, Matrix.Identity, 1).Where(item => item.Opacity > .02 && item.Shape.Brush is SolidColorBrush brush &&
            brush.Color == Color.FromRgb(64, 50, 34)).ToArray();
        var text = string.Concat(Glyphs(drawing).Select(item => item.GlyphRun.Characters is { } chars ? new string(chars.ToArray()) : string.Empty));
        if (seconds < .85)
            Assert(backs.Length > 0 && backs.Length <= cue.DrawCount && backs.All(item => viewport.Contains(item.Bounds)) &&
                captions.Length > 0 && captions.All(item => item.Bounds.Width >= 150 && item.Bounds.Height >= 62) &&
                text.Contains(cue.Label, StringComparison.Ordinal) && text.Contains(cue.ActorName, StringComparison.Ordinal),
                $"Native draw {seconds:F2}s lost its count-only backs/public owner caption or clipped them: backs={backs.Length}, text={text}.");
        else Assert(backs.Length == 0 && !ActiveDraw(host.Layer), "Expired native draw kept an old count/back animation.");
        var readouts = Find<FrameworkElement>(host.Root).Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0 &&
            (element is ButtonBase or TextBlock || element is HealthMeter) && !ReferenceEquals(element, anchor) && !element.IsDescendantOf(anchor!))
            .Select(element => (Element: element, Bounds: Bounds(element, host.Layer))).ToArray();
        foreach (var back in backs)
            foreach (var readout in readouts)
            {
                var overlap = Geometry.Combine(back.VisibleGeometry, new RectangleGeometry(readout.Bounds), GeometryCombineMode.Intersect, null).GetArea();
                Assert(overlap == 0,
                    $"A visible native draw back covered an actual command/readout/HP: time={seconds:F2}, " +
                    $"element={readout.Element.Name}/{readout.Element.GetType().Name}, readout={readout.Bounds}, back={back.Bounds}, overlap={overlap:F6}.");
            }
        var obstacles = Find<FrameworkElement>(host.Root).Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0 &&
            (element is ButtonBase or TextBlock || element is HealthMeter)).Select(element => Bounds(element, host.Layer))
            .Append(Bounds(anchor!, host.Layer)).ToArray();
        foreach (var panel in Shapes(drawing, Matrix.Identity, 1).Where(item => item.Opacity > .02 && item.Shape.Brush is SolidColorBrush brush &&
            (brush.Color == Color.FromRgb(19, 31, 34) || brush.Color == Color.FromRgb(64, 50, 34))))
            Assert(viewport.Contains(panel.Bounds) && obstacles.All(obstacle => !panel.Bounds.IntersectsWith(obstacle)),
                $"A native draw caption obscured or clipped a name/HP/command/portrait/readout/pile: bounds={panel.Bounds}, viewport={viewport}.");
        return backs.Length == 0 ? Rect.Empty : backs.Select(item => item.Bounds).Aggregate((left, right) => Rect.Union(left, right));
    }

    private static void InterruptAndRestore(NativeHost host, MemorySaveStore store, SafeBoundary pending, string output)
    {
        host.Model.LoadManualGameCommand.Execute(null); Flush(host); ShareClock(host);
        Assert(!host.Model.HasSaveError && Same(Engine(host.Model), pending) && host.Layer.ActiveEffectCount == 0,
            "Loading the actual pre-draw checkpoint replayed its initial cards or changed safe state.");
        DrawOwn(host); var boundary = Boundary(Engine(host.Model));
        host.Clock.Advance(.04); host.Capture(output, "draw-native-interrupted-before", 1120);
        Assert(ActiveDraw(host.Layer), "The actual interrupted draw did not have a pending back flight.");
        host.Model.IsMotionEnabled = false; Flush(host);
        Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
            "Disabling motion retained the committed draw-back flight or frame timer.");
        host.Model.IsMotionEnabled = true; host.Clock.Advance(.50); host.Capture(output, "draw-native-motion-on-no-replay", 1120);
        Assert(!ActiveDraw(host.Layer) && host.Layer.ActiveEffectCount == 0 && Same(Engine(host.Model), boundary),
            "Restoring motion replayed old private arrivals/count backs or changed their actual committed gains.");
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, Engine(host.Model).CreateCheckpoint()));
        host.Model.LoadManualGameCommand.Execute(null); Flush(host);
        host.Capture(output, "draw-native-loaded-no-replay", 1120);
        Assert(!host.Model.HasSaveError && host.Layer.ActiveEffectCount == 0 && Same(Engine(host.Model), boundary),
            "Loading the actual completed draw replayed history or changed a safe view/cost boundary.");
        host.Window.Content = null; host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert(!host.Root.IsLoaded && host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
            "Unloading the real draw root retained its layer or timer.");
        host.Window.Content = host.Root; host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.Clock.Advance(.40); host.Capture(output, "draw-native-reattached-no-replay", 1120);
        Assert(host.Root.IsLoaded && host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning && Same(Engine(host.Model), boundary),
            "Reattaching the real root replayed historical draw backs or changed a safe snapshot.");
    }

    private static void ShareClock(NativeHost host)
    { Find<HandPanel>(host.Root).Single().Clock = host.Clock; foreach (var surface in Find<CardLiftSurface>(host.Root)) surface.Clock = host.Clock; }
    private static void Flush(NativeHost host)
    {
        host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); host.Root.UpdateLayout();
        foreach (var surface in Find<CardLiftSurface>(host.Root)) RefreshHand.Invoke(surface, null);
        host.Layer.InvalidateVisual(); host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
    private static void SetSize(NativeHost host, int width)
    {
        host.Root.Width = width; host.Root.Height = width == 1120 ? 740 : 1080;
        host.Window.SizeToContent = SizeToContent.WidthAndHeight; host.Window.UpdateLayout(); Flush(host);
        Assert(host.Root.RenderSize == new Size(host.Root.Width, host.Root.Height), "The native draw camera used a cropped root.");
    }
    private static bool ActiveDraw(BattleFeedbackLayer layer) => ((System.Collections.IEnumerable)typeof(BattleFeedbackLayer)
        .GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!).Cast<object>()
        .Any(item => ((BattleCue)item.GetType().GetProperty("Cue")!.GetValue(item)!).Kind == BattleCueKind.Draw);
    private static Rect Bounds(FrameworkElement element, Visual ancestor) => element.TransformToVisual(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static Rect SeatBounds(NativeHost host, int seat) => Bounds(Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == seat), host.Layer);
    private static Point Destination(NativeHost host, BattleCue cue)
    {
        if (cue.SourceSeat == host.Layer.HandOwnerSeat && host.Layer.HandRoot is { } hand)
        {
            var bounds = Bounds(hand, host.Layer);
            if (!bounds.IsEmpty) return new Point(bounds.Left + bounds.Width / 2, bounds.Top + 7);
        }
        var portrait = SeatBounds(host, cue.SourceSeat);
        var center = Center(portrait);
        return new Point(center.X > host.Layer.ActualWidth / 2 ? portrait.Left - 14 : portrait.Right + 14, center.Y);
    }
    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static IEnumerable<(GeometryDrawing Shape, Rect Bounds, double Opacity, Geometry VisibleGeometry)> Shapes(Drawing? drawing, Matrix transform, double opacity, Geometry? clip = null)
    {
        if (drawing is GeometryDrawing shape)
        {
            var geometry = shape.Geometry.Clone(); var local = geometry.Transform?.Value ?? Matrix.Identity; local.Append(transform);
            geometry.Transform = new MatrixTransform(local);
            var visible = clip is null ? geometry : Geometry.Combine(geometry, clip, GeometryCombineMode.Intersect, null);
            if (!visible.Bounds.IsEmpty && visible.GetArea() > .001) yield return (shape, visible.Bounds, opacity, visible);
        }
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            var nextClip = clip;
            if (group.ClipGeometry is { } groupClip)
            {
                var transformed = groupClip.Clone(); var local = transformed.Transform?.Value ?? Matrix.Identity; local.Append(next);
                transformed.Transform = new MatrixTransform(local);
                nextClip = clip is null ? transformed : Geometry.Combine(clip, transformed, GeometryCombineMode.Intersect, null);
            }
            foreach (var child in group.Children) foreach (var item in Shapes(child, next, opacity * group.Opacity, nextClip)) yield return item;
        }
    }
    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    { if (drawing is ImageDrawing image) yield return image; if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var item in Images(child)) yield return item; }
    private static IEnumerable<GlyphRunDrawing> Glyphs(Drawing? drawing)
    { if (drawing is GlyphRunDrawing glyph) yield return glyph; if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var item in Glyphs(child)) yield return item; }
    private sealed record SafeBoundary(long Revision, int Commands, int Events, int Movements, string[] Views);
    private static SafeBoundary Boundary(GameEngine game) => new(game.Revision, game.AcceptedCommands.Count, game.Events.Count, game.CardMovements.Count,
        Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray());
    private static bool Same(GameEngine game, SafeBoundary before) => game.Revision == before.Revision && game.AcceptedCommands.Count == before.Commands &&
        game.Events.Count == before.Events && game.CardMovements.Count == before.Movements &&
        Enumerable.Range(0, before.Views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == before.Views[seat]);
    private static byte[] Pixels(BitmapSource source)
    { var bitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0); var result = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(result, bitmap.PixelWidth * 4, 0); return result; }
    private static void WriteEvidence(NativeHost host, string output, string name, int cursor, int commands, int steps, SafeBoundary boundary)
    {
        var game = Engine(host.Model);
        // The trusted movement facts contain hidden physical faces. Strip
        // them before export, just as the production projection does.
        var quantities = game.Events.Skip(cursor).Where(item => item.Payload is CardMovedEvent move &&
            move.From == CardLocation.DrawPile && move.To is { Zone: CardZoneKind.Hand, OwnerSeat: not null } && move.Reason == CardMoveReasons.Draw)
            .Select(item => new { item.Sequence, item.Revision, OwnerSeat = ((CardMovedEvent)item.Payload).To.OwnerSeat!.Value }).ToArray();
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
        {
            game.Seed, CommandsBefore = commands, CommandsAfter = game.AcceptedCommands.Count, NativePreparationSteps = steps,
            ActualDraws = quantities.GroupBy(item => (item.Revision, item.OwnerSeat)).Select(group => new
                { Sequence = group.Max(item => item.Sequence), group.Key.Revision, group.Key.OwnerSeat, DrawCount = group.Count() }).ToArray(),
            DrawCues = host.Model.BattleCues.Where(cue => cue.Kind == BattleCueKind.Draw && quantities.Any(item => item.Sequence == cue.Sequence))
                .Select(cue => new { cue.Sequence, cue.Revision, OwnerSeat = cue.SourceSeat, cue.DrawCount }).ToArray(),
            SafeViewCount = boundary.Views.Length, PlaybackSafeViewsAndCommandsUnchanged = Same(game, boundary)
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class BindingListener : TraceListener
    {
        internal List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message); }
    }
}
