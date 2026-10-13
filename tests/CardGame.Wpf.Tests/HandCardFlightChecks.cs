using System.IO;
using System.Collections.ObjectModel;
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

internal static class HandCardFlightChecks
{
    public static void AcceptedLocalCards(string output)
    {
        using (var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore()))
        {
            StartLordFixture(vm);
            vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
            AdvanceToDecision(vm);
            var action = Engine(vm).GetHumanLegalActions().Where(action => action.CardId is > 0 &&
                action.Kind is LegalActionKind.Equip or LegalActionKind.Slash or LegalActionKind.DrawTwo or LegalActionKind.Duel &&
                vm.Hand.Any(card => card.Id == action.CardId && (action.PlayedCardKind is null || card.Kind == action.PlayedCardKind)))
                .OrderBy(action => action.Kind == LegalActionKind.Equip ? 0 : 1).First();
            NativeCard(vm, vm.Hand.Single(card => card.Id == action.CardId), action.TargetSeat, output, "use");
        }
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, arrowBarrage: true);
        using var response = fixture.Model;
        NativeCard(response, response.Hand.First(card => card.Kind == CardKind.Dodge && card.IsPlayable), null, output, "response");
    }

    private static void NativeCard(MainViewModel vm, CardViewModel card, int? target, string output, string name)
    {
        vm.IsMotionEnabled = true; vm.IsSoundEnabled = false; vm.IsMusicEnabled = false;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock(); layer.Clock = clock;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, $"hand-flight-{name}-unselected.png"));
            var game = Engine(vm); var revision = game.Revision; var acceptedBefore = game.AcceptedCommands.Count;
            vm.SelectCardCommand.Execute(card);
            if (target is { } seat) vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
            Assert(vm.CanConfirmSelected, "The fixed native hand card cannot be confirmed.");
            Render(root, 1120, 740, Path.Combine(output, $"hand-flight-{name}-before.png"));
            var button = Find<Button>(root).Single(button => button.Name == "CardButton" && ReferenceEquals(button.DataContext, card));
            var surface = Find<Grid>(button).Single(grid => grid.Name == "CardSurface");
            var origin = surface.TransformToVisual(layer).TransformBounds(new Rect(surface.RenderSize));
            Console.WriteLine($"Hand capture binding: owner={layer.HandOwnerSeat}, anchor={BattleFeedbackLayer.GetHandAnchor(surface)}, root={layer.HandRoot?.Name}, viewport={layer.HandRoot?.TransformToVisual(layer).TransformBounds(new Rect(layer.HandRoot.RenderSize))}, visible={surface.Visibility}.");
            Assert(game.Revision == revision && game.AcceptedCommands.Count == acceptedBefore && layer.ActiveEffectCount == 0,
                "Selecting a local card started a public flight or committed a command.");
            vm.ConfirmSelectedCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == acceptedBefore + 1 && !vm.Hand.Any(item => item.Id == card.Id),
                "The physical card was not committed once and removed from the visible hand.");
            var cue = vm.BattleCues.Last(cue => cue.PublicCardId == card.Id && cue.CardKind == card.Kind);
            Console.WriteLine($"Committed hand cue: {cue.Sequence}, source={cue.SourceSeat}, physical={cue.PublicCardId}.");
            var accepted = game.AcceptedCommands.Count;
            var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
                .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            clock.Advance(.012);
            Render(root, 1120, 740, Path.Combine(output, $"hand-flight-{name}-launch.png"));
            var launch = FaceBounds(layer, card.Kind).Single();
            var expected = new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2);
            Console.WriteLine($"Native hand {name}: card={card.Id}/{card.Kind}, source={origin}, first face={launch}, accepted={acceptedBefore}->{accepted}.");
            Assert(Near(launch, expected), "The accepted local card launched from its portrait instead of its visible selected hand slot.");
            Assert(Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Single(item => ReferenceEquals(item.Image.ImageSource, card.Artwork)).Opacity > .99,
                "The already visible hand card faded through a different card at launch.");
            Assert(Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity)
                    .Any(item => ReferenceEquals(item.Image.ImageSource, card.RankArtwork)) &&
                Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity)
                    .Any(item => ReferenceEquals(item.Image.ImageSource, card.SuitArtwork)),
                "The committed physical hand card lost its actual suit or rank at flight launch.");
            var pendingLanding = Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == cue.Sequence);
            Assert(((FrameworkElement)pendingLanding.Parent).Opacity < .001,
                "The public row showed a second face and actor caption before the committed hand card arrived.");
            vm.SortHandCommand.Execute(null);
            layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, $"hand-flight-{name}-after-sort.png"));
            Assert(Near(FaceBounds(layer, card.Kind).Single(), launch), "Sorting the remaining hand moved an already committed card's launch position.");
            var duration = cue.Kind == BattleCueKind.Card ? .9 : 1.15;
            clock.Advance(duration * .3699 - .012);
            layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, $"hand-flight-{name}-arriving.png"));
            var landing = Find<Border>(root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == cue.Sequence);
            var settled = landing.TransformToVisual(layer).TransformBounds(new Rect(1, 1, landing.ActualWidth - 2, landing.ActualHeight - 2));
            Console.WriteLine($"Native hand {name} landing: expected={settled}, actual={FaceBounds(layer, card.Kind).Single()}.");
            Assert(Near(FaceBounds(layer, card.Kind).Single(), settled), "A hand-origin flight missed its existing public card landing.");
            Assert(((FrameworkElement)landing.Parent).Opacity < .001,
                "The static public face appeared just before its flight arrived.");
            foreach (var artwork in new[] { card.RankArtwork, card.SuitArtwork })
            {
                var image = Find<Image>(landing).Single(image => ReferenceEquals(image.Source, artwork));
                var local = Images(VisualTreeHelper.GetDrawing(image), Matrix.Identity).Single(item => ReferenceEquals(item.Image.ImageSource, artwork)).Bounds;
                var staticCorner = image.TransformToVisual(layer).TransformBounds(local);
                var movingCorner = Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Single(item => ReferenceEquals(item.Image.ImageSource, artwork)).Bounds;
                Assert(Near(staticCorner, movingCorner), "A flying rank or suit jumped to a different slot when the static public face took over.");
            }
            if (RecordMotion) Record(root, layer, clock, Path.Combine(output, $"hand-flight-{name}.gif"));
            clock.Advance(.002); layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, $"hand-flight-{name}-landed.png"));
            Assert(!FaceBounds(layer, card.Kind).Any() && ((FrameworkElement)landing.Parent).Opacity > .99 &&
                !BattleFeedbackLayer.GetIsFlightPending(landing),
                "An arrived hand card kept a duplicate flying face or failed to reveal its public face and caption.");
            var publicPlay = vm.RecentPlays.Single(play => play.Sequence == cue.Sequence);
            Assert(publicPlay.PublicCard == cue.PublicCard && publicPlay.Rank == card.Rank && publicPlay.SuitGlyph == card.SuitGlyph,
                "The public row lost the committed card's metadata during flight handoff.");
            Assert(ReferenceEquals(layer.Sidebar, window.FindName("TableSidebar")),
                "The real table's playback and audio controls were not protected from combat effects.");
            AssertSidebarClear(layer);
            AssertPlaybackClearOfPublicCaptions(root, vm);
            foreach (var size in new[] { new Size(1440, 880), new Size(1920, 1080) })
            {
                Render(root, (int)size.Width, (int)size.Height,
                    Path.Combine(output, $"playback-{name}-{size.Width}-captions.png"));
                AssertPlaybackClearOfPublicCaptions(root, vm);
            }
            Assert(game.AcceptedCommands.Count == accepted && Enumerable.Range(0, views.Length)
                .All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Rendering or hand sorting changed a committed command or player-safe snapshot.");
            vm.SaveGameCommand.Execute(null); vm.LoadManualGameCommand.Execute(null);
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && !vm.HasSaveError,
                "Loading a boundary replayed a private hand origin or retained its timer.");
            Assert(vm.RecentPlays.Single(play => play.Sequence == cue.Sequence).PublicCard == publicPlay.PublicCard,
                "Loading accepted commands lost the public card's frozen printed face.");
            var loaded = Engine(vm); var beforeResume = loaded.AcceptedCommands.Count;
            var resume = (Button)window.FindName("ContinueAdvanceButton");
            Assert(resume.IsEnabled && resume.Command!.CanExecute(resume.CommandParameter),
                "The paused restored table lost its bound resume action.");
            typeof(System.Windows.Controls.Primitives.ButtonBase)
                .GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(resume, null);
            Assert(loaded.AcceptedCommands.Count == beforeResume + 1 && loaded.AcceptedCommands.Last() is AdvanceCommand && !vm.CanStepAi,
                "The sidebar resume button must submit one native advance and stop at the next human boundary.");
        }
        finally { window.Content = null; window.Close(); }
    }

    public static void OriginBoundaries(string output)
    {
        var root = new Canvas { Width = 900, Height = 550, Background = Brushes.DarkSlateGray };
        var hand = new Canvas { Width = 400, Height = 176, ClipToBounds = true };
        Place(hand, 180, 365);
        var card = new Border { Width = 110, Height = 156, Background = Brushes.Wheat };
        BattleFeedbackLayer.SetHandAnchor(card, 41); Canvas.SetLeft(card, 40); Canvas.SetTop(card, 12); hand.Children.Add(card);
        var portraits = new[] { new Rect(720, 355, 120, 170), new Rect(720, 25, 120, 170) };
        for (var seat = 0; seat < portraits.Length; seat++)
        {
            var portrait = new Border { Width = 120, Height = 170, Background = Brushes.Black };
            BattleFeedbackLayer.SetSeatAnchor(portrait, seat); Place(portrait, portraits[seat].X, portraits[seat].Y);
        }
        var landing = new Border { Width = 86, Height = 120, Background = Brushes.Olive }; Place(landing, 480, 210);
        var cues = new ObservableCollection<BattleCue>(); var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 900, Height = 550, AnchorRoot = root, HandRoot = hand, HandOwnerSeat = 0, Cues = cues, Clock = clock };
        root.Children.Add(layer);
        Capture();
        long sequence = 0;
        // Control-only projection lifecycle: native engine removal/commit is
        // covered separately. Keep the observable history across every step.
        layer.CommittedRevision = 10; Capture();
        var origin = card.TransformToVisual(layer).TransformBounds(new Rect(card.RenderSize));
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 11;
        AddCue(0, 41);
        Assert(Near(FaceBounds(layer, CardKind.Slash).Single(),
                new Rect(origin.X + 1, origin.Y + 1, origin.Width - 2, origin.Height - 2)),
            "Layout after physical removal discarded the last hand pose before its new-revision public cue.");
        // Repeat within the same revision so expiry pruning cannot mask a
        // missing exact-ID consumption of the first cue's cached origin.
        CheckFallback(0, 41, "already consumed origin");

        layer.CommittedRevision = 12;
        hand.Children.Add(card); Capture();
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 13; Capture(); // No matching public cue consumes this removed card.
        layer.CommittedRevision = 14;
        CheckFallback(0, 41, "unconsumed origin beyond one commit");

        hand.Children.Add(card); Capture();
        Canvas.SetLeft(card, 600); Capture();
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 15;
        CheckFallback(0, 41, "removed after an offscreen pose");

        Canvas.SetLeft(card, 40); hand.Children.Add(card); Capture();
        card.Visibility = Visibility.Collapsed; Capture();
        hand.Children.Remove(card); Capture();
        layer.CommittedRevision = 16;
        CheckFallback(0, 41, "removed after a hidden pose");
        card.Visibility = Visibility.Visible; hand.Children.Add(card); Capture();

        CheckFallback(1, 41, "foreign owner");
        CheckFallback(0, 99, "unmatched public card");
        Canvas.SetLeft(card, 600); Capture();
        CheckFallback(0, 41, "offscreen hand card");
        Canvas.SetLeft(card, 40); Capture();
        hand.Visibility = Visibility.Collapsed; Capture();
        CheckFallback(0, 41, "hidden hand root");
        hand.Visibility = Visibility.Visible; Capture();
        hand.Children.Remove(card);
        cues.Clear(); // Clear a removed card's cached position before a fresh cue.
        CheckFallback(0, 41, "reset boundary");
        hand.Children.Add(card); Capture();
        layer.MotionEnabled = false; hand.Children.Remove(card); layer.MotionEnabled = true;
        CheckFallback(0, 41, "motion boundary");
        hand.Children.Add(card); Capture();
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        hand.Children.Remove(card);
        cues.Add(new(++sequence, BattleCueKind.Card, 0, [], "杀", "你") { CardKind = CardKind.Slash, PublicCardId = 41 });
        Capture();
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning, "An unloaded hand origin observed a new cue.");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Capture();
        Assert(layer.ActiveEffectCount == 0, "Reloading replayed a historical hand-origin cue.");
        CheckFallback(0, 41, "unload/reload boundary");
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(!layer.IsHitTestVisible && !layer.Focusable && layer.ActiveEffectCount == 0,
            "The hand flight retained animation or intercepted gameplay after unloading.");
        Console.WriteLine("Hand origin boundaries: removed-pose revision handoff, exact-ID consumption, one-commit expiry, removed offscreen/hidden fallback, actor/ID match, reset/motion/unload and historical cue non-replay passed.");

        void Place(FrameworkElement element, double x, double y)
        { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); root.Children.Add(element); }
        void Capture()
        {
            layer.InvalidateVisual(); root.Measure(new Size(900, 550)); root.Arrange(new Rect(0, 0, 900, 550)); root.UpdateLayout();
            new RenderTargetBitmap(900, 550, 96, 96, PixelFormats.Pbgra32).Render(root);
        }
        void AddCue(int seat, int id)
        {
            clock.Advance(2); // Expire the previous flight without resetting the origin cache or cue history.
            BattleFeedbackLayer.SetPlayAnchor(landing, ++sequence);
            cues.Add(new(sequence, BattleCueKind.Card, seat, [], "杀", "公开行动")
                { CardKind = CardKind.Slash, PublicCardId = id, Revision = layer.CommittedRevision });
            clock.Advance(.012); Capture();
            Assert(layer.ActiveEffectCount == 1, "A fresh boundary cue must retire the prior flight without clearing history.");
        }
        void CheckFallback(int seat, int id, string boundary)
        {
            AddCue(seat, id);
            var image = FaceBounds(layer, CardKind.Slash).Single();
            var portrait = portraits[seat]; var center = new Point(portrait.X + portrait.Width / 2, portrait.Y + portrait.Height / 2);
            Assert((new Point(image.X + image.Width / 2, image.Y + image.Height / 2) - center).Length < .1,
                $"A {boundary} cue reused a private/stale hand slot.");
        }
    }

    private static void Record(FrameworkElement root, BattleFeedbackLayer layer, FrameClock clock, string path)
    {
        var encoder = new GifBitmapEncoder(); clock.Reset();
        for (var frame = 0; frame < 24; frame++)
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
        using (var file = File.Create(path)) encoder.Save(file);
        BattleEffectsChecks.SetGifTiming(path, 4);
        using var recorded = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(recorded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert(decoder.Frames.Count == 24 && decoder.Frames.All(frame => frame.Metadata is BitmapMetadata data && (ushort)data.GetQuery("/grctlext/Delay") == 4),
            "The native hand-flight recording lost its frame timing.");
        Console.WriteLine($"Native hand motion: {Path.GetFileName(path)}, {decoder.Frames.Count} frames, {decoder.Frames[0].PixelWidth}x{decoder.Frames[0].PixelHeight}, all 40 ms.");
    }

    private static bool Near(Rect first, Rect second) => Math.Abs(first.X - second.X) < .1 && Math.Abs(first.Y - second.Y) < .1 &&
        Math.Abs(first.Width - second.Width) < .1 && Math.Abs(first.Height - second.Height) < .1;
    internal static void AssertPlaybackClearOfPublicCaptions(FrameworkElement root, MainViewModel vm)
    {
        var resume = Find<Button>(root).Single(button => ReferenceEquals(button.Command, vm.RunToHumanCommand) &&
            Equals(button.Content, "继续推进"));
        Assert(resume.Visibility == Visibility.Visible && resume.IsEnabled && resume.ActualHeight > 0,
            "The paused real AI boundary must expose an enabled resume action.");
        var buttonBounds = resume.TransformToVisual(root).TransformBounds(new Rect(resume.RenderSize));
        var captions = Find<TextBlock>(root).Where(text => text.Name == "PlayActorCaption").ToArray();
        Assert(captions.Length == vm.RecentPlays.Count && captions.All(text =>
                !buttonBounds.IntersectsWith(text.TransformToVisual(root).TransformBounds(new Rect(text.RenderSize)))),
            "The resume action covers a committed public card's actor/action caption.");
        Console.WriteLine($"Playback/public captions: resume={buttonBounds}; {captions.Length} captions clear.");
    }
    private static void AssertSidebarClear(BattleFeedbackLayer layer)
    {
        var sidebar = layer.Sidebar!;
        var bounds = sidebar.TransformToVisual(layer).TransformBounds(new Rect(sidebar.RenderSize));
        var width = (int)Math.Ceiling(layer.ActualWidth); var height = (int)Math.Ceiling(layer.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(layer);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        for (var y = Math.Max(0, (int)Math.Ceiling(bounds.Top) + 2); y < Math.Min(height, (int)Math.Floor(bounds.Bottom) - 2); y++)
            for (var x = Math.Max(0, (int)Math.Ceiling(bounds.Left) + 2); x < Math.Min(width, (int)Math.Floor(bounds.Right) - 2); x++)
                Assert(pixels[(y * width + x) * 4 + 3] == 0, "The native combat overlay painted over playback or audio controls.");
    }
    private static IEnumerable<Rect> FaceBounds(BattleFeedbackLayer layer, CardKind? kind) =>
        Images(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Where(item => ReferenceEquals(item.Image.ImageSource, CardArt.Get(kind))).Select(item => item.Bounds);
    private static IEnumerable<(ImageDrawing Image, Rect Bounds, double Opacity)> Images(Drawing? drawing, Matrix transform, double opacity = 1)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect), opacity);
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children) foreach (var item in Images(child, next, opacity * group.Opacity)) yield return item;
        }
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
        public void Reset() => _ticks = 0;
    }
}
