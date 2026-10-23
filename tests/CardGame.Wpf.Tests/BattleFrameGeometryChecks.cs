using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using static Program;

internal static class BattleFrameGeometryChecks
{
    public static void FrameGeometry(string output)
    {
        using var fixture = new Scene();
        var single = Measure(1);
        var multiple = Measure(8);
        fixture.SaveFrame(Path.Combine(output, "battle-frame-geometry-eight.png"));
        var differentSources = MeasureDifferentSources(output);
        var report = JsonSerializer.Serialize(new { single, multiple, differentSources }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "battle-frame-geometry.json"), report);
        Console.WriteLine("Frame geometry (same frozen 200ms scene, 16 warmup / 64 measured frames): " + report);
        CheckMovingReadouts(fixture, output);
        CheckRoundedDistanceTie();
        CheckSidebarAndRetiredSlash(fixture);
        // Keep this after the measurements and image so the pre-change run can
        // record its expected failure without a special benchmark runner mode.
        Assert(single.ChildReads <= fixture.Root.Children.Count * 3 && multiple.ChildReads <= fixture.Root.Children.Count * 3,
            $"Anchor/readout walks must be bounded to three per frame, independently of draw count: {single.ChildReads}/{multiple.ChildReads} reads for {fixture.Root.Children.Count} children.");
        Assert(differentSources.All(sample => sample.FirstCaptions.WithTitleGlyphs == sample.FirstCaptions.Visible &&
            sample.SteadyCaptions.WithTitleGlyphs == sample.SteadyCaptions.Visible),
            "Any placed draw caption must retain visible title glyphs; placement counts are recorded without assuming eight captions fit.");

        Sample Measure(int cueCount)
        {
            fixture.SetCues(cueCount);
            for (var index = 0; index < 16; index++) fixture.Render();
            fixture.Root.ChildReads = 0;
            fixture.Render();
            var childReads = fixture.Root.ChildReads;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var index = 0; index < 64; index++) fixture.Render();
            var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 64;
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 64;
            return new(cueCount, childReads, milliseconds, bytes);
        }
    }

    private sealed record Sample(int Cues, int ChildReads, double MillisecondsPerFrame, long BytesPerFrame);
    private sealed record Timing(int Frames, int ChildReadsPerFrame, double MillisecondsPerFrame,
        double MaximumMilliseconds, long BytesPerFrame);
    private sealed record CaptionSample(int Visible, int WithTitleGlyphs, int CachedTextOffsets);
    private sealed record SourceSample(int Sources, int[] SourceSeats, Timing FirstFrame, Timing SteadyFrame,
        CaptionSample FirstCaptions, CaptionSample SteadyCaptions);

    private static void CheckSidebarAndRetiredSlash(Scene fixture)
    {
        // Reuse the measured scene. This is a render/cache regression, while
        // NativeActionFeedbackChecks retains the real paid Slash/Dodge flow.
        fixture.Command.Visibility = fixture.Readout.Visibility = Visibility.Visible;
        fixture.Command.RenderTransform = fixture.Readout.RenderTransform = Transform.Identity;
        var contents = new CountingCanvas { Width = 270, Height = 520 };
        var firstLine = new TextBlock { Text = "牌局记录：等待响应", Foreground = Brushes.Wheat };
        contents.Children.Add(firstLine);
        for (var index = 1; index <= 12; index++)
        {
            var line = new TextBlock { Text = $"公开记录 {index}：角色使用一张牌", Foreground = Brushes.Wheat };
            Canvas.SetTop(line, index * 36); contents.Children.Add(line);
        }
        var scroll = new ScrollViewer { Content = contents, CanContentScroll = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var sidebar = fixture.Place(new Border { Width = 300, Height = 280, ClipToBounds = true,
            Background = Brushes.Black, Child = scroll }, 840, 320);
        fixture.Layer.Sidebar = sidebar;
        fixture.Relayout();
        fixture.SetSlashCue(); fixture.Render();
        var firstRoute = fixture.SlashRoute;
        Assert(firstRoute is not null && SlashShapes().Any(),
            "The cache probe must begin with a real rendered Slash route and visible blade ink.");
        contents.ChildReads = 0; fixture.Root.ChildReads = 0; fixture.Render();
        var liveReads = fixture.Root.ChildReads;
        var liveSidebarReads = contents.ChildReads;

        firstLine.Text = "牌局记录更新：目标角色已经选择打出闪";
        fixture.Relayout(); fixture.Render();
        var textRetainedRoute = ReferenceEquals(firstRoute, fixture.SlashRoute);
        scroll.ScrollToVerticalOffset(48);
        fixture.Relayout(); fixture.Render();
        Assert(scroll.VerticalOffset > 0, "The sidebar probe must actually scroll its rendered child content.");
        var scrollRetainedRoute = ReferenceEquals(firstRoute, fixture.SlashRoute);
        var slashProtectsSidebar = Protected(SlashShapes());

        fixture.Advance(.5); // Card cue survives to 900ms; its blade ended at 585ms.
        firstLine.Text = "刀光退场后新增的公开记录";
        fixture.Readout.RenderTransform = new TranslateTransform(1, 0);
        fixture.Relayout();
        contents.ChildReads = 0; fixture.Root.ChildReads = 0; fixture.Render();
        var retiredReads = fixture.Root.ChildReads;
        var retiredSidebarReads = contents.ChildReads;
        var retiredRetainedRoute = ReferenceEquals(firstRoute, fixture.SlashRoute);
        var retiredInk = SlashShapes().Any();

        fixture.SetCues(1); fixture.Render();
        var drawRoute = fixture.DrawRoute;
        firstLine.Text = "又一条公开记录：摸牌数量已经更新";
        scroll.ScrollToVerticalOffset(96);
        fixture.Relayout(); fixture.Render();
        var drawRetainedRoute = ReferenceEquals(drawRoute, fixture.DrawRoute);
        var drawProtectsSidebar = Protected(Shapes(fixture.Drawing.Drawing, Matrix.Identity, null)
            .Where(shape => IsBack(shape.Drawing)));
        Console.WriteLine("Sidebar route work: " + JsonSerializer.Serialize(new
        {
            RootChildren = fixture.Root.Children.Count, SidebarChildren = contents.Children.Count,
            liveReads, liveSidebarReads, retiredReads, retiredSidebarReads,
            textRetainedRoute, scrollRetainedRoute, retiredRetainedRoute, drawRetainedRoute,
            slashProtectsSidebar, drawProtectsSidebar, retiredInk, ScrollOffset = scroll.VerticalOffset
        }));
        Assert(textRetainedRoute && scrollRetainedRoute && drawRetainedRoute && slashProtectsSidebar && drawProtectsSidebar,
            "Changes wholly inside the protected sidebar must retain Slash/draw routes, while both clips still exclude its complete rectangle.");
        Assert(!retiredInk && retiredRetainedRoute && retiredReads <= fixture.Root.Children.Count &&
            retiredSidebarReads <= contents.Children.Count,
            "At 700ms the retired Slash must leave only the anchor walk, without reading avoidance ink or rebuilding its route.");

        IEnumerable<(GeometryDrawing Drawing, Geometry Visible, Geometry? Clip)> SlashShapes() =>
            Shapes(fixture.Drawing.Drawing, Matrix.Identity, null).Where(shape => shape.Drawing is
                { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen: null } && fill.Color == Color.FromRgb(255, 203, 100));

        bool Protected(IEnumerable<(GeometryDrawing Drawing, Geometry Visible, Geometry? Clip)> candidates)
        {
            var shapes = candidates.ToArray();
            var bounds = new RectangleGeometry(fixture.Bounds(sidebar));
            return shapes.Length > 0 && shapes.Any(shape => shape.Visible.GetArea() > 1) &&
                shapes.All(shape => shape.Clip is not null &&
                    Geometry.Combine(shape.Clip, bounds, GeometryCombineMode.Intersect, null).GetArea() < .01);
        }
    }

    private static void CheckRoundedDistanceTie()
    {
        var method = typeof(BattleFeedbackLayer).Assembly.GetType("CardGame.Wpf.Controls.BattleTextLayout")!
            .GetMethod("TryPlace", BindingFlags.Public | BindingFlags.Static)!;
        // Vertical distances 100 and 99.99999999999972 both round to 40100
        // after adding the horizontal distance. Preserve the earlier y=90
        // candidate, rather than treating the smaller vertical distance as a win.
        Rect[] occupied = [new(16, 0, 384, 300), new(0, 104, 10, 2),
            new(100, 106, 1, 1), new(100, 103 - Math.ScaleB(1d, -46), 1, 1)];
        object?[] arguments = [new Size(10, 10), new Point(205, 105), new Rect(0, 0, 400, 300), occupied, Rect.Empty];
        var placed = (bool)method.Invoke(null, arguments)!;
        var result = (Rect)arguments[4]!;
        Assert(placed && result == new Rect(0, 90, 10, 10),
            $"Equal rounded total distances must preserve the original vertical candidate order; actual placement={result}.");
    }

    private static SourceSample[] MeasureDifferentSources(string output)
    {
        // This is a deterministic render fixture with eight real seat anchors,
        // not an eight-player engine run or native MainWindow acceptance.
        using var fixture = new Scene(eightSeats: true);
        var result = new List<SourceSample>();
        foreach (var seats in new[] { new[] { 0 }, new[] { 0, 2, 4, 6 }, Enumerable.Range(0, 8).ToArray() })
        {
            for (var index = 0; index < 2; index++)
            { fixture.SetSourceCues(seats); fixture.Render(); }
            CaptionSample? firstCaptions = null;
            var first = MeasureFrames(8, reset: true);
            var steady = MeasureFrames(16, reset: false);
            var steadyCaptions = Captions(fixture);
            result.Add(new(seats.Length, seats, first, steady, firstCaptions!, steadyCaptions));

            Timing MeasureFrames(int frames, bool reset)
            {
                long allocated = 0; double elapsed = 0, maximum = 0; var reads = 0;
                for (var index = 0; index < frames; index++)
                {
                    if (reset)
                    {
                        // Reset after warmup and before *each* measured first
                        // render. Age is 200ms; age zero would skip text entirely.
                        fixture.SetSourceCues(seats);
                        Assert(fixture.TextOffsetCount == 0,
                            "A first-layout sample retained TextOffset from a warmup or previous cue batch.");
                    }
                    fixture.Root.ChildReads = 0;
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var started = Stopwatch.GetTimestamp();
                    fixture.Render();
                    var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                    elapsed += milliseconds; maximum = Math.Max(maximum, milliseconds);
                    reads += fixture.Root.ChildReads;
                    if (reset && index == 0)
                    {
                        firstCaptions = Captions(fixture);
                        fixture.SaveFrame(Path.Combine(output, $"battle-frame-geometry-sources-{seats.Length}-first-200ms.png"));
                    }
                }
                return new(frames, reads / frames, elapsed / frames, maximum, allocated / frames);
            }
        }
        Console.WriteLine("Synthetic eight-seat draw sources: 200ms visual age, 2 fresh warmups; " +
            "first=8 independently reset TextOffset-null frames, steady=16 frames reusing the final placement. " +
            "Timing excludes cue reset, caption inspection and PNG encoding.");
        return result.ToArray();
    }

    private static CaptionSample Captions(Scene fixture)
    {
        var items = DrawingItems(fixture.Drawing.Drawing, Matrix.Identity, 1).ToArray();
        var viewport = new Rect(0, 0, Scene.Width, Scene.Height);
        var captions = items.Where(item => item.Opacity > .01 && item.Drawing is GeometryDrawing shape && IsCaption(shape))
            .Select(item => new MatrixTransform(item.Transform).TransformBounds(item.Drawing.Bounds))
            .Where(viewport.Contains).ToArray();
        var glyphs = items.Where(item => item.Opacity > .01 && item.Drawing is GlyphRunDrawing)
            .Select(item => new MatrixTransform(item.Transform).TransformBounds(item.Drawing.Bounds)).ToArray();
        // The title occupies the upper portion; the small actor name alone
        // must not make a missing draw title count as displayed text.
        var withText = captions.Count(caption => glyphs.Any(glyph => !glyph.IsEmpty &&
            caption.Contains(Center(glyph)) && Center(glyph).Y < caption.Top + caption.Height * .6));
        return new(captions.Length, withText, fixture.TextOffsetCount);
    }

    private static IEnumerable<(Drawing Drawing, Matrix Transform, double Opacity)> DrawingItems(
        Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is DrawingGroup group)
        {
            var nested = group.Transform?.Value ?? Matrix.Identity; nested.Append(transform);
            foreach (var child in group.Children)
                foreach (var item in DrawingItems(child, nested, opacity * group.Opacity)) yield return item;
        }
        else if (drawing is not null) yield return (drawing, transform, opacity);
    }

    private static void CheckMovingReadouts(Scene fixture, string output)
    {
        fixture.SetCues(1); fixture.Render();
        var back = Shapes(fixture.Drawing.Drawing, Matrix.Identity, null).First(shape => IsBack(shape.Drawing));
        var owner = fixture.Bounds(fixture.Owner);
        var openArtwork = new Point(owner.Left + owner.Width * .28, owner.Top + owner.Height * .24);
        var label = fixture.Bounds((TextBlock)fixture.Owner.Content);
        Assert(back.Clip is not null && back.Clip.FillContains(openArtwork) && !back.Clip.FillContains(Center(label)),
            "Draw clipping must leave owner artwork open while protecting the inner text of its seat button.");
        var target = VisiblePoint(back.Visible);
        foreach (var control in new FrameworkElement[] { fixture.Command, fixture.Readout })
        {
            var original = ProtectedBounds(control);
            var transform = (TranslateTransform)control.RenderTransform;
            transform.X += target.X - Center(original).X;
            transform.Y += target.Y - Center(original).Y;
            // Deliberately no Measure/Arrange/UpdateLayout: render transforms do
            // not provide the layout invalidation a persistent cache would need.
            fixture.Render();
            var moved = ProtectedBounds(control);
            Assert(moved.Contains(target), $"The moved {control.GetType().Name} must occupy the formerly visible card-back point: before={original}, after={moved}, target={target}, translation={transform.X},{transform.Y}.");
            Assert(Shapes(fixture.Drawing.Drawing, Matrix.Identity, null).Where(shape => IsBack(shape.Drawing) || IsCaption(shape.Drawing))
                .All(shape => Geometry.Combine(shape.Visible, new RectangleGeometry(moved), GeometryCombineMode.Intersect, null).GetArea() < .01),
                "A frame must protect the complete current button and the actual text glyph bounds, including after RenderTransform changes.");
            if (control == fixture.Command) fixture.SaveFrame(Path.Combine(output, "battle-frame-geometry-moved-command.png"));
            control.Visibility = Visibility.Hidden;
            fixture.Render();
            Assert(Shapes(fixture.Drawing.Drawing, Matrix.Identity, null).Any(shape => IsBack(shape.Drawing) && shape.Visible.FillContains(target)),
                "Hiding a protected readout must reopen that space in the very next frame.");
        }

        Rect ProtectedBounds(FrameworkElement control)
        {
            if (control is not TextBlock text) return fixture.Bounds(control);
            var ink = Rect.Empty;
            foreach (var item in DrawingItems(VisualTreeHelper.GetDrawing(text), Matrix.Identity, 1))
                if (item.Drawing is GlyphRunDrawing glyph)
                    ink.Union(new MatrixTransform(item.Transform).TransformBounds(glyph.GlyphRun.BuildGeometry().Bounds));
            Assert(!ink.IsEmpty, "The moving readout fixture must have actual rendered glyphs to protect.");
            return text.TransformToVisual(fixture.Layer).TransformBounds(ink);
        }
    }

    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static bool IsBack(GeometryDrawing drawing) => drawing.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(48, 39, 29);
    private static bool IsCaption(GeometryDrawing drawing) => drawing.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(64, 50, 34);
    private static Point VisiblePoint(Geometry geometry)
    {
        var bounds = geometry.Bounds;
        for (var row = 1; row < 10; row++)
            for (var column = 1; column < 10; column++)
            {
                var point = new Point(bounds.Left + bounds.Width * column / 10, bounds.Top + bounds.Height * row / 10);
                if (geometry.FillContains(point)) return point;
            }
        throw new InvalidOperationException("The frame fixture has no visible traveling card-back point.");
    }

    private static IEnumerable<(GeometryDrawing Drawing, Geometry Visible, Geometry? Clip)> Shapes(Drawing? drawing, Matrix transform, Geometry? clip)
    {
        if (drawing is GeometryDrawing shape)
        {
            var geometry = shape.Geometry.Clone(); geometry.Transform = new MatrixTransform(transform);
            yield return (shape, clip is null ? geometry : Geometry.Combine(geometry, clip, GeometryCombineMode.Intersect, null), clip);
        }
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            var nextClip = clip;
            if (group.ClipGeometry is { } local)
            {
                var actual = local.Clone(); actual.Transform = new MatrixTransform(next);
                nextClip = clip is null ? actual : Geometry.Combine(clip, actual, GeometryCombineMode.Intersect, null);
            }
            foreach (var child in group.Children)
                foreach (var nested in Shapes(child, next, nextClip)) yield return nested;
        }
    }

    private sealed class Scene : IDisposable
    {
        public const int Width = 1200;
        public const int Height = 800;
        public CountingCanvas Root { get; } = new() { Width = Width, Height = Height, Background = Brushes.DarkSlateGray };
        public BattleFeedbackLayer Layer { get; }
        public DrawingVisual Drawing { get; } = new();
        public Button Owner { get; }
        public Button Command { get; }
        public TextBlock Readout { get; }
        private readonly ObservableCollection<BattleCue> _cues = [];
        private readonly FrameClock _clock = new();
        private readonly Action<DrawingContext> _render;
        private readonly Grid _host;

        public Scene(bool eightSeats = false)
        {
            var pile = Place(new Border { Width = 64, Height = 82, Background = Brushes.DimGray }, eightSeats ? 568 : 540, eightSeats ? 240 : 70);
            var hand = Place(new Border { Width = 800, Height = 100, BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1) }, 300, 680);
            var arena = Place(new Border { Width = 600, Height = 360 }, 300, 200);
            var commands = Place(new Border { Width = 800, Height = 54 }, 200, 610);
            if (eightSeats)
            {
                Point[] positions = [new(32, 602), new(24, 375), new(24, 150), new(225, 20),
                    new(525, 20), new(825, 20), new(1034, 150), new(1034, 375)];
                var seats = new List<Button>();
                for (var seat = 0; seat < positions.Length; seat++)
                {
                    var content = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8) };
                    content.Children.Add(new TextBlock { Text = $"座位 {seat + 1}", FontSize = 15, Foreground = Brushes.Wheat });
                    content.Children.Add(new TextBlock { Text = "体力 4 / 4 · 手牌 4", FontSize = 11, Foreground = Brushes.Wheat });
                    content.Children.Add(new HealthMeter { Value = 4, Maximum = 4, Height = 7 });
                    var portrait = Place(new Button { Width = 140, Height = 158, Content = content,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                        Background = Brushes.SlateGray }, positions[seat].X, positions[seat].Y);
                    BattleFeedbackLayer.SetSeatAnchor(portrait, seat); seats.Add(portrait);
                }
                Owner = seats[0];
                Readout = Place(new TextBlock { Text = "第 3 回合 · 出牌阶段", Width = 190, Height = 25,
                    Foreground = Brushes.Wheat, RenderTransform = new TranslateTransform() }, 410, 195);
                Place(new TextBlock { Text = "牌堆 76 · 弃牌 24", Width = 160, Height = 22, Foreground = Brushes.Wheat }, 655, 255);
                var handCards = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                for (var index = 0; index < 6; index++)
                    handCards.Children.Add(new Border { Width = 76, Height = 96, Margin = new Thickness(5, 1, 5, 1),
                        Background = Brushes.Tan, BorderBrush = Brushes.Wheat, BorderThickness = new Thickness(1) });
                hand.Child = handCards;
                Place(new Button { Width = 120, Height = 36, Content = "结束出牌" }, 670, 620);
                Place(new Button { Width = 100, Height = 36, Content = "取消选择" }, 410, 620);
                Command = Place(new Button { Width = 120, Height = 36, Content = "确认出牌", RenderTransform = new TranslateTransform() }, 530, 620);
            }
            else
            {
                Owner = Place(new Button { Width = 180, Height = 190, Content = new TextBlock { Text = "主公 · 4 / 4", FontSize = 14 }, Background = Brushes.SlateGray }, 80, 420);
                var opponent = Place(new Button { Width = 180, Height = 190, Content = "对手", Background = Brushes.SlateGray }, 930, 110);
                BattleFeedbackLayer.SetSeatAnchor(Owner, 0);
                BattleFeedbackLayer.SetSeatAnchor(opponent, 1);
                Command = Place(new Button { Width = 120, Height = 36, Content = "确认出牌", RenderTransform = new TranslateTransform() }, 530, 620);
                for (var index = 0; index < 32; index++)
                    Place(new TextBlock { Width = 90, Height = 20, Text = $"公开读数 {index + 1}", Foreground = Brushes.Wheat }, 20 + index % 8 * 145, 10 + index / 8 * 24);
                Readout = Root.Children.OfType<TextBlock>().First();
                Readout.RenderTransform = new TranslateTransform();
            }
            Layer = new BattleFeedbackLayer
            {
                Width = Width, Height = Height, AnchorRoot = Root, DrawPileAnchor = pile,
                HandRoot = hand, HandOwnerSeat = 0, CenterAnchor = arena, ActionBar = commands,
                Cues = _cues, Clock = _clock
            };
            _host = new Grid { Width = Width, Height = Height };
            _host.Children.Add(Root); _host.Children.Add(Layer);
            _host.Measure(new Size(Width, Height)); _host.Arrange(new Rect(0, 0, Width, Height)); _host.UpdateLayout();
            _render = typeof(BattleFeedbackLayer).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<DrawingContext>>(Layer);
        }

        public T Place<T>(T element, double x, double y) where T : FrameworkElement
        { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Root.Children.Add(element); return element; }

        public void SetCues(int count)
        {
            _cues.Clear();
            for (var index = 0; index < count; index++)
                _cues.Add(new BattleCue(index + 1, BattleCueKind.Draw, 0, [0], "摸牌 +2", "主公") { Revision = index + 1, DrawCount = 2 });
            _clock.Advance(.2);
            Assert(Layer.ActiveEffectCount == count, "The frame fixture must retain every requested concurrent draw cue.");
        }

        public void SetSourceCues(int[] seats)
        {
            _cues.Clear();
            for (var index = 0; index < seats.Length; index++)
                _cues.Add(new BattleCue(index + 1, BattleCueKind.Draw, seats[index], [seats[index]], "摸牌 +2", $"座位 {seats[index] + 1}")
                    { Revision = index + 1, DrawCount = 2 });
            _clock.Advance(.2);
            Assert(Layer.ActiveEffectCount == seats.Length, "The frame fixture must retain every requested concurrent draw cue.");
        }

        public void SetSlashCue()
        {
            _cues.Clear();
            _cues.Add(new BattleCue(1, BattleCueKind.Card, 1, [0], "杀", "对手") { Revision = 1, CardKind = CardKind.Slash });
            _clock.Advance(.2);
        }

        public object? SlashRoute
        {
            get
            {
                var active = ((System.Collections.IEnumerable)typeof(BattleFeedbackLayer)
                    .GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Layer)!).Cast<object>().Single();
                return (active.GetType().GetField("_slashPaths", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(active) as System.Collections.IDictionary)?[0];
            }
        }

        public object? DrawRoute => ((System.Collections.IDictionary)typeof(BattleFeedbackLayer)
            .GetField("_drawRouteCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Layer)!)[0];

        public void Advance(double seconds) => _clock.Advance(seconds);

        public void Relayout()
        {
            _host.Measure(new Size(Width, Height)); _host.Arrange(new Rect(0, 0, Width, Height)); _host.UpdateLayout();
            // Real text drawings, including ScrollViewer offsets, supply the ink.
            new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32).Render(Root);
        }

        public int TextOffsetCount => ((System.Collections.IEnumerable)typeof(BattleFeedbackLayer)
            .GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Layer)!).Cast<object>()
            .Count(item => item.GetType().GetProperty("TextOffset")!.GetValue(item) is not null);

        public void Render() { using var context = Drawing.RenderOpen(); _render(context); }
        public Rect Bounds(FrameworkElement element) => element.TransformToVisual(Layer).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        public void SaveFrame(string path)
        {
            var background = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            background.Render(Root);
            var composite = new DrawingVisual();
            using (var context = composite.RenderOpen())
            { context.DrawImage(background, new Rect(0, 0, Width, Height)); context.DrawDrawing(Drawing.Drawing); }
            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(composite);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path); encoder.Save(file);
        }

        public void Dispose() { Layer.Cues = null; _host.Children.Clear(); Root.Children.Clear(); }
    }

    private sealed class CountingCanvas : Canvas
    {
        public int ChildReads { get; set; }
        protected override Visual GetVisualChild(int index) { ChildReads++; return base.GetVisualChild(index); }
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
