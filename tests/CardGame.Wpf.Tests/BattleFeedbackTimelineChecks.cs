using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class BattleFeedbackTimelineChecks
{
    public static void CombatBeats(string output)
    {
        using var attack = new BattleEffectsPreview();
        using var hit = new BattleEffectsPreview();
        using var dodge = new BattleEffectsPreview();
        var attackClock = new FrameClock();
        var hitClock = new FrameClock();
        var dodgeClock = new FrameClock();
        attack.FeedbackLayer.Clock = attackClock;
        hit.FeedbackLayer.Clock = hitClock;
        dodge.FeedbackLayer.Clock = dodgeClock;
        attack.FeedbackLayer.MotionEnabled = false;
        var clear = Pixels(Capture(attack));
        attack.FeedbackLayer.MotionEnabled = true;
        attack.Play(BattleEffectPreviewKind.Slash);
        hit.Play(BattleEffectPreviewKind.HitSequence);
        dodge.Play(BattleEffectPreviewKind.DodgeSequence);
        attackClock.Advance(.18); hitClock.Advance(.18); dodgeClock.Advance(.18);
        var launch = Pixels(Capture(attack));
        Assert(SameCombatFrame(launch, Pixels(Capture(hit))) && SameCombatFrame(launch, Pixels(Capture(dodge))),
            "Damage or Dodge was painted before the public attack reached its target.");
        Save(Capture(hit), Path.Combine(output, "battle-beat-launch.png"));
        attackClock.Advance(.2);
        var incoming = Pixels(Capture(attack));
        for (var y = 95; y < 163; y++)
            for (var x = 700; x < 758; x++)
            {
                var offset = (y * 880 + x) * 4;
                Assert(incoming.AsSpan(offset, 4).SequenceEqual(clear.AsSpan(offset, 4)),
                    "A declaration alone struck the target portrait before a committed damage result.");
            }
        attackClock.Advance(.27); hitClock.Advance(.47); dodgeClock.Advance(.47);
        var approach = Pixels(Capture(attack));
        var impact = Capture(hit);
        var evade = Capture(dodge);
        Assert(!approach.SequenceEqual(Pixels(impact)) && !approach.SequenceEqual(Pixels(evade)) &&
               !Pixels(impact).SequenceEqual(Pixels(evade)),
            "Confirmed damage and successful Dodge did not produce distinct result beats.");
        Save(impact, Path.Combine(output, "battle-beat-impact.png"));
        Save(evade, Path.Combine(output, "battle-beat-dodge.png"));
        foreach (var preview in new[] { attack, hit, dodge })
            Assert(!preview.FeedbackLayer.IsHitTestVisible && !preview.FeedbackLayer.Focusable,
                "Combat feedback intercepted an active table command.");
        hitClock.Advance(2);
        Capture(hit);
        Assert(hit.FeedbackLayer.ActiveEffectCount == 0, "A scheduled result outlived its bounded animation.");

        // A person can answer well after the attack animation has finished.
        // That committed response must appear immediately, and off/reset/unload
        // must also discard results which have not started drawing yet.
        var cues = new ObservableCollection<BattleCue>();
        var delayedClock = new FrameClock();
        dodge.FeedbackLayer.Cues = cues;
        dodge.FeedbackLayer.Clock = delayedClock;
        var card = new BattleCue(10, BattleCueKind.Card, 0, [1], "杀", "关羽") { CardKind = CardKind.Slash, Revision = 2 };
        var response = new BattleCue(11, BattleCueKind.Response, 1, [], "打出闪", "曹操") { CardKind = CardKind.Dodge, Revision = 3 };
        cues.Add(card);
        delayedClock.Advance(1);
        Capture(dodge);
        Assert(dodge.FeedbackLayer.ActiveEffectCount == 0, "The old declaration did not expire.");
        cues.Add(response);
        delayedClock.Advance(.08);
        Capture(dodge);
        Assert(Images(VisualTreeHelper.GetDrawing(dodge.FeedbackLayer)).Any(image =>
                ReferenceEquals(image.ImageSource, CardArt.Get(CardKind.Dodge))),
            "A late human Dodge waited for an already-finished attack.");
        foreach (var stop in new Action[]
        {
            () => { dodge.FeedbackLayer.MotionEnabled = false; dodge.FeedbackLayer.MotionEnabled = true; },
            () => cues.Clear(),
            () => dodge.FeedbackLayer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent))
        })
        {
            cues.Clear(); cues.Add(card); cues.Add(new(12, BattleCueKind.Damage, 0, [1], "−1", "曹操"));
            stop();
            delayedClock.Advance(.7);
            Capture(dodge);
            Assert(dodge.FeedbackLayer.ActiveEffectCount == 0 && !dodge.FeedbackLayer.IsFrameTimerRunning,
                "Stopping the layer left a queued damage beat behind.");
        }
        Console.WriteLine("Combat beats: launch precedes hit/Dodge; late human response is immediate; queued effects stop with motion/reset/unload.");
        ResponseProgressLifetime();
        LifeOutcomeLifetime();
        WeaponTiming(output);
        NativeDamage(output);
    }

    private static void LifeOutcomeLifetime()
    {
        var cues = new ObservableCollection<BattleCue>();
        var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Cues = cues, Clock = clock };
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            cues.Add(new(20, BattleCueKind.Dying, 0, [0], "濒死", "武将") { LifeResolutionId = 700 });
            clock.Advance(1.1);
            cues.Add(new(19, BattleCueKind.Rescued, 0, [0], "旧窗口获救", "武将") { LifeResolutionId = 699 });
            clock.Advance(.2);
            cues.Add(new(21, BattleCueKind.Dying, 1, [1], "另一角色濒死", "武将") { LifeResolutionId = 701 });
            Assert(layer.ActiveEffectCount == 1,
                "An older completion replaced the newer dying window or prolonged its expired feedback.");
            // A short projected batch may omit the new dying entry. Its later
            // public completion still supersedes earlier life feedback at that seat.
            cues.Add(new(22, BattleCueKind.Rescued, 1, [1], "新窗口获救", "武将") { LifeResolutionId = 702 });
            clock.Advance(1);
            cues.Add(new(23, BattleCueKind.Dying, 0, [0], "濒死", "武将") { LifeResolutionId = 703 });
            Assert(layer.ActiveEffectCount == 1,
                "A later completion was dropped because its new dying entry was absent from the cue batch.");
            cues.Add(new(24, BattleCueKind.Death, 0, [0], "阵亡", "武将"));
            Assert(layer.ActiveEffectCount == 1, "The committed death retained its superseded dying effect.");
            foreach (var stop in new Action[]
            {
                () => { layer.MotionEnabled = false; layer.MotionEnabled = true; },
                () => cues.Clear(),
                () => layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent))
            })
            {
                cues.Clear();
                cues.Add(new(30, BattleCueKind.Damage, 1, [0], "−1", "武将"));
                cues.Add(new(31, BattleCueKind.Dying, 0, [0], "濒死", "武将") { LifeResolutionId = 704 });
                cues.Add(new(32, BattleCueKind.Death, 0, [0], "阵亡", "武将"));
                stop();
                clock.Advance(2);
                Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
                    "Stopping feedback retained a scheduled life outcome or restarted its frame timer.");
            }
        }
        finally { layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        Console.WriteLine("Life outcome component lifetime: sequence guards, omitted entry, supersession and interruption passed.");
    }

    private static void ResponseProgressLifetime()
    {
        var cues = new ObservableCollection<BattleCue>();
        var layer = new BattleFeedbackLayer { Cues = cues, Clock = new FrameClock() };
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        BattleCue Progress(long sequence, long resolution, int responder, int count) =>
            new(sequence, BattleCueKind.ResponseWindow, responder, [], $"连续响应 {count} / 2", "武将")
            {
                ResponseResolutionId = resolution, ResponseCount = count,
                RequiredResponseCount = 2, RequiredResponseCardKind = CardKind.Dodge
            };
        try
        {
            cues.Add(new(1, BattleCueKind.Response, 0, [], "打出闪", "武将") { CardKind = CardKind.Dodge });
            cues.Add(Progress(2, 100, 0, 1));
            cues.Add(Progress(3, 101, 0, 1));
            cues.Add(Progress(4, 100, 1, 1));
            Assert(layer.ActiveEffectCount == 4, "Independent public response sets or other responders lost their feedback.");
            cues.Add(Progress(5, 100, 0, 2));
            Assert(layer.ActiveEffectCount == 4,
                "Advancing the same public response set must replace its old count without discarding a paid card or another set.");
            cues.Add(Progress(6, 100, 0, 0));
            Assert(layer.ActiveEffectCount == 5, "Invalid progress must not replace a valid paid response count.");
            cues.Add(new(7, BattleCueKind.Weapon, 1, [0], "贯石斧 · 破闪", "武将")
            { WeaponKind = CardKind.StoneAxe, WeaponResolutionId = 100, IsWeaponActivated = true });
            Assert(layer.ActiveEffectCount == 5,
                "The weapon continuation must replace only its target's completed public response count, keeping the paid card and other sets.");
            cues.Add(new(8, BattleCueKind.Weapon, 0, [0], "青龙偃月刀 · 追击", "武将")
            { WeaponKind = CardKind.QinglongCrescentBlade, WeaponResolutionId = 999, IsWeaponActivated = true });
            Assert(layer.ActiveEffectCount == 6, "An unrelated weapon result discarded another attack's response count.");
            cues.Add(new(9, BattleCueKind.Weapon, 0, [1], "贯石斧 · 未发动", "武将")
            { WeaponKind = CardKind.StoneAxe, WeaponResolutionId = 100 });
            Assert(layer.ActiveEffectCount == 6, "Declining a weapon must retire only that public attack's response set.");
            layer.MotionEnabled = false;
            layer.MotionEnabled = true;
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
                "Changing motion preferences must not replay a historical response count.");
            cues.Add(Progress(7, 100, 0, 2));
            cues.Clear();
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
                "Resetting the public cue collection must retire its response progress.");
        }
        finally { layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void WeaponTiming(string output)
    {
        using var announced = new BattleEffectsPreview();
        using var committed = new BattleEffectsPreview();
        var announcedClock = new FrameClock();
        var committedClock = new FrameClock();
        var announcedCues = new ObservableCollection<BattleCue>();
        var committedCues = new ObservableCollection<BattleCue>();
        announced.FeedbackLayer.Clock = announcedClock;
        committed.FeedbackLayer.Clock = committedClock;
        announced.FeedbackLayer.Cues = announcedCues;
        committed.FeedbackLayer.Cues = committedCues;
        foreach (var preview in new[] { announced, committed })
        {
            var bystander = preview.Scene.Children.OfType<FrameworkElement>()
                .Single(element => BattleFeedbackLayer.GetSeatAnchor(element) == 2);
            bystander.Visibility = Visibility.Visible;
            Canvas.SetLeft(bystander, 365); Canvas.SetTop(bystander, 145);
        }
        var clear = Pixels(Capture(announced));
        var axe = new BattleCue(20, BattleCueKind.Weapon, 0, [1], "贯石斧 · 破闪", "关羽",
            Detail: "弃置 2 张牌 · 此杀继续结算")
            { WeaponKind = CardKind.StoneAxe, WeaponResolutionId = 100, IsWeaponActivated = true };
        var damage = new BattleCue(21, BattleCueKind.Damage, 0, [1], "−1", "曹操");
        announcedCues.Add(axe);
        committedCues.Add(axe);
        committedCues.Add(damage);
        foreach (var delta in new[] { .04, .04, .10 })
        {
            announcedClock.Advance(delta); committedClock.Advance(delta);
            var frame = Pixels(Capture(announced));
            Assert(frame.SequenceEqual(Pixels(Capture(committed))),
                "An actual damage result was painted before the paid Stone Axe reached its target edge.");
            Assert(SameArea(clear, frame, 365, 145, 146, 170),
                "A Stone Axe stroke or its caption covered the bystander's public portrait.");
        }
        Save(Capture(committed), Path.Combine(output, "battle-weapon-beat-before-arrival.png"));
        announcedClock.Advance(.14); committedClock.Advance(.14);
        var onlyWeapon = Pixels(Capture(announced));
        var actualHit = Capture(committed);
        Assert(!SameTargetArtwork(onlyWeapon, Pixels(actualHit)),
            "Committed damage did not begin after the Stone Axe's arrival.");
        Save(actualHit, Path.Combine(output, "battle-weapon-beat-actual-damage.png"));
        Assert(!Images(VisualTreeHelper.GetDrawing(announced.FeedbackLayer)).Any(),
            "A weapon result manufactured a card face or a weapon-card play.");
        announcedClock.Advance(.44);
        Capture(announced);
        Assert(announced.FeedbackLayer.ActiveEffectCount == 0,
            "Weapon activation remained active beyond its 750ms bounded lifetime.");

        // A result can follow a slower native skill choice. It does not replay
        // an expired axe, nor wait for a different actor/recipient or a decline.
        foreach (var (description, weapon, damageSource, damageTarget, age) in new[]
        {
            ("declined", axe with { IsWeaponActivated = false }, 0, 1, 0d),
            ("inquiry", axe with { IsInquiry = true }, 0, 1, 0d),
            ("other weapon", axe with { WeaponKind = CardKind.QinglongCrescentBlade }, 0, 1, 0d),
            ("other source", axe with { SourceSeat = 1, TargetSeats = new[] { 0 } }, 0, 0, 0d),
            ("other target", axe with { TargetSeats = new[] { 0 } }, 0, 1, 0d),
            ("late skill choice", axe, 0, 1, .8)
        })
        {
            announcedCues.Clear(); committedCues.Clear();
            announcedCues.Add(weapon); committedCues.Add(weapon);
            announcedClock.Advance(age); committedClock.Advance(age);
            Capture(announced); Capture(committed);
            committedCues.Add(damage with { SourceSeat = damageSource, TargetSeats = new[] { damageTarget } });
            announcedClock.Advance(.08); committedClock.Advance(.08);
            Assert(!Pixels(Capture(announced)).SequenceEqual(Pixels(Capture(committed))),
                $"Actual damage incorrectly waited for a {description} weapon cue.");
        }
        Console.WriteLine("Weapon beats: actual damage waits 240ms for the paid axe; activation alone has no hit/card face; declines, unrelated cues and late continuations do not hold results.");
    }

    private static bool SameTargetArtwork(byte[] left, byte[] right)
        => SameArea(left, right, 676, 65, 111, 93);

    private static bool SameArea(byte[] left, byte[] right, int x0, int y0, int width, int height)
    {
        for (var y = y0; y < y0 + height; y++)
            for (var x = x0; x < x0 + width; x++)
            {
                var offset = (y * 880 + x) * 4;
                if (!left.AsSpan(offset, 4).SequenceEqual(right.AsSpan(offset, 4))) return false;
            }
        return true;
    }

    private static void NativeDamage(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true;
        vm.IsSoundEnabled = false;
        vm.IsMusicEnabled = false;
        var window = new MainWindow(vm);
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock();
        layer.Clock = clock;
        try
        {
            Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "battle-beat-live-before.png"));
            var game = Engine(vm);
            var revision = game.Revision;
            var hp = game.CreateSnapshot(0).Players[0].Hp;
            Assert(vm.CanDeclineResponse, "The native fixture has no decline command.");
            vm.DeclineResponseCommand.Execute(null);
            for (var step = 0; step < 12 && game.CreateSnapshot(0).Players[0].Hp == hp && vm.CanStepAi; step++)
                vm.StepAiCommand.Execute(null);
            Assert(game.Revision > revision && game.CreateSnapshot(0).Players[0].Hp < hp &&
                   vm.BattleCues.Any(cue => cue.Kind == BattleCueKind.Damage && cue.TargetSeats.Contains(0)),
                "A native declined Slash did not commit HP loss and publish its actual damage cue.");
            var accepted = game.AcceptedCommands.Count;
            var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            clock.Advance(.28);
            Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "battle-beat-live-damage.png"));
            Assert(layer.ActiveEffectCount > 0, "The native committed result did not reach the real table layer.");
            var portrait = Find<Button>((FrameworkElement)window.Content).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == 0);
            var icons = Find<ItemsControl>(portrait).Single(items => items.Name == "SeatHealthIcons");
            var seat = (SeatViewModel)portrait.DataContext;
            var damage = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Last(e => e.TargetSeat == 0);
            var cue = vm.BattleCues.Last(e => e.Kind == BattleCueKind.Damage && e.TargetSeats.Contains(0));
            Assert(seat.Hp == damage.RemainingHp && seat.MaxHp == game.CreateSnapshot(0).Players[0].MaxHp &&
                   icons.Items.Cast<string>().Count(path => path.EndsWith("gallery-hp-wu.png", StringComparison.Ordinal)) == Math.Clamp(seat.Hp, 0, 8) &&
                   cue.HpDelta == -damage.Amount && cue.RemainingHp == seat.Hp && cue.MaximumHp == seat.MaxHp,
                "The real portrait icons lost the actual public HP amount or remaining value.");
            clock.Advance(3);
            layer.InvalidateVisual();
            Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, "battle-beat-live-retired.png"));
            Assert(layer.ActiveEffectCount == 0, $"The native damage effect did not retire ({layer.ActiveEffectCount} active).");
            Assert(game.AcceptedCommands.Count == accepted, $"Rendering advanced commands: {accepted} -> {game.AcceptedCommands.Count}, auto={vm.IsAutoAdvance}.");
            Assert(Enumerable.Range(0, 4).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                "Drawing or retiring combat effects changed a player-safe view.");
            Console.WriteLine($"Native damage beat: revision {revision} -> {game.Revision}; HP {hp} -> {game.CreateSnapshot(0).Players[0].Hp}; 4 viewer snapshots unchanged by rendering.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static BitmapSource Capture(BattleEffectsPreview preview)
    {
        preview.FeedbackLayer.InvalidateVisual();
        preview.Scene.Measure(new Size(880, 470));
        preview.Scene.Arrange(new Rect(0, 0, 880, 470));
        preview.Scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(880, 470, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(preview.Scene);
        return bitmap;
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }
    private static bool SameCombatFrame(byte[] left, byte[] right)
    {
        // Current HP is authoritative immediately; only the combat animation waits.
        // Exclude the target's HP readout and trailing health strip from this comparison.
        for (var y = 0; y < 470; y++)
            for (var x = 0; x < 880; x++)
                if (!(x >= 655 && x < 801 && y >= 173 && y < 215))
                {
                    var offset = (y * 880 + x) * 4;
                    if (!left.AsSpan(offset, 4).SequenceEqual(right.AsSpan(offset, 4))) return false;
                }
        return true;
    }
    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Images(child)) yield return nested;
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
