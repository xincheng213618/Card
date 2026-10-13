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

    public static void RenderEffects(string output)
    {
        ResponsePaymentEffects(output);
        WeaponActivationEffects(output);
        LifeTransitionEffects(output);
        JudgmentResultEffects(output);
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
            clock.Advance(kind == BattleEffectPreviewKind.QinglongPursuit ? .18 :
                kind == BattleEffectPreviewKind.StoneAxeContinuation ? .34 :
                kind is BattleEffectPreviewKind.FireChain or BattleEffectPreviewKind.ThunderChain ? .7 :
                kind is BattleEffectPreviewKind.HitSequence or BattleEffectPreviewKind.DodgeSequence ? .65 :
                kind is BattleEffectPreviewKind.Damage or BattleEffectPreviewKind.FireDamage or BattleEffectPreviewKind.ThunderDamage ? .1 :
                kind == BattleEffectPreviewKind.Skill ? .48 : .38);
            preview.FeedbackLayer.InvalidateVisual();
            var bitmap = Capture(preview.Scene);
            if (kind is BattleEffectPreviewKind.RequiredDodgeFirst or BattleEffectPreviewKind.RequiredDodgeSecond)
            {
                var count = kind == BattleEffectPreviewKind.RequiredDodgeFirst ? 1 : 2;
                var cues = preview.FeedbackLayer.Cues!.Cast<BattleCue>().ToArray();
                Assert(cues.Count(cue => cue is { Kind: BattleCueKind.Response, CardKind: CardKind.Dodge, SourceSeat: 1 }) == 1 &&
                    cues.Single(cue => cue.HasResponseProgress) is { SourceSeat: 1, RequiredResponseCount: 2,
                        RequiredResponseCardKind: CardKind.Dodge } progress && progress.ResponseCount == count &&
                    cues.All(cue => cue.Kind is not (BattleCueKind.Damage or BattleCueKind.Recovery)) &&
                    Find<HealthMeter>(preview.Scene).All(meter => meter.Value == meter.Maximum),
                    "The response-stage preview invented a result, altered public HP or failed to pair its paid card with the exact 1/2 or 2/2 progress.");
            }
            if (kind is BattleEffectPreviewKind.StoneAxeContinuation or BattleEffectPreviewKind.QinglongPursuit)
            {
                var axe = kind == BattleEffectPreviewKind.StoneAxeContinuation;
                var cues = preview.FeedbackLayer.Cues!.Cast<BattleCue>().ToArray();
                var weapon = cues.Single(cue => cue.Kind == BattleCueKind.Weapon);
                Assert(weapon.IsWeaponActivated && weapon.SourceSeat == 0 && weapon.TargetSeats.SequenceEqual([1]) &&
                    weapon.WeaponKind == (axe ? CardKind.StoneAxe : CardKind.QinglongCrescentBlade) &&
                    weapon.CardKind is null && weapon.PublicCardId is null && weapon.PublicCard is null,
                    "The weapon preview lost its actual actor/target or fabricated a played physical weapon card.");
                Assert(axe ? cues.Any(cue => cue is { Kind: BattleCueKind.Damage, SourceSeat: 0, HpDelta: -1, RemainingHp: 3 }) :
                    cues.Count(cue => cue is { Kind: BattleCueKind.Card, CardKind: CardKind.Slash, SourceSeat: 0 }) == 1 &&
                    cues.All(cue => cue.Kind != BattleCueKind.Damage) &&
                    Find<HealthMeter>(preview.Scene).All(meter => meter.Value == meter.Maximum),
                    "The weapon preview must use a separate committed damage or follow-up Slash cue, rather than treating activation as a hit.");
            }
            if (kind == BattleEffectPreviewKind.JudgmentReveal)
            {
                var cues = preview.FeedbackLayer.Cues!.Cast<BattleCue>().ToArray();
                Assert(cues is [{ Kind: BattleCueKind.Judgment, SourceSeat: 1, JudgmentStage: JudgmentCueStage.Resolved,
                    JudgmentResolutionId: 1, JudgmentTitle: "乐不思蜀", Detail: "非红桃 · 跳过出牌阶段",
                    JudgmentCard: { Id: 1, Kind: CardKind.Dodge, Suit: Suit.Club, Rank: 8 } }] &&
                    cues[0].HasJudgmentCard && cues[0].TargetSeats.SequenceEqual([1]) &&
                    cues[0].PublicCard is null && cues[0].PublicCardId is null && cues[0].CardKind is null &&
                    cues[0].HpDelta == 0 && cues[0].RemainingHp is null && cues[0].MaximumHp is null &&
                    Find<HealthMeter>(preview.Scene).All(meter => meter.Value == 4 && meter.Maximum == 4) &&
                    Find<System.Windows.Controls.Button>(preview).Count(button => Equals(button.Content, "判定翻牌")) == 1,
                    "The judgment replay must expose one effective club-eight result through its own button without inventing a played card or altering public HP.");
            }
            if (kind == BattleEffectPreviewKind.Draw)
            {
                var cues = preview.FeedbackLayer.Cues!.Cast<BattleCue>().ToArray();
                var drawPile = Find<FrameworkElement>(preview.Scene).Single(element => element.Name == "PreviewDrawPileAnchor");
                var pileBounds = drawPile.TransformToVisual(preview.Scene).TransformBounds(new Rect(drawPile.RenderSize));
                Assert(cues is [{ Kind: BattleCueKind.Draw, SourceSeat: 1, DrawCount: 2, Label: "摸牌 +2", IsInquiry: false }] &&
                    cues[0].TargetSeats.SequenceEqual([1]) && cues[0].CardKind is null && cues[0].PublicCard is null &&
                    cues[0].PublicCardId is null && cues[0].JudgmentCard is null && cues[0].HpDelta == 0 &&
                    cues[0].RemainingHp is null && cues[0].MaximumHp is null &&
                    Find<HealthMeter>(preview.Scene).All(meter => meter.Value == 4 && meter.Maximum == 4) &&
                    Find<System.Windows.Controls.Button>(preview).Count(button => Equals(button.Content, "摸牌")) == 1 &&
                    ReferenceEquals(preview.FeedbackLayer.DrawPileAnchor, drawPile) && drawPile.Visibility == Visibility.Visible &&
                    pileBounds.Width > 0 && pileBounds.Height > 0 && new Rect(preview.Scene.RenderSize).Contains(pileBounds),
                    "Draw replay must use a visible pile anchor and only public actor/count, without exposing card identities or altering health.");
            }
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
            if (kind is not (BattleEffectPreviewKind.StoneAxeContinuation or BattleEffectPreviewKind.QinglongPursuit))
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

    private static void ResponsePaymentEffects(string output)
    {
        // Component-only frames isolate response ink from the public card's
        // flight and text. Real command timing is covered by the native fixture.
        foreach (var (name, size) in new[] { ("compact", new Size(92, 124)), ("standard", new Size(154, 204)) })
        {
            var responder = new Rect(55, 45, size.Width, size.Height);
            var attacker = new Rect(responder.Right + 35, 45, size.Width, size.Height);
            var paid = new BattleCue(1, BattleCueKind.Response, 1, [], "打出闪", "应答者") { CardKind = CardKind.Dodge };
            var clear = PaymentFrame(responder, attacker, null, 0);
            var dodge = PaymentFrame(responder, attacker, paid, .18 / 1.15);
            var lateDodge = PaymentFrame(responder, attacker, paid, .6 / 1.15);
            Assert(PixelChanges(clear.Pixels, dodge.Pixels, clear.Width, responder.Contains) > 40 &&
                PixelChanges(clear.Pixels, dodge.Pixels, clear.Width, point => !responder.Contains(point)) == 0,
                $"A paid Dodge must visibly sidestep at its actual {name} responder without painting its attacker or neighboring controls.");
            Assert(OpenResponseInk(dodge.Drawing) && lateDodge.Pixels.AsSpan().SequenceEqual(clear.Pixels),
                "Paying a Dodge retained a closed success shield or a long-lived portrait body effect.");
            Assert(PaymentFrame(responder, attacker, paid with { IsInquiry = true }, .15).Pixels.AsSpan().SequenceEqual(clear.Pixels) &&
                PaymentFrame(responder, attacker, paid with { Kind = BattleCueKind.Card }, .15).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                "A response inquiry or a card-kind-only declaration invented a paid Dodge result.");
            Save(dodge.Bitmap, Path.Combine(output, $"battle-effects-paid-dodge-{name}.png"));

            var first = new BattleCue(2, BattleCueKind.ResponseWindow, 1, [0], "连续响应 1 / 2", "应答者")
            {
                ResponseCount = 1, RequiredResponseCount = 2, RequiredResponseCardKind = CardKind.Dodge
            };
            var firstFrame = PaymentFrame(responder, attacker, first, .18 / 1.25);
            var secondFrame = PaymentFrame(responder, attacker, first with { ResponseCount = 2 }, .18 / 1.25);
            Assert(CyanEnergy(secondFrame.Pixels, clear.Pixels) > CyanEnergy(firstFrame.Pixels, clear.Pixels) &&
                !firstFrame.Pixels.AsSpan().SequenceEqual(secondFrame.Pixels) &&
                OpenResponseInk(firstFrame.Drawing) && OpenResponseInk(secondFrame.Drawing),
                "Public 1/2 and 2/2 must visibly advance an open response meter without closing a success shield.");
            Assert(PixelChanges(clear.Pixels, firstFrame.Pixels, clear.Width, point => !responder.Contains(point)) == 0 &&
                PaymentFrame(responder, attacker, first, 1.05 / 1.25).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                "Response progress escaped its responder or retained expired portrait ink.");
            var nearComplete = PaymentFrame(responder, attacker, first with { ResponseCount = 999, RequiredResponseCount = 1000 }, .18 / 1.25);
            var complete = PaymentFrame(responder, attacker, first with { ResponseCount = 1000, RequiredResponseCount = 1000 }, .18 / 1.25);
            Assert(CyanEnergy(complete.Pixels, clear.Pixels) > CyanEnergy(nearComplete.Pixels, clear.Pixels) &&
                !nearComplete.Pixels.AsSpan().SequenceEqual(complete.Pixels),
                "A dense 999/1000 response request displayed the same completed meter as 1000/1000.");
            foreach (var frame in new[] { nearComplete, complete,
                PaymentFrame(responder, attacker, first with { RequiredResponseCount = int.MaxValue }, .18 / 1.25) })
                Assert(ResponseStrokes(frame.Drawing).Count() is > 0 and <= 32 && OpenResponseInk(frame.Drawing),
                    "Dense response counts produced an unbounded number of strokes or a closed outcome shape.");
            foreach (var invalid in new[]
            {
                first with { ResponseCount = 0 }, first with { ResponseCount = -1 },
                first with { ResponseCount = 3 }, first with { RequiredResponseCount = 1 },
                first with { RequiredResponseCardKind = null }, first with { IsInquiry = true },
                first with { Kind = BattleCueKind.Response }
            })
                Assert(!invalid.HasResponseProgress &&
                    PaymentFrame(responder, attacker, invalid, .18 / 1.25).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                    "An invalid or unanswered response count invented portrait progress ink.");
            foreach (var frame in new[] { dodge, firstFrame, secondFrame })
            {
                var identity = new Rect(responder.Left, responder.Top, responder.Width, responder.Height * .16);
                var nameArea = new Rect(responder.Left, responder.Top + responder.Height * .2, responder.Width * .2, responder.Height * .45);
                var hp = new Rect(responder.Left, responder.Top + responder.Height * .72, responder.Width, responder.Height * .28);
                var pips = new Rect(responder.Left + responder.Width * .87, responder.Top + responder.Height * .2,
                    responder.Width * .13, responder.Height * .44);
                Assert(new[] { identity, nameArea, hp, pips }.All(region =>
                    PixelChanges(clear.Pixels, frame.Pixels, clear.Width, region.Contains) == 0),
                    "Paid response or progress ink painted over public identity, name, HP or health pips.");
            }
            Save(firstFrame.Bitmap, Path.Combine(output, $"battle-effects-response-first-{name}.png"));
            Save(secondFrame.Bitmap, Path.Combine(output, $"battle-effects-response-second-{name}.png"));
            Save(nearComplete.Bitmap, Path.Combine(output, $"battle-effects-response-near-complete-{name}.png"));
            Save(complete.Bitmap, Path.Combine(output, $"battle-effects-response-complete-{name}.png"));
        }
        Console.WriteLine("Response ink components: short open paid Dodge, responder-only 1/2→2/2, visible dense incomplete gap, bounded huge counts, invalid/inquiry guard, metadata preservation and decay.");
    }

    private static void WeaponActivationEffects(string output)
    {
        var actor = new Rect(45, 45, 154, 204);
        var target = new Rect(414, 45, 154, 204);
        var clear = PaymentFrame(actor, target, null, 0);
        var axe = new BattleCue(1, BattleCueKind.Weapon, 1, [0], "贯石斧 · 破闪", "发动者")
        {
            WeaponKind = CardKind.StoneAxe, IsWeaponActivated = true, WeaponResolutionId = 1
        };
        var early = PaymentFrame(actor, target, axe, .08 / .75);
        var arrival = PaymentFrame(actor, target, axe, .26 / .75);
        var earlyRoute = ChangedBounds(clear.Pixels, early.Pixels, clear.Width, point => !actor.Contains(point));
        var arrivedRoute = ChangedBounds(clear.Pixels, arrival.Pixels, clear.Width, point => !actor.Contains(point));
        Assert(PixelChanges(clear.Pixels, early.Pixels, clear.Width, actor.Contains) > 0 &&
            !earlyRoute.IsEmpty && !arrivedRoute.IsEmpty && arrivedRoute.Right > earlyRoute.Right &&
            arrivedRoute.Right >= target.Left - 30 && arrivedRoute.Right <= target.Left &&
            PixelChanges(clear.Pixels, arrival.Pixels, clear.Width, target.Contains) == 0,
            "Stone Axe activation must travel from its actor toward the original target edge without inventing a portrait hit.");
        var qinglong = axe with { WeaponKind = CardKind.QinglongCrescentBlade, Label = "青龙偃月刀 · 追击" };
        var pursuit = PaymentFrame(actor, target, qinglong, .18 / .75);
        Assert(PixelChanges(clear.Pixels, pursuit.Pixels, clear.Width, actor.Contains) > 0 &&
            PixelChanges(clear.Pixels, pursuit.Pixels, clear.Width, point => !actor.Contains(point)) == 0,
            "Qinglong activation must remain at its owner; the actual follow-up Slash owns the next attack flight.");
        var paidDodge = PaymentFrame(actor, target,
            new(2, BattleCueKind.Response, 1, [], "打出闪", "应答者") { CardKind = CardKind.Dodge }, .18 / 1.15);
        Assert(!pursuit.Pixels.AsSpan().SequenceEqual(paidDodge.Pixels),
            "Weapon activation was rendered as an unrelated paid-card response.");
        foreach (var weapon in new[] { axe, qinglong })
        {
            Assert(weapon.CardKind is null && weapon.PublicCardId is null && weapon.PublicCard is null &&
                PaymentFrame(actor, target, weapon, 1).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                "Weapon activation fabricated a physical card face or kept portrait ink past its .75-second lifetime.");
            foreach (var declined in new[]
            {
                weapon with { IsWeaponActivated = false }, weapon with { IsInquiry = true },
                weapon with { WeaponKind = null }, weapon with { WeaponKind = CardKind.Slash },
                weapon with { Kind = BattleCueKind.Response }
            })
                Assert(PaymentFrame(actor, target, declined, .18 / .75).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                    "A declined, unanswered or unrecognized weapon action invented an activation result.");
            var frame = PaymentFrame(actor, target, weapon, .18 / .75);
            var identity = new Rect(actor.Left, actor.Top, actor.Width, actor.Height * .16);
            var name = new Rect(actor.Left, actor.Top + actor.Height * .2, actor.Width * .2, actor.Height * .45);
            var hp = new Rect(actor.Left, actor.Top + actor.Height * .72, actor.Width, actor.Height * .28);
            var pips = new Rect(actor.Left + actor.Width * .87, actor.Top + actor.Height * .2, actor.Width * .13, actor.Height * .44);
            Assert(new[] { identity, name, hp, pips, target }.All(region =>
                PixelChanges(clear.Pixels, frame.Pixels, clear.Width, region.Contains) == 0),
                "A weapon body hid public portrait metadata or prepainted its target's health result.");
        }
        Save(early.Bitmap, Path.Combine(output, "battle-effects-weapon-stone-axe-early.png"));
        Save(arrival.Bitmap, Path.Combine(output, "battle-effects-weapon-stone-axe-arrival.png"));
        Save(pursuit.Bitmap, Path.Combine(output, "battle-effects-weapon-qinglong-owner.png"));
        Console.WriteLine("Weapon components: Stone Axe reaches the original target edge without a hit; Qinglong stays at its owner; false/inquiry/unknown guards, metadata preservation, separate paid response and .75s decay.");
    }

    private static void LifeTransitionEffects(string output)
    {
        // These component-only samples verify the bounded portrait ink. Native
        // command checks own the actual hit, rescue choice and result ordering.
        foreach (var (name, size) in new[] { ("compact", new Size(92, 124)), ("standard", new Size(154, 204)) })
        {
            var victim = new Rect(55, 45, size.Width, size.Height);
            var bystander = new Rect(victim.Right + 35, 45, size.Width, size.Height);
            var clear = PaymentFrame(victim, bystander, null, 0);
            var artwork = new Rect(victim.Left + victim.Width * .24, victim.Top + victim.Height * .2,
                victim.Width * .6, victim.Height * .46);
            var rasterArtwork = new Rect(Math.Floor(artwork.Left), Math.Floor(artwork.Top),
                Math.Ceiling(artwork.Right) - Math.Floor(artwork.Left), Math.Ceiling(artwork.Bottom) - Math.Floor(artwork.Top));
            var identity = new Rect(victim.Left, victim.Top, victim.Width, victim.Height * .16);
            var verticalName = new Rect(victim.Left, victim.Top + victim.Height * .2, victim.Width * .2, victim.Height * .45);
            var hp = new Rect(victim.Left, victim.Top + victim.Height * .72, victim.Width, victim.Height * .28);
            var pips = new Rect(victim.Left + victim.Width * .87, victim.Top + victim.Height * .2,
                victim.Width * .13, victim.Height * .44);
            var frames = new List<byte[]>();
            foreach (var (kind, durationField, caption) in new[]
            {
                (BattleCueKind.Dying, "DyingDuration", "濒死"),
                (BattleCueKind.Death, "DeathDuration", "阵亡"),
                (BattleCueKind.Rescued, "RescueDuration", "获救")
            })
            {
                var duration = (double)PaintResponse.DeclaringType!.GetField(durationField,
                    BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
                // An unrelated actor cannot move the effect away from the
                // victim in this already committed public cue's target list.
                var cue = new BattleCue(1, kind, 0, [1], caption, "公共角色");
                var early = PaymentFrame(victim, bystander, cue, .12);
                var middle = PaymentFrame(victim, bystander, cue, .45);
                var late = PaymentFrame(victim, bystander, cue, .85);
                foreach (var frame in new[] { early, middle, late })
                    Assert(PixelChanges(clear.Pixels, frame.Pixels, clear.Width, artwork.Contains) > 40 &&
                        PixelChanges(clear.Pixels, frame.Pixels, clear.Width, point => !rasterArtwork.Contains(point)) == 0 &&
                        new[] { identity, verticalName, hp, pips, bystander }.All(region =>
                            PixelChanges(clear.Pixels, frame.Pixels, clear.Width, region.Contains) == 0),
                        $"{kind} must remain visible within its {name} victim's artwork without covering public metadata or another portrait.");
                Assert(!early.Pixels.AsSpan().SequenceEqual(middle.Pixels) &&
                    !middle.Pixels.AsSpan().SequenceEqual(late.Pixels),
                    $"{kind} did not visibly progress and settle during its bounded lifetime.");
                foreach (var progress in new[] { 0.0, 1.0, (duration + .1) / duration })
                    Assert(PaymentFrame(victim, bystander, cue, progress).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                        $"{kind} left portrait ink before its start or after its lifetime.");
                foreach (var invalid in new[] { cue with { IsInquiry = true }, cue with { TargetSeats = [] }, cue with { TargetSeats = [99] } })
                    Assert(PaymentFrame(victim, bystander, invalid, .45).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                        "A rescue inquiry or missing public victim invented a dying, death or rescued portrait result.");

                if (kind == BattleCueKind.Dying)
                {
                    var firstBeat = PaymentFrame(victim, bystander, cue, .25);
                    var between = PaymentFrame(victim, bystander, cue, .5);
                    var secondBeat = PaymentFrame(victim, bystander, cue, .75);
                    Assert(RedEnergy(firstBeat.Pixels, clear.Pixels) > RedEnergy(between.Pixels, clear.Pixels) &&
                        RedEnergy(secondBeat.Pixels, clear.Pixels) > RedEnergy(between.Pixels, clear.Pixels),
                        "Dying must give two finite attention pulses rather than a static red portrait cover.");
                }
                else if (kind == BattleCueKind.Death)
                {
                    var earlyInk = ChangedBounds(clear.Pixels, early.Pixels, clear.Width, artwork.Contains);
                    var middleInk = ChangedBounds(clear.Pixels, middle.Pixels, clear.Width, artwork.Contains);
                    Assert(middleInk.Width < earlyInk.Width,
                        "Death's closing marks spread outward rather than gathering into the victim's artwork.");
                }
                else
                {
                    var earlyInk = ChangedBounds(clear.Pixels, early.Pixels, clear.Width, artwork.Contains);
                    var middleInk = ChangedBounds(clear.Pixels, middle.Pixels, clear.Width, artwork.Contains);
                    Assert(middleInk.Top < earlyInk.Top && !middle.Pixels.AsSpan().SequenceEqual(
                        PaymentFrame(victim, bystander, cue with { Kind = BattleCueKind.Recovery }, .45).Pixels),
                        "Rescued must visibly rise and differ from an ordinary committed health recovery.");
                }
                frames.Add(middle.Pixels);
                Save(middle.Bitmap, Path.Combine(output, $"battle-effects-{kind.ToString().ToLowerInvariant()}-{name}.png"));
            }
            Assert(!frames[0].AsSpan().SequenceEqual(frames[1]) && !frames[1].AsSpan().SequenceEqual(frames[2]) &&
                !frames[0].AsSpan().SequenceEqual(frames[2]),
                "Dying, death and a successful rescue collapsed into the same portrait result.");
        }
        Console.WriteLine("Life-transition ink components: compact/standard public victims; finite dying heartbeats, inward death closure and upward rescued arcs; distinct from recovery, metadata/bystander preservation, inquiry/missing-target guards and exact retirement.");
    }

    private static void JudgmentResultEffects(string output)
    {
        // Component-only ink; the native judgment checks verify the engine's
        // public effective judgment face and the layer's reveal placement.
        double Timing(string name) => (double)PaintResponse.DeclaringType!.GetField(name,
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var duration = Timing("JudgmentDuration");
        var reveal = Timing("JudgmentRevealTime");
        var confirmation = Timing("JudgmentConfirmationTime");
        foreach (var (name, size) in new[] { ("compact", new Size(92, 124)), ("standard", new Size(154, 204)) })
        {
            var target = new Rect(55, 45, size.Width, size.Height);
            var bystander = new Rect(target.Right + 35, 45, size.Width, size.Height);
            var clear = PaymentFrame(target, bystander, null, 0);
            var card = new CardSnapshot(91, CardKind.Dodge, Suit.Heart, 7, "闪", "7");
            var cue = new BattleCue(1, BattleCueKind.Judgment, 0, [1], "判定结果已揭示", "公开目标")
            {
                JudgmentStage = JudgmentCueStage.Resolved, JudgmentResolutionId = 23,
                JudgmentCard = card, Detail = "满足判定条件"
            };
            Assert(cue.CardKind is null && cue.PublicCardId is null && cue.PublicCard is null,
                "The effective judgment face must not masquerade as an ordinary printed physical-card projection.");
            var artwork = new Rect(target.Left + target.Width * .24, target.Top + target.Height * .2,
                target.Width * .6, target.Height * .46);
            var rasterArtwork = new Rect(Math.Floor(artwork.Left), Math.Floor(artwork.Top),
                Math.Ceiling(artwork.Right) - Math.Floor(artwork.Left), Math.Ceiling(artwork.Bottom) - Math.Floor(artwork.Top));
            var identity = new Rect(target.Left, target.Top, target.Width, target.Height * .16);
            var verticalName = new Rect(target.Left, target.Top + target.Height * .2, target.Width * .2, target.Height * .45);
            var hp = new Rect(target.Left, target.Top + target.Height * .72, target.Width, target.Height * .28);
            var pips = new Rect(target.Left + target.Width * .87, target.Top + target.Height * .2,
                target.Width * .13, target.Height * .44);
            var early = PaymentFrame(target, bystander, cue, (reveal + confirmation * .2) / duration);
            var middle = PaymentFrame(target, bystander, cue, (reveal + confirmation * .5) / duration);
            var late = PaymentFrame(target, bystander, cue, (reveal + confirmation * .85) / duration);
            foreach (var frame in new[] { early, middle, late })
                Assert(PixelChanges(clear.Pixels, frame.Pixels, clear.Width, artwork.Contains) > 20 &&
                    PixelChanges(clear.Pixels, frame.Pixels, clear.Width, point => !rasterArtwork.Contains(point)) == 0 &&
                    new[] { identity, verticalName, hp, pips, bystander }.All(region =>
                        PixelChanges(clear.Pixels, frame.Pixels, clear.Width, region.Contains) == 0) &&
                    ResponseStrokes(frame.Drawing).Count() is > 0 and <= 2,
                    $"A revealed judgment needs a bounded visible {name} target-only stroke without covering names, HP or another portrait.");
            Assert(!early.Pixels.AsSpan().SequenceEqual(middle.Pixels) && !middle.Pixels.AsSpan().SequenceEqual(late.Pixels) &&
                middle.Pixels.AsSpan().SequenceEqual(PaymentFrame(target, bystander,
                    cue with { Label = "判定结果已揭示", Detail = "不满足判定条件" }, (reveal + confirmation * .5) / duration).Pixels),
                "Judgment confirmation must grow and settle with the same neutral ink for matched and unmatched public outcomes.");
            foreach (var elapsed in new[] { 0.0, reveal, reveal + confirmation, duration, duration + .1 })
                Assert(PaymentFrame(target, bystander, cue, elapsed / duration).Pixels.AsSpan().SequenceEqual(clear.Pixels),
                    "Judgment portrait confirmation started before face reveal or survived its short lifetime.");
            foreach (var invalid in new[]
            {
                cue with { JudgmentStage = null }, cue with { JudgmentStage = JudgmentCueStage.Requested },
                cue with { JudgmentStage = JudgmentCueStage.Replaced }, cue with { IsInquiry = true },
                cue with { JudgmentCard = null }, cue with { JudgmentCard = card with { Id = 0 } },
                cue with { JudgmentCard = card with { Kind = (CardKind)int.MaxValue } },
                cue with { JudgmentCard = card with { Rank = 0 } }, cue with { JudgmentCard = card with { Rank = 14 } },
                cue with { JudgmentCard = card with { Suit = Suit.None } },
                cue with { JudgmentCard = card with { DisplayName = "" } }, cue with { JudgmentCard = card with { RankText = "" } },
                cue with { JudgmentCard = null, PublicCardId = card.Id, CardKind = card.Kind, PublicCard = card with { Suit = Suit.Spade } },
                cue with { TargetSeats = [] }, cue with { TargetSeats = [99] },
                cue with { Kind = BattleCueKind.ResponseWindow }
            })
                Assert(PaymentFrame(target, bystander, invalid, (reveal + confirmation * .5) / duration)
                    .Pixels.AsSpan().SequenceEqual(clear.Pixels),
                    "A requested, replaced, unknown or incomplete judgment face invented a resolved portrait confirmation.");
            Save(middle.Bitmap, Path.Combine(output, $"battle-effects-judgment-result-{name}.png"));
        }
        Console.WriteLine("Judgment ink components: compact/standard public effective judgment faces with empty ordinary card fields; neutral gold for both condition outcomes, short reveal-aligned target-only stroke, metadata/bystander preservation, request/replacement/incomplete guards and endpoint retirement.");
    }

    private static long RedEnergy(byte[] pixels, byte[] clear)
    {
        long energy = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            energy += Math.Max(0, pixels[offset + 2] - clear[offset + 2]);
        return energy;
    }

    private static Rect ChangedBounds(byte[] before, byte[] after, int width, Func<Point, bool> region)
    {
        var bounds = Rect.Empty;
        for (var offset = 0; offset < before.Length; offset += 4)
        {
            var point = new Point(offset / 4 % width + .5, offset / 4 / width + .5);
            if (region(point) && Math.Abs(before[offset] - after[offset]) + Math.Abs(before[offset + 1] - after[offset + 1]) +
                Math.Abs(before[offset + 2] - after[offset + 2]) > 20) bounds.Union(new Rect(point, new Size(1, 1)));
        }
        return bounds;
    }

    private static (RenderTargetBitmap Bitmap, byte[] Pixels, DrawingGroup Drawing, int Width) PaymentFrame(
        Rect responder, Rect attacker, BattleCue? cue, double progress)
    {
        var width = (int)Math.Ceiling(attacker.Right + 35);
        var height = (int)Math.Ceiling(responder.Bottom + 35);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(Brushes.DarkSlateGray, null, responder);
            dc.DrawRectangle(Brushes.DarkSlateGray, null, attacker);
            if (cue is not null)
                PaintResponse.Invoke(null, [dc, cue, progress, responder, new Dictionary<int, Rect> { [0] = attacker, [1] = responder }]);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return (bitmap, Pixels(bitmap), visual.Drawing, width);
    }

    private static IEnumerable<GeometryDrawing> ResponseStrokes(Drawing? drawing)
    {
        if (drawing is GeometryDrawing { Pen: not null } stroke) yield return stroke;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var childStroke in ResponseStrokes(child)) yield return childStroke;
    }

    private static bool OpenResponseInk(Drawing drawing) => ResponseStrokes(drawing).Any() &&
        ResponseStrokes(drawing).All(stroke => stroke.Brush is null && stroke.Geometry is not EllipseGeometry &&
            PathGeometry.CreateFromGeometry(stroke.Geometry).Figures.All(figure => !figure.IsClosed));

    private static int PixelChanges(byte[] before, byte[] after, int width, Func<Point, bool> region)
    {
        var changes = 0;
        for (var offset = 0; offset < before.Length; offset += 4)
            if (region(new Point(offset / 4 % width + .5, offset / 4 / width + .5)) &&
                Math.Abs(before[offset] - after[offset]) + Math.Abs(before[offset + 1] - after[offset + 1]) +
                Math.Abs(before[offset + 2] - after[offset + 2]) > 20) changes++;
        return changes;
    }

    private static long CyanEnergy(byte[] pixels, byte[] clear)
    {
        long energy = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            energy += Math.Max(0, pixels[offset] - clear[offset]) + Math.Max(0, pixels[offset + 1] - clear[offset + 1]);
        return energy;
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
        var examples = new[] { BattleEffectPreviewKind.HitSequence, BattleEffectPreviewKind.DodgeSequence,
            BattleEffectPreviewKind.RequiredDodgeFirst, BattleEffectPreviewKind.RequiredDodgeSecond,
            BattleEffectPreviewKind.StoneAxeContinuation, BattleEffectPreviewKind.QinglongPursuit, BattleEffectPreviewKind.FireSlash,
            BattleEffectPreviewKind.ThunderDamage, BattleEffectPreviewKind.DamageRecovery, BattleEffectPreviewKind.Skill };
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
