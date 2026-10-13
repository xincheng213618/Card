using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using static Program;

internal static class ElementalImpactReadabilityChecks
{
    private static readonly MethodInfo Paint = typeof(BattleFeedbackLayer).Assembly
        .GetType("CardGame.Wpf.Controls.BattleEffectPainter", throwOnError: true)!
        .GetMethod("Draw", BindingFlags.Static | BindingFlags.Public)!;

    public static void PortraitBoundsAndDecay(string output)
    {
        foreach (var (name, size) in new[]
        {
            ("compact", new Size(92, 124)), ("standard", new Size(154, 204)),
            ("wide", new Size(220, 120)), ("tall", new Size(154, 280))
        })
        {
            var target = new Rect(100, 80, size.Width, size.Height);
            var clear = Capture(target, null, 0);
            byte[]? firePixels = null;
            foreach (var nature in new[] { DamageNature.Fire, DamageNature.Thunder })
            {
                var cue = new BattleCue(1, BattleCueKind.Damage, 0, [1], "−1", "公开命中", nature);
                var early = Capture(target, cue, .1);
                var afterBody = Capture(target, cue, .5);
                Save(early.Bitmap, Path.Combine(output, $"elemental-impact-{name}-{nature.ToString().ToLowerInvariant()}.png"));
                Assert(Changed(clear.Pixels, early.Pixels, early.Width, early.Height, point => !target.Contains(point)) == 0,
                    $"{nature} impact escaped the {name} target portrait into the header or a neighboring seat.");
                foreach (var (regionName, bounds) in MetadataRegions(target))
                    Assert(Changed(clear.Pixels, early.Pixels, early.Width, early.Height, bounds.Contains) == 0,
                        $"{nature} impact painted over the {name} portrait's {regionName}.");
                Assert(Changed(clear.Pixels, early.Pixels, early.Width, early.Height, target.Contains) > 100 &&
                    ElementalPixels(clear.Pixels, early.Pixels, nature) > 50,
                    $"{nature} became imperceptible or lost its recognizable elemental color in the {name} portrait.");
                Assert(afterBody.Pixels.AsSpan().SequenceEqual(clear.Pixels),
                    $"An old {nature} impact retained its body glow while the next chained recipient could already be hit.");
                if (nature == DamageNature.Fire) firePixels = early.Pixels;
                else Assert(firePixels is not null && !early.Pixels.AsSpan().SequenceEqual(firePixels),
                    "Fire and Thunder produced the same portrait impact frame.");
            }
            var recovery = Capture(target, new(2, BattleCueKind.Recovery, 0, [1], "+1", "公开回复"), .5);
            Assert(Changed(clear.Pixels, recovery.Pixels, clear.Width, clear.Height, target.Contains) > 100,
                "Shortening damage impact accidentally shortened the existing recovery presentation.");
            var relay = Capture(target, new(3, BattleCueKind.ChainPropagation, 0, [1], "连环传导", "公开传导", DamageNature.Thunder)
                { OriginSeat = 0 }, .18);
            Assert(Changed(clear.Pixels, relay.Pixels, clear.Width, clear.Height, point => !target.Contains(point)) > 30,
                "Bounding portrait impact accidentally clipped the real public relay between the two portraits.");
        }
        Console.WriteLine("Elemental impact pixels: four controlled portrait aspect ratios; header/neighbor/name/HP/identity pixels unchanged; distinct visible Fire/Thunder; old damage body gone, recovery/relay retained.");
    }

    private static IEnumerable<(string Name, Rect Bounds)> MetadataRegions(Rect target)
    {
        yield return ("identity", new(target.Left + 5, target.Top + 5, target.Width - 10, target.Height * .16 - 5));
        yield return ("vertical name", new(target.Left + target.Width * .04, target.Top + target.Height * .2,
            target.Width * .17, target.Height * .44));
        yield return ("health and hand count", new(target.Left + 5, target.Top + target.Height * .72,
            target.Width - 10, target.Height * .26 - 5));
        yield return ("right health pips", new(target.Left + target.Width * .87, target.Top + target.Height * .2,
            target.Width * .09, target.Height * .44));
    }

    private static (RenderTargetBitmap Bitmap, byte[] Pixels, int Width, int Height) Capture(Rect target, BattleCue? cue, double progress)
    {
        var width = (int)Math.Ceiling(target.Width * 2 + 300); var height = (int)Math.Ceiling(target.Height + 180);
        var neighbor = new Rect(target.Right + 24, target.Top, target.Width, target.Height);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(73, 64, 48)), null, new Rect(0, 0, width, height));
            Portrait(dc, target); Portrait(dc, neighbor);
            dc.DrawText(Text("出牌阶段", 18), new Point(20, 20));
            if (cue is not null) Paint.Invoke(null, [dc, cue, progress, neighbor,
                new Dictionary<int, Rect> { [0] = neighbor, [1] = target }]);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        return (bitmap, pixels, width, height);
    }

    private static void Portrait(DrawingContext dc, Rect target)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(33, 29, 24)), new Pen(Brushes.DarkGoldenrod, 1), target);
        dc.DrawText(Text("魏", 11), new Point(target.Left + 6, target.Top + 5));
        dc.DrawText(Text("忠", 11), new Point(target.Right - 17, target.Top + 5));
        dc.DrawText(Text("关\n羽", 12), new Point(target.Left + 6, target.Top + target.Height * .25));
        dc.DrawText(Text("3/4", 10), new Point(target.Left + 6, target.Top + target.Height * .74));
        dc.DrawText(Text("牌 3", 10), new Point(target.Right - 33, target.Top + target.Height * .74));
        dc.DrawRectangle(Brushes.DarkSeaGreen, null,
            new Rect(target.Left + 6, target.Top + target.Height * .89, target.Width * .6, 4));
    }

    private static FormattedText Text(string text, double size) => new(text, System.Globalization.CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Beige, 1);

    private static int Changed(byte[] clear, byte[] frame, int width, int height, Func<Point, bool> within)
    {
        var count = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            if (!within(new Point(x + .5, y + .5))) continue;
            var offset = (y * width + x) * 4;
            if (!clear.AsSpan(offset, 4).SequenceEqual(frame.AsSpan(offset, 4))) count++;
        }
        return count;
    }

    private static int ElementalPixels(byte[] clear, byte[] frame, DamageNature nature)
    {
        var count = 0;
        for (var offset = 0; offset < frame.Length; offset += 4)
        {
            if (clear.AsSpan(offset, 4).SequenceEqual(frame.AsSpan(offset, 4))) continue;
            var blue = frame[offset]; var green = frame[offset + 1]; var red = frame[offset + 2];
            if (nature == DamageNature.Fire ? red > green + 25 && red > blue + 25 : blue > red + 20 && blue > green + 20) count++;
        }
        return count;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
