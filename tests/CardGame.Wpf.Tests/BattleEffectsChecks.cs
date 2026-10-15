using System.IO;
using System.Reflection;
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
    private static readonly MethodInfo PaintResponse = typeof(BattleFeedbackLayer).Assembly
        .GetType("CardGame.Wpf.Controls.BattleEffectPainter", throwOnError: true)!
        .GetMethod("Draw", BindingFlags.Static | BindingFlags.Public)!;

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

    private static IEnumerable<GeometryDrawing> ResponseStrokes(Drawing? drawing)
    {
        if (drawing is GeometryDrawing { Pen: not null } stroke) yield return stroke;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var childStroke in ResponseStrokes(child)) yield return childStroke;
    }

    internal static void SetGifTiming(string path, ushort delay = 8)
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
                    data[cursor + 2] = (byte)(delay & 255); data[cursor + 3] = (byte)(delay >> 8); // Centiseconds.
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
