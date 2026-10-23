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

    public static void OriginBoundaries(string output)
    {
        NarrowRouteGeometry();
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
        CheckLiftPoseUpdates();
        CheckLongFooter();
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
        void AddCue(int seat, int id, string actorName = "公开行动")
        {
            clock.Advance(2); // Expire the previous flight without resetting the origin cache or cue history.
            BattleFeedbackLayer.SetPlayAnchor(landing, ++sequence);
            cues.Add(new(sequence, BattleCueKind.Card, seat, [], "杀", actorName)
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
        void CheckLongFooter()
        {
            hand.Children.Add(card); Capture();
            hand.Children.Remove(card); Capture();
            layer.CommittedRevision++;
            // This is the current mobile Huang Yueying name, followed by the
            // ordinary public use label. It exceeds one footer line untrimmed.
            AddCue(0, 41, "谋黄月英·移动版");
            var drawings = Leaves(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).ToArray();
            var footer = drawings.Single(item => item.Drawing is GeometryDrawing
                { Geometry: RectangleGeometry, Brush: SolidColorBrush brush } && brush.Color == Color.FromArgb(224, 35, 27, 19));
            var footerBounds = new MatrixTransform(footer.Transform).TransformBounds(footer.Drawing.Bounds);
            var glyphBounds = drawings.Where(item => item.Drawing is GlyphRunDrawing).Select(item =>
                new MatrixTransform(item.Transform).TransformBounds(((GlyphRunDrawing)item.Drawing).GlyphRun.BuildGeometry().Bounds)).ToArray();
            Assert(glyphBounds.Length > 0 && glyphBounds.All(footerBounds.Contains),
                $"The real long public actor/use caption must stay inside its single-line flight footer; footer={footerBounds}, glyphs={string.Join(" | ", glyphBounds)}.");
            Console.WriteLine($"Long public flight footer: 谋黄月英·移动版使用; footer={footerBounds}; all {glyphBounds.Length} glyph runs contained.");

            static IEnumerable<(Drawing Drawing, Matrix Transform)> Leaves(Drawing? drawing, Matrix transform)
            {
                if (drawing is DrawingGroup group)
                {
                    var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
                    foreach (var child in group.Children)
                        foreach (var leaf in Leaves(child, next)) yield return leaf;
                }
                else if (drawing is not null) yield return (drawing, transform);
            }
        }
        void CheckLiftPoseUpdates()
        {
            var group = new CountingHandGroup { Width = 400, Height = 176 };
            var moving = new CardLiftSurface { Width = 110, Height = 156, MotionEnabled = false, Background = Brushes.Wheat };
            var neighbor = new Border { Width = 110, Height = 156, Background = Brushes.Wheat };
            BattleFeedbackLayer.SetHandAnchor(moving, 42);
            BattleFeedbackLayer.SetHandAnchor(neighbor, 43);
            Canvas.SetLeft(moving, 20); Canvas.SetTop(moving, 12);
            Canvas.SetLeft(neighbor, 150); Canvas.SetTop(neighbor, 12);
            group.Children.Add(moving); group.Children.Add(neighbor); hand.Children.Add(group);
            Capture();
            var neighborOrigin = neighbor.TransformToVisual(layer).TransformBounds(new Rect(neighbor.RenderSize));
            group.VisualChildReads = 0;
            // A real surface emits its lift event before any subsequent layout.
            // Immediate removal must retain this selected pose for the public cue.
            moving.IsSelected = true;
            var liftedOrigin = moving.TransformToVisual(layer).TransformBounds(new Rect(moving.RenderSize));
            var liftChildReads = group.VisualChildReads;
            Assert(liftChildReads == 0, "Lifting one hand surface traversed its sibling cards before layout.");
            group.Children.Remove(moving); group.Children.Remove(neighbor);
            layer.CommittedRevision++;
            AddCue(0, 42);
            Assert(Near(FaceBounds(layer, CardKind.Slash).Single(),
                    new Rect(liftedOrigin.X + 1, liftedOrigin.Y + 1, liftedOrigin.Width - 2, liftedOrigin.Height - 2)),
                "An immediate public use lost the selected surface's last lifted pose.");
            AddCue(0, 43);
            Assert(Near(FaceBounds(layer, CardKind.Slash).Single(),
                    new Rect(neighborOrigin.X + 1, neighborOrigin.Y + 1, neighborOrigin.Width - 2, neighborOrigin.Height - 2)),
                "Updating one lifted surface discarded the neighboring card's removed pose.");

            group.Children.Add(moving); group.Children.Add(neighbor); Capture();
            group.Visibility = Visibility.Hidden;
            moving.IsSelected = false;
            group.Children.Remove(moving); group.Children.Remove(neighbor);
            group.Visibility = Visibility.Visible;
            layer.CommittedRevision++;
            CheckFallback(0, 42, "lift beneath a hidden ancestor");
            CheckFallback(0, 43, "sibling beneath a hidden ancestor");
            hand.Children.Remove(group);
            Console.WriteLine($"Hand surface lift: {liftChildReads} sibling visual reads; immediate removed-pose handoff and hidden-ancestor retirement passed.");
        }
    }

    private static void NarrowRouteGeometry()
    {
        // Actual 1120-DIP native layout with a 300-DIP sidebar. Both shapes must
        // cross the controls without losing a public face or exposing a hidden one.
        var played = new Rect[]
        {
                new(279.0423307291667, 372.11800130208337, 262.44136718749996, 22.6630859375),
                new(205, 399, 110, 42),
                new(355, 399, 110, 42),
                new(515, 399, 90, 42),
                new(367.7817720448298, 15, 84.43645591034046, 111.85088964746397),
                new(17, 107.65666666666667, 154, 204),
                new(649, 107.65666666666667, 154, 204),
                new(820, 0, 300, 458),
                new(338, 150, 144, 44.07333333333334),
        };
        var transfer = new Rect[]
        {
                new(271.91832031250004, 372.08870442708337, 276.285546875, 22.765625),
                new(205, 399, 110, 42),
                new(355, 399, 110, 42),
                new(515, 399, 90, 42),
                new(211, 327, 198, 42),
                new(411, 327, 198, 42),
                new(334.9375, 149.09458333333333, 150.96875, 30.3125),
                new(376.537109375, 179.1559765625, 67.23828125, 17.716796875),
                new(367.7817720448298, 15, 84.43645591034046, 111.85088964746397),
                new(17, 107.65666666666667, 154, 204),
                new(649, 107.65666666666667, 154, 204),
                new(970, 469.44805194805195, 142, 188.1038961038961),
                new(820, 0, 300, 458),
                new(338, 150, 144, 44.07333333333334),
        };
        Check("Played", new(226, 565), new(410, 273.18666666666667), new(110, 156), new(86, 120), played);
        Check("Transfer", new(613, 189.25666666666666), new(508, 472), new(46, 64), new(18.4, 25.6), transfer);
        Check("Discard", new(613, 189.25666666666666), new(508, 472), new(46, 64), new(18.4, 25.6), transfer);
        Check("Played", new(226, 565), new(410, 273.18666666666667), new(110, 156), new(86, 120), [], open: true);

        void Check(string style, Point launch, Point destination, Size initial, Size final, Rect[] obstacles, bool open = false)
        {
            var type = typeof(BattleFeedbackLayer).Assembly.GetType("CardGame.Wpf.Controls.PublicCardFlightPath")!;
            var create = type.GetMethod("Create")!;
            var profile = Enum.Parse(create.GetParameters()[^1].ParameterType, style);
            var viewport = new Rect(2, 2, 1116, 664);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var path = create.Invoke(null, [launch, destination, initial, final, obstacles, viewport, profile])!;
            var elapsed = watch.Elapsed.TotalMilliseconds;
            Assert(!(bool)type.GetProperty("NeedsClip")!.GetValue(path)!,
                $"The {style} narrow-layout route must remain completely visible instead of falling back to clipping.");
            var position = type.GetMethod("PositionAt")!;
            var scale = type.GetMethod("ScaleAt")!;
            var swept = type.GetMethod("SweptBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var steps = (int)type.GetField("ValidationSteps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
            var minimumScale = 1.0;
            for (var sample = 0; sample <= 512; sample++)
            {
                var time = sample / 512.0;
                // These are the renderer's actual timelines and ink, independent
                // of the planner's interpolation and conservative bounds code.
                var move = 1 - Math.Pow(1 - time, style == "Played" ? 3 : 2);
                var center = (Point)position.Invoke(path, [time])!;
                var zoom = (double)scale.Invoke(path, [move])!;
                minimumScale = Math.Min(minimumScale, zoom);
                Assert(zoom > 0 && zoom <= 1, "A flight must retain a positive bounded card size.");
                if (sample is 0 or 512)
                    Assert((center - (sample == 0 ? launch : destination)).Length < .000001 && Math.Abs(zoom - 1) < .000001,
                        "Adaptive routing must retain the exact launch, landing and unscaled endpoint sizes.");
                if (open)
                    Assert(Math.Abs(zoom - (1 - .42 * Math.Sin(move * Math.PI))) < .000001,
                        "An unobstructed flight must retain its existing scale curve.");
                var width = initial.Width + (final.Width - initial.Width) * move;
                var height = initial.Height + (final.Height - initial.Height) * move;
                var rotation = (style == "Transfer" ? 8 : style == "Discard" ? -10 : Math.Sign(destination.X - launch.X) * 10) * Math.Sin(time * Math.PI);
                var transform = new TransformGroup();
                transform.Children.Add(new ScaleTransform(zoom, zoom));
                transform.Children.Add(new RotateTransform(rotation));
                transform.Children.Add(new TranslateTransform(center.X, center.Y));
                var ink = Rect.Empty;
                var body = new Rect(-width / 2, -height / 2, width, height);
                var outline = body;
                outline.Inflate(style == "Played" ? 1 : .75, style == "Played" ? 1 : .75);
                Include(outline);
                if (style == "Played")
                {
                    Include(new Rect(body.X + 4, body.Y + 5, body.Width, body.Height));
                    var footerHeight = 21 * width / 86;
                    Include(new Rect(body.X, body.Bottom - footerHeight * (1 - move), width, footerHeight));
                }
                var interval = Math.Min(steps - 1, (int)(time * steps));
                var proof = (Rect)swept.Invoke(path, [(double)interval / steps, (double)(interval + 1) / steps])!;
                proof.Inflate(.000001, .000001);
                Assert(proof.Contains(ink) && viewport.Contains(ink) && obstacles.All(obstacle => !obstacle.IntersectsWith(ink)),
                    $"The {style} route proof must enclose all drawn ink, including rotation and stroke, and avoid every real control at t={time:F4}.");
                void Include(Rect rectangle)
                {
                    foreach (var point in new[] { rectangle.TopLeft, rectangle.TopRight, rectangle.BottomLeft, rectangle.BottomRight })
                        ink.Union(transform.Transform(point));
                }
            }
            Console.WriteLine($"Narrow route {style}, open={open}: {elapsed:F2} ms, minimum scale={minimumScale:F3}, exact endpoints and all 513 drawn footprints clear.");
        }
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
    private sealed class CountingHandGroup : Canvas
    {
        public int VisualChildReads { get; set; }
        protected override Visual GetVisualChild(int index)
        {
            VisualChildReads++;
            return base.GetVisualChild(index);
        }
    }
}
