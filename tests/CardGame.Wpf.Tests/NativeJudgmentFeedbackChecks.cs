using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;
using NativeHost = NativeResponseStatusChecks.NativeHost;

internal static class NativeJudgmentFeedbackChecks
{
    private static readonly double RevealTime = Timing("JudgmentRevealTime");
    private static readonly double Duration = Timing("JudgmentDuration");
    public static void NativeJudgment(string output)
    {
        var errors = new BindingListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            var (prepared, registry) = DelayedTrickScenario.Create();
            var indulgenceId = Prepare(prepared);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
            using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Assert(!model.HasSaveError, model.SaveStatus);
            model.IsMotionEnabled = true; model.SelectedPlaybackSpeed = PlaybackSpeed.Normal;
            using var host = new NativeHost(model);
            // Only observe presentation requests. The owned hidden window's
            // inactive controller must not share gate ownership with this probe.
            ((IDisposable)typeof(CardGame.Wpf.MainWindow).GetField("_audio", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host.Window)!).Dispose();
            var sounds = new List<(double Seconds, GameSound Sound)>();
            host.Layer.SetSoundsEnabled(true);
            host.Layer.SoundsRequested += (_, args) => sounds.AddRange(args.Sounds.Select(sound =>
                (host.Clock.GetElapsedTime(0, host.Clock.GetTimestamp()).TotalSeconds, sound)));
            host.Capture(output, "judgment-native-before-pass", 1120);
            Assert(host.Layer.ActiveEffectCount == 0 && Same(Engine(model), Boundary(prepared)),
                "Cold native delayed-card loading replayed history or changed a player-safe view.");
            var pendingBoundary = Boundary(Engine(model));
            var (resolved, cue, commands, advances, cursor) = Resolve(host, indulgenceId);
            var boundary = Boundary(Engine(model));
            var start = host.Clock.GetTimestamp();
            var soundStart = sounds.Count(item => item.Sound == GameSound.Card);
            Assert(soundStart == 0, "The real judgment sounded at request/commit instead of its reveal beat.");
            var movie = RecordMotion ? new GifBitmapEncoder() : null;
            var movieIndex = 0;
            foreach (var sample in new[] { .04, .14, .26, .42, .80, 1.60, 1.96 })
            {
                AdvanceTo(sample);
                if (sample == 1.96) continue;
                host.Capture(output, $"judgment-native-{sample * 1000:0000}ms", 1120);
                AssertFrame(host, cue, sample);
                Assert(Same(Engine(model), boundary), "Judgment playback changed accepted commands, physical costs, events or one of four safe views.");
                Assert(sounds.Count(item => item.Sound == GameSound.Card) == (sample < .18 ? 0 : 1),
                    $"The actual judgment Card request at {sample:F2}s did not follow its reveal exactly once.");
                if (sample == .26)
                {
                    host.Capture(output, "judgment-native-revealed-readable", 1920);
                    AssertFrame(host, cue, sample);
                    Assert(Same(Engine(model), boundary), "The second native judgment resolution changed public or private state.");
                }
            }
            Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
                "The actual judgment panel did not expire with its presentation timer.");
            if (movie is not null)
            {
                var path = Path.Combine(output, "judgment-native-reveal.gif");
                using (var file = File.Create(path)) movie.Save(file);
                BattleEffectsChecks.SetGifTiming(path, 4);
                using var stream = File.OpenRead(path);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert(decoder.Frames.Count == 50 && decoder.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                    frame.Metadata is BitmapMetadata metadata && (ushort)metadata.GetQuery("/grctlext/Delay") == 4),
                    "The native judgment film lost its fixed output or 50 forward-clock frames at 40ms.");
                Assert(!Pixels(decoder.Frames[1]).SequenceEqual(Pixels(decoder.Frames[7])) &&
                    !Pixels(decoder.Frames[20]).SequenceEqual(Pixels(decoder.Frames[40])),
                    "The native judgment recording repeated static back/front/expired frames.");
            }
            File.WriteAllText(Path.Combine(output, "judgment-native-events.json"), JsonSerializer.Serialize(new
            {
                Engine(model).Seed, IndulgenceCardId = indulgenceId, CommandsBefore = commands,
                CommandsAfter = Engine(model).AcceptedCommands.Count, NativeAdvances = advances,
                Events = Engine(model).Events.Skip(cursor).Select(item => new { item.Sequence, Kind = item.Payload.GetType().Name, Payload = (object)item.Payload }).ToArray(),
                SoundRequests = sounds.Select(item => new { item.Seconds, Sound = item.Sound.ToString() }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            InterruptAndLoad(host, output, indulgenceId, pendingBoundary, boundary, sounds);
            Assert(errors.Errors.Count == 0, "Native judgment binding diagnostics: " + string.Join(Environment.NewLine, errors.Errors));
            Console.WriteLine($"Native judgment: fixed seed31 Indulgence{indulgenceId}, target{resolved.TargetSeat}, actual judgment" +
                $"{resolved.CardId}/{resolved.CardKind}/{resolved.Suit}/{resolved.Rank}, ID{resolved.ResolutionId}, safe={resolved.Succeeded}; " +
                $"commands{commands}->{boundary.Commands} (pass + {advances} native advances), flip180ms, exact result='{cue.Detail}', " +
                "one reveal sound request, four safe views unchanged; no speaker recording.");

            void AdvanceTo(double seconds)
            {
                if (movie is not null)
                    while (movieIndex < 50 && movieIndex * .04 <= seconds + 1e-8)
                    {
                        MoveTo(movieIndex * .04); Flush(host); SetSize(host, 1120);
                        var native = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32); native.Render(host.Root);
                        var frame = new RenderTargetBitmap(728, 481, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual();
                        using (var dc = visual.RenderOpen()) dc.DrawImage(native, new Rect(0, 0, 728, 481));
                        frame.Render(visual); movie.Frames.Add(BitmapFrame.Create(frame)); movieIndex++;
                    }
                MoveTo(seconds);
            }
            void MoveTo(double seconds)
            {
                var current = host.Clock.GetElapsedTime(start, host.Clock.GetTimestamp()).TotalSeconds;
                Assert(seconds + 1e-8 >= current, "Native judgment recording moved its controlled clock backwards.");
                host.Clock.Advance(Math.Max(0, seconds - current)); BattleAudioBeatChecks.Tick(host.Layer); host.Layer.InvalidateVisual();
            }
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
    }

    private static int Prepare(GameEngine game)
    {
        var action = game.GetHumanLegalActions().First(action => action.CardId is { } id && action.ConversionSource is null &&
            action.TargetSeats.SequenceEqual(new[] { 1 }) && game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == id).Kind == CardKind.Indulgence);
        var id = action.CardId!.Value;
        DelayedTrickScenario.Accept(game, new PlayCardCommand(0, id, action.TargetSeats, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId, action.PlayedCardKind));
        Assert(game.CreateSnapshot(0).Players[1].Judgment.Any(card => card.Id == id), "The actual Indulgence did not enter AI1's judgment zone.");
        var advances = 0;
        for (; advances < 32; advances++)
        {
            var view = game.CreateSnapshot(0);
            if (view.PendingDecision is { Kind: DecisionKind.Nullification, PlayerSeat: 0, IncomingCard: CardKind.Indulgence } &&
                game.Events.Select(item => item.Payload).OfType<NullificationRequestedEvent>().LastOrDefault() is { EffectCardId: var effect } && effect == id)
                break;
            if (view.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                DelayedTrickScenario.Accept(game, new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            else
            {
                Assert(view.PendingDecision is null, "The fixed delayed fixture reached an unexpected human decision; no seed search is permitted.");
                DelayedTrickScenario.Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
        }
        Assert(advances < 32 && game.CreateSnapshot(0).CurrentSeat == 1 && !game.Events.Any(item => item.Payload is JudgmentRequestedEvent),
            "The real delayed fixture did not stop at its first public AI1 Indulgence response window.");
        Console.WriteLine($"Native judgment setup: seed31, physical Indulgence{id}, {advances} bounded real preparation commands, public current nullification prompt.");
        return id;
    }

    private static (JudgmentResolvedEvent Result, BattleCue Cue, int Commands, int Advances, int Cursor) Resolve(NativeHost host, int indulgenceId)
    {
        var model = host.Model; var game = Engine(model); var cursor = game.Events.Count; var commands = game.AcceptedCommands.Count;
        var hand = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToArray();
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Assert(prompt is { Kind: DecisionKind.Nullification, PlayerSeat: 0, IncomingCard: CardKind.Indulgence },
            "The native judgment must begin by answering its current actual delayed-card counterspell prompt.");
        var pass = model.NullificationChoices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "pass");
        model.SelectNullificationChoiceCommand.Execute(pass);
        Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == prompt.PromptId && answer.Choice == pass.Id, "Native pass did not submit exactly its published current response once.");
        model.SelectNullificationChoiceCommand.Execute(pass);
        Assert(game.AcceptedCommands.Count == commands + 1, "A stale native pass submitted a second response.");
        var advances = 0;
        for (; advances < 12 && !game.Events.Skip(cursor).Any(item => item.Payload is JudgmentResolvedEvent); advances++)
        {
            Assert(model.CanStepAi && game.CreateSnapshot(0).PendingDecision is null,
                "The fixed judgment unexpectedly required another human response; no fabricated choice or seed search is permitted.");
            model.StepAiCommand.Execute(null);
        }
        var requestedEnvelope = game.Events.Skip(cursor).Single(item => item.Payload is JudgmentRequestedEvent);
        var requested = (JudgmentRequestedEvent)requestedEnvelope.Payload;
        var resolvedEnvelope = game.Events.Skip(cursor).Single(item => item.Payload is JudgmentResolvedEvent);
        var result = (JudgmentResolvedEvent)resolvedEnvelope.Payload;
        Assert(result is { Reason: JudgmentReasons.Indulgence, TargetSeat: 1, CardId: > 0, CardKind: not null, Suit: not null, Rank: >= 1 and <= 13 } &&
            result.ResolutionId == requested.ResolutionId && result.ParentResolutionId == requested.ParentResolutionId &&
            requested.SourceCard == CardKind.Indulgence && requestedEnvelope.Sequence < resolvedEnvelope.Sequence &&
            game.AcceptedCommands.Count == commands + 1 + advances && advances < 12 &&
            hand.SequenceEqual(game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id)),
            "The actual delayed judgment lost its public owner/ID/complete face, duplicated commands or paid a human card on pass.");
        var id = result.CardId!.Value;
        Assert(game.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.DrawPile && move.To == CardLocation.Judgment(1) && move.Reason == CardMoveReasons.JudgmentReveal) == 1 &&
            game.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Judgment(1) && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.JudgmentFinish) == 1 &&
            game.CardMovements.Count(move => move.CardId == indulgenceId && move.From == CardLocation.Judgment(1) && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.DelayedCardFinish) == 1 &&
            game.Events.Count(item => item.Payload is CardActionAcceptedEvent { Action.Type: CardActionType.Use } use &&
                use.Action.PhysicalCards.Any(card => card.CardId == indulgenceId)) == 1,
            "The real judgment draw/discard or originally paid delayed card did not follow its unique authoritative movements.");
        var delayed = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DelayedCardResolvedEvent>().Single();
        var counterspell = game.Events.Skip(cursor).Select(item => item.Payload).OfType<NullificationResolvedEvent>().Single();
        Assert(delayed.CardId == indulgenceId && delayed.CardKind == CardKind.Indulgence && delayed.TargetSeat == 1 &&
            delayed.JudgmentCardId == id && delayed.JudgmentSucceeded == result.Succeeded && delayed.SkippedPlayPhase == !result.Succeeded &&
            counterspell.EffectCardId == indulgenceId && counterspell is { EffectNullified: false, ChainDepth: 0 },
            "The displayed safe-suit result did not match the actual delayed effect and unanswered counterspell chain.");
        var projected = BattleCueProjector.Project(game.Events.Skip(cursor), game.CreateSnapshot(0));
        var inquiry = projected.Single(cue => cue.Sequence == requestedEnvelope.Sequence);
        Assert(inquiry is { Kind: BattleCueKind.Judgment, IsInquiry: true, JudgmentStage: JudgmentCueStage.Requested, JudgmentCard: null } &&
            inquiry.JudgmentResolutionId == result.ResolutionId && inquiry.PublicCard is null && inquiry.PublicCardId is null && inquiry.CardKind is null,
            "A public requested judgment exposed a face or became an ordinary played card.");
        var cue = model.BattleCues.Single(cue => cue.Sequence == resolvedEnvelope.Sequence);
        var face = cue.JudgmentCard;
        var expected = result.Succeeded ? "红桃 · 不跳过出牌阶段" : "非红桃 · 跳过出牌阶段";
        Assert(cue.HasJudgmentCard && cue.JudgmentStage == JudgmentCueStage.Resolved && cue.JudgmentResolutionId == result.ResolutionId &&
            cue.SourceSeat == 1 && cue.TargetSeats.SequenceEqual(new[] { 1 }) && cue.Revision == resolvedEnvelope.Revision &&
            face is not null && face.Id == id && face.Kind == result.CardKind && face.Suit == result.Suit && face.Rank == result.Rank &&
            cue.Detail == expected && cue.JudgmentTitle == "乐不思蜀" && cue.PublicCard is null && cue.PublicCardId is null && cue.CardKind is null &&
            model.RecentPlays.All(play => play.PublicCardId != id) &&
            model.BattleCues.All(item => item.Kind != BattleCueKind.Card || item.PublicCardId != id),
            "The native judgment must use only its actual resolved effective face/result, without fabricating a public use or landing.");
        return (result, cue, commands, advances, cursor);
    }

    private static void AssertFrame(NativeHost host, BattleCue cue, double seconds)
    {
        var drawing = VisualTreeHelper.GetDrawing(host.Layer);
        var glyphs = Glyphs(drawing, Matrix.Identity, 1).Where(item => item.Opacity > .02).ToArray();
        var text = string.Concat(glyphs.Select(item => item.Glyph.GlyphRun.Characters is { } chars ? new string(chars.ToArray()) : string.Empty));
        var face = cue.JudgmentCard!; var images = Images(drawing, Matrix.Identity, 1).Where(item => item.Opacity > .02 && ReferenceEquals(item.Image.ImageSource, CardArt.Get(face.Kind))).ToArray();
        if (seconds < RevealTime)
        {
            Assert(text.Contains("翻开判定牌", StringComparison.Ordinal) && !text.Contains(cue.Detail!, StringComparison.Ordinal),
                $"Native judgment {seconds:F2}s exposed its result before completing the real flip; glyphs={text}.");
            if (seconds < .09) Assert(images.Length == 0, "The first back-facing native judgment frame exposed the result face.");
            else Assert(images.Length == 1 && images[0].Bounds.Width < 63,
                "A native judgment still flipping before180ms already showed a full-width settled face.");
        }
        else if (seconds < Duration)
        {
            var suit = face.Suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", Suit.Diamond => "♦", _ => "?" };
            Assert(text.Contains(cue.ActorName, StringComparison.Ordinal) && text.Contains("乐不思蜀", StringComparison.Ordinal) &&
                text.Contains("判定花色 " + suit + face.RankText, StringComparison.Ordinal) &&
                text.Contains(cue.Detail!, StringComparison.Ordinal) && images.Length == 1 && Math.Abs(images[0].Bounds.Width - 64) < .1 &&
                Math.Abs(images[0].Bounds.Height - 90) < .1,
                $"Native judgment lost its precise public identity, effective suit/rank, complete face or actual outcome: time={seconds:F2}, glyphs={text}, " +
                $"face bounds=[{string.Join(";", images.Select(item => item.Bounds))}].");
            Assert(Images(drawing, Matrix.Identity, 1).Any(item => ReferenceEquals(item.Image.ImageSource, CardArt.GetRank(face.RankText, suit))) &&
                Images(drawing, Matrix.Identity, 1).Any(item => ReferenceEquals(item.Image.ImageSource, CardArt.GetSuit(suit))),
                "The actual revealed face lost its public rank or effective suit artwork.");
        }
        else Assert(!text.Contains("乐不思蜀", StringComparison.Ordinal) && images.Length == 0,
            "The expired native judgment retained an old flip or outcome caption.");
        var obstacles = Find<FrameworkElement>(host.Root).Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0 &&
            (element is Button or TextBlock || element is HealthMeter)).Select(element => element.TransformToVisual(host.Layer).TransformBounds(new Rect(element.RenderSize))).ToArray();
        var viewport = new Rect(host.Layer.RenderSize);
        foreach (var panel in Shapes(drawing, Matrix.Identity, 1).Where(item => item.Opacity > .02 &&
            item.Shape.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(19, 31, 34)))
            Assert(viewport.Contains(panel.Bounds) && obstacles.All(obstacle => !panel.Bounds.IntersectsWith(obstacle)),
                $"A native judgment caption obscured/cropped a portrait, name, HP, readout or command: time={seconds:F2}, panel={panel.Bounds}, viewport={viewport}.");
        Assert(glyphs.All(item => viewport.Contains(item.Bounds)) && images.All(item => viewport.Contains(item.Bounds)),
            "Actual rendered native judgment glyphs or face were geometrically clipped.");
    }

    private static void InterruptAndLoad(NativeHost host, string output, int id, SafeBoundary pending, SafeBoundary completed,
        List<(double Seconds, GameSound Sound)> sounds)
    {
        host.Model.LoadManualGameCommand.Execute(null); Flush(host);
        Assert(!host.Model.HasSaveError && Same(Engine(host.Model), pending) && host.Layer.ActiveEffectCount == 0,
            "Actual pre-judgment loading replayed its old history or changed a safe view.");
        var soundCount = sounds.Count(item => item.Sound == GameSound.Card);
        Resolve(host, id); var boundary = Boundary(Engine(host.Model));
        Assert(Same(Engine(host.Model), completed), "Replaying the same real response command checkpoint changed its deterministic safe views.");
        host.Clock.Advance(.04); BattleAudioBeatChecks.Tick(host.Layer); Flush(host);
        Assert(host.Layer.ActiveEffectCount > 0 && sounds.Count(item => item.Sound == GameSound.Card) == soundCount,
            "The second actual judgment did not have a pending flip before the interruption boundary.");
        host.Model.IsMotionEnabled = false; Flush(host);
        Assert(host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning, "Motion off kept a real pending judgment or its timer.");
        host.Model.IsMotionEnabled = true; host.Clock.Advance(.26); BattleAudioBeatChecks.Tick(host.Layer);
        host.Capture(output, "judgment-native-motion-on-no-replay", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && sounds.Count(item => item.Sound == GameSound.Card) == soundCount && Same(Engine(host.Model), boundary),
            "Restoring motion replayed an interrupted flip/sound or changed a committed safe state.");
        host.Model.SaveGameCommand.Execute(null); host.Model.LoadManualGameCommand.Execute(null); Flush(host);
        host.Capture(output, "judgment-native-loaded-no-replay", 1120);
        Assert(!host.Model.HasSaveError && host.Layer.ActiveEffectCount == 0 && Same(Engine(host.Model), boundary),
            "Loading an actual resolved judgment replayed its historical face or changed its cost/command boundary.");
        host.Window.Content = null; host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert(!host.Root.IsLoaded && host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning,
            "Native unload retained a judgment effect or running frame timer.");
        host.Window.Content = host.Root; host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.Clock.Advance(.30); BattleAudioBeatChecks.Tick(host.Layer); host.Capture(output, "judgment-native-reattached-no-replay", 1120);
        Assert(host.Root.IsLoaded && host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning &&
            sounds.Count(item => item.Sound == GameSound.Card) == soundCount && Same(Engine(host.Model), boundary),
            "Reattaching the actual native root replayed a historical judgment or sound, or changed a safe view.");
    }

    private static void Flush(NativeHost host)
    {
        host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); host.Root.UpdateLayout(); host.Layer.InvalidateVisual();
        host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
    private static void SetSize(NativeHost host, int width)
    {
        host.Root.Width = width; host.Root.Height = width == 1120 ? 740 : 1080;
        host.Window.SizeToContent = SizeToContent.WidthAndHeight; host.Window.UpdateLayout(); Flush(host);
        Assert(host.Root.RenderSize == new Size(host.Root.Width, host.Root.Height), "Native judgment recording used a cropped output root.");
    }
    private static IEnumerable<(GeometryDrawing Shape, Rect Bounds, double Opacity)> Shapes(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GeometryDrawing shape) yield return (shape, new MatrixTransform(transform).TransformBounds(shape.Geometry.Bounds), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Shapes(child, next, opacity * group.Opacity)) yield return item;
        }
    }
    private static IEnumerable<(GlyphRunDrawing Glyph, Rect Bounds, double Opacity)> Glyphs(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GlyphRunDrawing glyph) yield return (glyph, new MatrixTransform(transform).TransformBounds(glyph.Bounds), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Glyphs(child, next, opacity * group.Opacity)) yield return item;
        }
    }
    private static IEnumerable<(ImageDrawing Image, Rect Bounds, double Opacity)> Images(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Images(child, next, opacity * group.Opacity)) yield return item;
        }
    }
    private sealed record SafeBoundary(long Revision, int Commands, int Events, int Movements, string[] Views);
    private static SafeBoundary Boundary(GameEngine game) => new(game.Revision, game.AcceptedCommands.Count, game.Events.Count, game.CardMovements.Count,
        Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray());
    private static bool Same(GameEngine game, SafeBoundary before) => game.Revision == before.Revision && game.AcceptedCommands.Count == before.Commands &&
        game.Events.Count == before.Events && game.CardMovements.Count == before.Movements &&
        Enumerable.Range(0, before.Views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == before.Views[seat]);
    private static byte[] Pixels(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes;
    }
    private static double Timing(string name) => (double)typeof(BattleFeedbackLayer).Assembly
        .GetType("CardGame.Wpf.Controls.BattleEffectPainter")!.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
    private sealed class BindingListener : TraceListener
    {
        internal List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        { if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message); }
    }
}
