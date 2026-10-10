using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class BattleEffectsChecks
{
    public static void PublicSkills(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var view = Engine(vm).CreateSnapshot(0);
        var skill = new GeneralSkillDefinition("武圣", "公开技能投影测试") { ContentId = "effects:wusheng" };
        var owner = view.Players[0] with { IsGeneralPublic = true, Skills = new[] { skill } };
        view = view with { Players = view.Players.Select(player => player.Seat == owner.Seat ? owner : player).ToArray() };
        var skillId = skill.ContentId!;
        long sequence = 0;
        EventEnvelope Wrap(IGameEvent payload, long revision = 1) => new(new EventId(++sequence), null, sequence, revision, "effects-check", payload);
        var events = new[]
        {
            Wrap(new SkillUsageConsumedEvent(owner.Seat, skillId, "test", SkillUsageScope.Turn, 1)),
            Wrap(new ProgramSkillResolvedEvent(1, owner.Seat, skillId, "test", true)),
            Wrap(new ProgramSkillResolvedEvent(2, owner.Seat, skillId, "test", false)),
            Wrap(new ProgramCardPolicyResolvedEvent(owner.Seat, skillId, "test", false))
        };
        var cues = BattleCueProjector.Project(events, view);
        Assert(cues is [ { Kind: BattleCueKind.Skill } ] && cues[0].Label == skill.Name && cues[0].SkillId == skill.ContentId,
            "A committed skill was missing, duplicated by completion, or inferred from an unapplied result.");
        Assert(GameSoundRules.FromPublicCues(cues).SequenceEqual([GameSound.Response]),
            "The new skill effect lost the existing committed skill sound.");
        var hidden = view with { Players = view.Players.Select(player => player.Seat == owner.Seat ?
            player with { IsGeneralPublic = false, IsSecondaryGeneralPublic = false } : player).ToArray() };
        Assert(BattleCueProjector.Project(events, hidden).Count == 0,
            "A hidden general's skill name or content ID reached the effect layer.");
        var next = events.Append(Wrap(new ProgramSkillResolvedEvent(3, owner.Seat, skillId, "test", true), 2));
        Assert(BattleCueProjector.Project(next, view).Count(cue => cue.Kind == BattleCueKind.Skill) == 2,
            "Deduplication swallowed a later committed skill activation.");

        using var active = ActiveSkillChecks.CreateShowcase(721019);
        active.IsMotionEnabled = true;
        active.SelectGeneralChoiceCommand.Execute(active.GeneralChoices.Single(choice => choice.SkillName == "苦肉"));
        AdvanceToDecision(active);
        var window = new MainWindow(active);
        var clock = new FrameClock();
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        layer.Clock = clock;
        try
        {
            Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "battle-effects-in-match-before.png"));
            var revision = Engine(active).Revision;
            var hp = Engine(active).CreateSnapshot(0).Players[0].Hp;
            active.UseActiveSkillCommand.Execute(null);
            Assert(Engine(active).Revision == revision + 1 && Engine(active).CreateSnapshot(0).Players[0].Hp == hp - 1,
                "The fixed real active skill did not commit its Core result.");
            Assert(active.BattleCues.Any(cue => cue.Revision == revision + 1 && cue.Kind == BattleCueKind.Skill && cue.Label == "苦肉"),
                "A real committed active skill reached the game but did not trigger its visual effect.");
            clock.Advance(.4);
            Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "battle-effects-in-match-skill.png"));
            Assert(layer.ActiveEffectCount > 0, "The real skill did not reach the table's bound effect layer.");
        }
        finally { window.Content = null; window.Close(); }
    }

    public static void RenderEffects(string output)
    {
        using var preview = new BattleEffectsPreview();
        var clock = new FrameClock();
        preview.FeedbackLayer.Clock = clock;
        preview.FeedbackLayer.MotionEnabled = false;
        var baseline = Capture(preview.Scene);
        var fingerprints = new HashSet<int>();
        foreach (var kind in Enum.GetValues<BattleEffectPreviewKind>())
        {
            preview.FeedbackLayer.MotionEnabled = true;
            preview.Play(kind);
            clock.Advance(kind == BattleEffectPreviewKind.Skill ? .48 : .38);
            preview.FeedbackLayer.InvalidateVisual();
            var bitmap = Capture(preview.Scene);
            var pixels = Pixels(bitmap);
            var original = Pixels(baseline);
            var changed = 0;
            var hash = new HashCode();
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (Math.Abs(pixels[i] - original[i]) + Math.Abs(pixels[i + 1] - original[i + 1]) +
                    Math.Abs(pixels[i + 2] - original[i + 2]) > 35) changed++;
                if (i % 64 == 0) hash.Add(pixels[i]);
            }
            Assert(changed > 2500, $"{kind} did not visibly render over its actual WPF battlefield ({changed} changed pixels).");
            fingerprints.Add(hash.ToHashCode());
            Save(bitmap, Path.Combine(output, $"battle-effects-{kind.ToString().ToLowerInvariant()}.png"));
            clock.Advance(2);
            preview.FeedbackLayer.InvalidateVisual();
            Capture(preview.Scene);
            Assert(preview.FeedbackLayer.ActiveEffectCount == 0, $"{kind} kept effects alive after its lifetime.");
        }
        Assert(fingerprints.Count == Enum.GetValues<BattleEffectPreviewKind>().Length,
            "Different effect examples produced identical frames.");
        var protectedSeat = preview.Scene.Children.OfType<System.Windows.Controls.Border>()
            .Single(border => BattleFeedbackLayer.GetSeatAnchor(border) == 1);
        preview.FeedbackLayer.ActionBar = protectedSeat;
        preview.Play(BattleEffectPreviewKind.Slash);
        clock.Advance(.38);
        preview.FeedbackLayer.InvalidateVisual();
        var clipped = Pixels(Capture(preview.Scene));
        var clear = Pixels(baseline);
        for (var y = 65; y < 195; y++)
            for (var x = 675; x < 781; x++)
            {
                var offset = (y * 880 + x) * 4;
                Assert(clipped.AsSpan(offset, 4).SequenceEqual(clear.AsSpan(offset, 4)),
                    "Combat effects painted inside a protected decision or action area.");
            }
        preview.FeedbackLayer.ActionBar = null;
        preview.FeedbackLayer.MotionEnabled = false;
        preview.Play(BattleEffectPreviewKind.Slash);
        Assert(preview.FeedbackLayer.ActiveEffectCount == 0 && !preview.FeedbackLayer.IsFrameTimerRunning && !preview.FeedbackLayer.IsHitTestVisible,
            "Disabled effects restarted animation or intercepted game input.");
        if (RecordMotion) RecordPreview(preview, clock, output);
    }

    public static void PreviewLifecycle()
    {
        using var preview = new BattleEffectsPreview();
        preview.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        preview.FeedbackLayer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        preview.Play(BattleEffectPreviewKind.Skill);
        Assert(preview.FeedbackLayer.IsFrameTimerRunning, "The loaded preview did not animate.");
        preview.Dispose();
        Assert(preview.FeedbackLayer.ActiveEffectCount == 0 && !preview.FeedbackLayer.IsFrameTimerRunning,
            "Closing the preview retained effects or its frame timer.");
        preview.FeedbackLayer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        using var vm = new MainViewModel(false, 17, false, new MemorySaveStore());
        var window = new MainWindow(vm);
        try
        {
            Assert(window.FindName("EffectsPreviewButton") is System.Windows.Controls.Button { Content: "预览战场特效" },
                "The preview cannot be reached through game settings.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void RecordPreview(BattleEffectsPreview preview, FrameClock clock, string output)
    {
        var encoder = new GifBitmapEncoder();
        var examples = new[] { BattleEffectPreviewKind.Slash, BattleEffectPreviewKind.Dodge, BattleEffectPreviewKind.FireSlash,
            BattleEffectPreviewKind.ThunderDamage, BattleEffectPreviewKind.Recovery, BattleEffectPreviewKind.Skill };
        preview.FeedbackLayer.MotionEnabled = true;
        foreach (var kind in examples)
        {
            preview.Play(kind);
            for (var frame = 0; frame < 21; frame++)
            {
                clock.Advance(.08);
                preview.FeedbackLayer.InvalidateVisual();
                var bitmap = Capture(preview.Scene);
                var compact = new TransformedBitmap(bitmap, new ScaleTransform(.7, .7));
                var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", (ushort)8);
                metadata.SetQuery("/grctlext/Disposal", (byte)2);
                if (encoder.Frames.Count == 0)
                {
                    metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                }
                encoder.Frames.Add(BitmapFrame.Create(compact, null, metadata, null));
            }
            clock.Advance(2);
        }
        var path = Path.Combine(output, "battle-effects-preview.gif");
        using (var file = File.Create(path)) encoder.Save(file);
        // WIC's GIF encoder discards frame delays. Write control metadata without
        // touching palettes or compressed pixels, then verify the resulting decoder.
        SetGifTiming(path);
        using var recorded = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(recorded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Console.WriteLine($"Recorded preview: {decoder.Frames.Count} frames, {decoder.Frames[0].PixelWidth}×{decoder.Frames[0].PixelHeight}, delay={(decoder.Frames[0].Metadata as BitmapMetadata)?.GetQuery("/grctlext/Delay") ?? "missing"}.");
        Assert(decoder.Frames.Count == examples.Length * 21 && decoder.Frames[0].PixelWidth == 616 &&
            decoder.Frames[0].Metadata is BitmapMetadata decodedMetadata && (ushort)decodedMetadata.GetQuery("/grctlext/Delay") == 8,
            "The recorded preview lost animation frames, timing, or its compact resolution.");
    }

    private static void SetGifTiming(string path)
    {
        var data = File.ReadAllBytes(path);
        var headerEnd = 13 + ((data[10] & 128) == 0 ? 0 : 3 * (1 << ((data[10] & 7) + 1)));
        var cursor = headerEnd;
        while (cursor < data.Length && data[cursor] != 0x3b)
        {
            var kind = data[cursor++];
            if (kind == 0x21)
            {
                var label = data[cursor++];
                if (label == 0xf9)
                {
                    Assert(data[cursor] == 4, "Invalid GIF graphic control block.");
                    data[cursor + 1] = (byte)((data[cursor + 1] & ~28) | 8); // Restore background between full frames.
                    data[cursor + 2] = 8; data[cursor + 3] = 0; // 80 ms.
                }
            }
            else if (kind == 0x2c)
            {
                var packed = data[cursor + 8];
                cursor += 9;
                if ((packed & 128) != 0) cursor += 3 * (1 << ((packed & 7) + 1));
                cursor++; // LZW minimum code size.
            }
            else throw new InvalidOperationException($"Unexpected GIF block {kind:X2}.");
            while (data[cursor] != 0) cursor += 1 + data[cursor];
            cursor++;
        }
        using var file = File.Create(path);
        file.Write(data, 0, headerEnd);
        // Infinite loop application extension at the first block boundary.
        file.Write(new byte[] { 0x21, 0xff, 11 });
        file.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        file.Write(new byte[] { 3, 1, 0, 0, 0 });
        file.Write(data, headerEnd, data.Length - headerEnd);
    }

    private static RenderTargetBitmap Capture(FrameworkElement root)
    {
        root.Measure(new Size(880, 470));
        root.Arrange(new Rect(0, 0, 880, 470));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(880, 470, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
