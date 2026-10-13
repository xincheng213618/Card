using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using static Program;

internal static class BattleTextReadabilityChecks
{
    private static readonly Color Damage = Color.FromRgb(255, 126, 99);
    private static readonly Color Recovery = Color.FromRgb(145, 227, 170);
    private static readonly Color Skill = Color.FromRgb(255, 249, 218);

    public static void Numbers(string output)
    {
        var (root, layer, cues, clock) = Scene();
        cues.Add(new(1, BattleCueKind.Damage, 0, [2], "−2", "黄盖"));
        clock.Advance(.2);
        cues.Add(new(2, BattleCueKind.Recovery, 2, [2], "+1", "黄盖"));
        clock.Advance(.15);
        var firstFrame = Capture(root, layer, Path.Combine(output, "battle-text-consecutive.png"));
        var first = Numbers(layer).ToArray();
        Console.WriteLine($"Consecutive number bounds: {string.Join("; ", first.Select(item => item.Bounds))}.");
        Assert(first.Length == 2 && !first[0].Bounds.IntersectsWith(first[1].Bounds),
            "Consecutive damage and recovery numbers still overlap.");
        // Compare the glyph origin before its pop scale; its visible width naturally changes with time.
        var survivorX = first.Single(item => item.Color == Recovery).OriginX;
        var repeatedFrame = Capture(root, layer);
        Assert(Pixels(firstFrame).SequenceEqual(Pixels(repeatedFrame)),
            "Redrawing the same floating results at the same clock time changed their visible glyphs, captions or placement.");
        clock.Advance(.04);
        Capture(root, layer);
        var advanced = Numbers(layer).ToArray();
        Assert(advanced.Length == 2 && advanced.Single(item => item.Color == Damage).Bounds.Top <
            first.Single(item => item.Color == Damage).Bounds.Top &&
            advanced.All(item => Protected(layer).All(area => !area.IntersectsWith(item.Bounds))),
            "Reused floating text must keep both results and their upward motion without covering a protected area.");
        clock.Advance(.85);
        Capture(root, layer);
        var after = Numbers(layer).Single();
        Assert(Math.Abs(after.OriginX - survivorX) < .1,
            "A surviving floating number jumped sideways when an earlier number expired.");

        // A live command area can move during layout; cached text must get out of its way.
        Canvas.SetLeft(layer.ActionBar!, after.Bounds.Left - 10);
        Canvas.SetTop(layer.ActionBar!, after.Bounds.Top - 10);
        Capture(root, layer);
        var relocated = Numbers(layer).ToArray();
        Assert(relocated.Length == 1 && relocated.All(item => Protected(layer).All(area => !area.IntersectsWith(item.Bounds))),
            "Cached floating text covered command buttons after the layout changed.");
        Canvas.SetLeft(layer.ActionBar!, 508); Canvas.SetTop(layer.ActionBar!, 385);

        cues.Clear();
        layer.IsPromptVisible = true;
        var sequence = 10L;
        foreach (var seat in new[] { 0, 1, 2 })
            foreach (var kind in new[] { BattleCueKind.Damage, BattleCueKind.Recovery, BattleCueKind.Damage })
                cues.Add(new(sequence++, kind, 0, [seat], kind == BattleCueKind.Damage ? "−12" : "+3",
                    seat == 0 ? "关羽" : seat == 1 ? "曹操" : "黄盖"));
        clock.Advance(.17);
        Capture(root, layer, Path.Combine(output, "battle-text-edge-crowd.png"));
        var crowd = Numbers(layer).ToArray();
        Assert(crowd.Length == 9, $"The visible text layout lost public HP results ({crowd.Length}/9).");
        var viewport = new Rect(0, 0, 850, 500);
        var protectedAreas = Protected(layer).ToArray();
        for (var i = 0; i < crowd.Length; i++)
        {
            Assert(viewport.Contains(crowd[i].Bounds) && protectedAreas.All(area => !area.IntersectsWith(crowd[i].Bounds)),
                "A floating number left the viewport or covered a live decision/command area.");
            for (var j = i + 1; j < crowd.Length; j++)
                Assert(!crowd[i].Bounds.IntersectsWith(crowd[j].Bounds), "Different public HP numbers overlap near the table edges.");
        }
        var originalDpi = VisualTreeHelper.GetDpi(root);
        var targetDpi = new DpiScale(originalDpi.DpiScaleX + .5, originalDpi.DpiScaleY + .5);
        Assert(Math.Abs(targetDpi.PixelsPerDip - originalDpi.PixelsPerDip) > .001,
            "The logical DPI fixture must change the current density rather than redraw at its existing value.");
        try
        {
            // Exercise the actual drawing at another logical density without
            // requiring a physical monitor transition or inspecting its cache.
            VisualTreeHelper.SetRootDpi(root, targetDpi);
            Capture(root, layer);
            VerifyNumberDpi(targetDpi.PixelsPerDip);
        }
        finally
        {
            VisualTreeHelper.SetRootDpi(root, originalDpi);
            Capture(root, layer);
            VerifyNumberDpi(originalDpi.PixelsPerDip);
        }
        Console.WriteLine($"Floating result logical DPI transition: {originalDpi.PixelsPerDip} -> " +
            $"{targetDpi.PixelsPerDip} -> {VisualTreeHelper.GetDpi(layer).PixelsPerDip}.");
        layer.MotionEnabled = false;
        Capture(root, layer);
        Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && !Numbers(layer).Any() && !layer.IsHitTestVisible,
            "Motion off retained text positions, animation, or intercepted table input.");
        Console.WriteLine("Battle text numbers: separate results, stable survivor position, nine edge/crowd results and protected areas passed.");

        void VerifyNumberDpi(double expectedPixelsPerDip)
        {
            var captions = TextRuns(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).ToArray();
            Assert(Math.Abs(VisualTreeHelper.GetDpi(layer).PixelsPerDip - expectedPixelsPerDip) < .001 &&
                captions.Length >= 9 && captions.All(item => Math.Abs(item.Drawing.GlyphRun.PixelsPerDip - expectedPixelsPerDip) < .001),
                "Every actual floating caption must use the current logical density after changing or restoring root DPI.");
            var dpiCrowd = Numbers(layer).ToArray();
            var dpiProtected = Protected(layer).ToArray();
            Assert(dpiCrowd.Length == 9 && dpiCrowd.All(item =>
                double.IsFinite(item.Bounds.X) && double.IsFinite(item.Bounds.Y) &&
                double.IsFinite(item.Bounds.Width) && double.IsFinite(item.Bounds.Height) &&
                viewport.Contains(item.Bounds) && dpiProtected.All(area => !area.IntersectsWith(item.Bounds))),
                "Changing or restoring logical DPI must retain all nine finite floating results inside the viewport and clear of protected areas.");
            for (var i = 0; i < dpiCrowd.Length; i++)
                for (var j = i + 1; j < dpiCrowd.Length; j++)
                    Assert(!dpiCrowd[i].Bounds.IntersectsWith(dpiCrowd[j].Bounds),
                        "Floating results overlapped after changing or restoring logical DPI.");
            Console.WriteLine($"Floating result logical DPI: {expectedPixelsPerDip}, captions={captions.Length}, results={dpiCrowd.Length}.");
        }
    }

    public static void SkillBanners(string output)
    {
        var (root, layer, cues, clock) = Scene();
        layer.IsPromptVisible = true;
        cues.Add(new(1, BattleCueKind.Skill, 2, [2], "贯石斧·连续响应公开长技能", "黄盖") { SkillId = "public:long-skill" });
        clock.Advance(.4);
        Capture(root, layer, Path.Combine(output, "battle-text-long-skill.png"));
        var runs = TextRuns(VisualTreeHelper.GetDrawing(layer), Matrix.Identity)
            .Where(item => item.Drawing.ForegroundBrush is SolidColorBrush brush && brush.Color == Skill).ToArray();
        Assert(runs.Length > 0, "The public skill name was not drawn.");
        var areas = Protected(layer).Concat(Find<HealthMeter>(root).Select(m => m.TransformToVisual(layer).TransformBounds(new Rect(m.RenderSize)))).ToArray();
        foreach (var run in runs)
            Assert(new Rect(0, 0, 850, 500).Contains(run.Bounds) && areas.All(area => !area.IntersectsWith(run.Bounds)),
                "A skill name was clipped at the bottom/right edge or covered a health/command area.");
        cues.Add(new(2, BattleCueKind.Damage, 0, [2], "−1", "黄盖"));
        clock.Advance(.1);
        Capture(root, layer, Path.Combine(output, "battle-text-skill-and-damage.png"));
        var number = Numbers(layer).Single().Bounds;
        var skillRuns = TextRuns(VisualTreeHelper.GetDrawing(layer), Matrix.Identity)
            .Where(item => item.Drawing.ForegroundBrush is SolidColorBrush brush && brush.Color == Skill).ToArray();
        Assert(skillRuns.Length > 0 && skillRuns.All(item => !item.Bounds.IntersectsWith(number)),
            "A skill banner swallowed its owner's concurrent HP result.");
        Assert(!layer.IsHitTestVisible && !layer.Focusable, "Readable skill text intercepted gameplay.");
        cues.Clear(); clock.Advance(2);
        Capture(root, layer);
        Assert(!TextRuns(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Any() && layer.ActiveEffectCount == 0,
            "Clearing the table left a skill name behind.");
        Console.WriteLine("Battle skill text: long public name stays inside the scene, clear of HP/commands and simultaneous damage.");
    }

    private static (Canvas Root, BattleFeedbackLayer Layer, ObservableCollection<BattleCue> Cues, FrameClock Clock) Scene()
    {
        var root = new Canvas { Width = 850, Height = 500, Background = new SolidColorBrush(Color.FromRgb(16, 26, 29)) };
        var positions = new[] { new Rect(5, 5, 100, 132), new Rect(725, 5, 120, 132), new Rect(710, 358, 135, 137) };
        for (var i = 0; i < positions.Length; i++)
        {
            var rect = positions[i];
            var meter = new HealthMeter { Value = 3, Maximum = 4, Height = 7, Margin = new Thickness(7, 0, 7, 3), VerticalAlignment = VerticalAlignment.Bottom };
            BattleFeedbackLayer.SetHealthAnchor(meter, i);
            var face = new Grid();
            face.Children.Add(new TextBlock { Text = i == 0 ? "关羽" : i == 1 ? "曹操" : "黄盖", Foreground = Brushes.Wheat,
                FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            face.Children.Add(meter);
            var seat = new Border { Width = rect.Width, Height = rect.Height, Background = Brushes.DarkSlateGray,
                BorderBrush = Brushes.Olive, BorderThickness = new Thickness(1), Child = face };
            BattleFeedbackLayer.SetSeatAnchor(seat, i); Place(root, seat, rect.Left, rect.Top);
        }
        var action = new Border { Width = 195, Height = 105, Background = Brushes.Teal, Child = new TextBlock { Text = "操作按钮", Foreground = Brushes.Wheat } };
        Place(root, action, 508, 385);
        var decision = new Border { Width = 335, Height = 110, Background = Brushes.DarkOliveGreen, Child = new TextBlock { Text = "当前决策", Foreground = Brushes.Wheat } };
        Place(root, decision, 230, 183);
        var cues = new ObservableCollection<BattleCue>();
        var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 850, Height = 500, Cues = cues, AnchorRoot = root, ActionBar = action, CenterAnchor = decision, Clock = clock };
        root.Children.Add(layer);
        Capture(root, layer);
        return (root, layer, cues, clock);
    }
    private static IEnumerable<Rect> Protected(BattleFeedbackLayer layer)
    {
        foreach (var element in new[] { layer.ActionBar, layer.SupplementalActions, layer.IsPromptVisible ? layer.CenterAnchor : null })
            if (element is not null)
                yield return element.TransformToVisual(layer).TransformBounds(new Rect(element.RenderSize));
    }
    private static void Place(Canvas canvas, FrameworkElement child, double x, double y)
    { Canvas.SetLeft(child, x); Canvas.SetTop(child, y); canvas.Children.Add(child); }

    private static IEnumerable<(Color Color, Rect Bounds, double OriginX)> Numbers(BattleFeedbackLayer layer) =>
        Shapes(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).Where(item =>
            item.Drawing.Pen is null && item.Drawing.Brush is SolidColorBrush brush && (brush.Color == Damage || brush.Color == Recovery))
        .Select(item => (((SolidColorBrush)item.Drawing.Brush!).Color, item.Bounds, item.Drawing.Geometry.Bounds.X));
    private static IEnumerable<(GeometryDrawing Drawing, Rect Bounds)> Shapes(Drawing? drawing, Matrix transform)
    {
        if (drawing is GeometryDrawing shape)
            yield return (shape, new MatrixTransform(transform).TransformBounds(shape.Geometry.Bounds));
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in Shapes(child, next)) yield return nested;
        }
    }
    private static IEnumerable<(GlyphRunDrawing Drawing, Rect Bounds)> TextRuns(Drawing? drawing, Matrix transform)
    {
        if (drawing is GlyphRunDrawing run)
            yield return (run, new MatrixTransform(transform).TransformBounds(run.Bounds));
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in TextRuns(child, next)) yield return nested;
        }
    }
    private static RenderTargetBitmap Capture(Canvas root, BattleFeedbackLayer layer, string? path = null)
    {
        layer.InvalidateVisual();
        root.Measure(new Size(850, 500)); root.Arrange(new Rect(0, 0, 850, 500)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(850, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        if (path is not null)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path); encoder.Save(file);
        }
        return bitmap;
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var bytes = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(bytes, stride, 0);
        return bytes;
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
