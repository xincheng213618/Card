using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.Controls;

/// <summary>Bounded vector effects. All positions and identities come from public presentation cues.</summary>
internal static class BattleEffectPainter
{
    internal const double DamageDuration = 1.15;
    internal const double DamageImpactTime = .3;
    internal const double ChainDuration = .6;
    internal const double ChainArrivalTime = .24;
    internal const double WeaponDuration = .75;
    internal const double WeaponArrivalTime = .24;
    internal const double DyingDuration = 1.2;
    internal const double DeathDuration = .85;
    internal const double RescueDuration = .95;
    internal const double JudgmentDuration = 1.45;
    internal const double JudgmentRevealTime = .18;
    internal const double JudgmentConfirmationTime = .38;
    private static readonly Brush Gold = Solid(255, 203, 100);
    private static readonly Brush White = Solid(255, 249, 218);
    private static readonly Brush Cyan = Solid(116, 231, 255);
    private static readonly Brush Red = Solid(255, 91, 62);
    private static readonly Brush Violet = Solid(175, 151, 255);
    private static readonly Brush Green = Solid(134, 255, 179);
    private static readonly Brush AshGold = Solid(204, 187, 158);
    private static readonly Brush SkillInk = Solid(36, 42, 34);
    private static readonly Brush SkillSeal = Solid(174, 69, 48);
    private static readonly Brush GoldGlow = Glow(Color.FromArgb(190, 255, 178, 55));
    private static readonly Brush BlueGlow = Glow(Color.FromArgb(160, 53, 195, 255));
    private static readonly Brush FireGlow = Glow(Color.FromArgb(200, 255, 73, 25));
    private static readonly Brush ThunderGlow = Glow(Color.FromArgb(185, 153, 94, 255));
    private static readonly Brush GreenGlow = Glow(Color.FromArgb(150, 76, 237, 144));
    private static readonly Brush DyingGlow = Glow(Color.FromArgb(110, 255, 91, 62));

    public static void Draw(DrawingContext dc, BattleCue cue, double progress, Rect source,
        IReadOnlyDictionary<int, Rect> seats)
    {
        if (cue.IsInquiry || cue.Kind == BattleCueKind.HpLoss || progress <= 0 || progress >= 1) return;
        if (cue.Kind == BattleCueKind.Judgment)
        {
            if (!cue.HasJudgmentCard) return;
            var elapsed = progress * JudgmentDuration;
            if (elapsed <= JudgmentRevealTime || elapsed >= JudgmentRevealTime + JudgmentConfirmationTime) return;
            var confirm = (elapsed - JudgmentRevealTime) / JudgmentConfirmationTime;
            foreach (var seat in cue.TargetSeats)
                if (seats.TryGetValue(seat, out var target) && !target.IsEmpty && target.Width > 0 && target.Height > 0)
                    JudgmentConfirmation(dc, PortraitArtwork(target), confirm);
            return;
        }
        if (cue.Kind == BattleCueKind.Weapon)
        {
            if (!cue.IsWeaponActivated || source.IsEmpty ||
                cue.WeaponKind is not (CardKind.StoneAxe or CardKind.QinglongCrescentBlade)) return;
            WeaponActivation(dc, source, progress);
            if (cue.WeaponKind == CardKind.StoneAxe)
                foreach (var seat in cue.TargetSeats)
                    if (seats.TryGetValue(seat, out var target)) StoneAxe(dc, source, target, progress);
            return;
        }
        if (cue.Kind is BattleCueKind.Dying or BattleCueKind.Death or BattleCueKind.Rescued)
        {
            foreach (var seat in cue.TargetSeats)
                if (seats.TryGetValue(seat, out var target) && !target.IsEmpty && target.Width > 0 && target.Height > 0)
                {
                    // These are separate committed public transitions. A hit
                    // never predicts dying, death or a successful rescue.
                    var artwork = PortraitArtwork(target);
                    if (artwork.Width <= 8 || artwork.Height <= 8) continue;
                    dc.PushClip(new RectangleGeometry(artwork));
                    dc.PushOpacity(Pulse(progress, 0, 1));
                    if (cue.Kind == BattleCueKind.Dying) DyingPulse(dc, artwork, progress);
                    else if (cue.Kind == BattleCueKind.Death) DeathClosure(dc, artwork, progress);
                    else RescueRise(dc, artwork, progress);
                    dc.Pop();
                    dc.Pop();
                }
            return;
        }
        if (cue.HasResponseProgress)
        {
            if (!source.IsEmpty) ResponseProgress(dc, source, cue, progress);
            return;
        }
        if (cue.Kind == BattleCueKind.ChainPropagation)
        {
            if (!source.IsEmpty)
                foreach (var seat in cue.TargetSeats)
                    if (seats.TryGetValue(seat, out var target)) Chain(dc, source, target, cue.Nature, progress);
            return;
        }
        if (cue.Kind == BattleCueKind.Skill)
        {
            if (!source.IsEmpty) Skill(dc, source, progress);
            return;
        }
        if (cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery)
        {
            if (cue.Kind == BattleCueKind.Damage)
            {
                // The portrait hit is brief; its committed HP result can stay
                // readable while the next public relay reaches another seat.
                progress *= DamageDuration / DamageImpactTime;
                if (progress >= 1) return;
            }
            foreach (var seat in cue.TargetSeats)
                if (seats.TryGetValue(seat, out var target))
                    Impact(dc, target, cue.Kind == BattleCueKind.Recovery ? Green :
                        cue.Nature == DamageNature.Thunder ? Violet : Red, progress,
                        cue.Kind == BattleCueKind.Recovery ? GreenGlow :
                        cue.Nature == DamageNature.Fire ? FireGlow :
                        cue.Nature == DamageNature.Thunder ? ThunderGlow : FireGlow,
                        cue.Kind == BattleCueKind.Recovery, cue.Nature);
            return;
        }
        if (cue.Kind is not (BattleCueKind.Card or BattleCueKind.Response) || cue.Detail == "重铸") return;
        if (cue.Kind == BattleCueKind.Response && cue.DuelOpponentSeat is not null &&
            cue.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
        {
            if (!source.IsEmpty) DuelGuard(dc, source, progress);
            return;
        }
        if (cue.Kind == BattleCueKind.Response && cue.CardKind == CardKind.Nullification)
        {
            if (!source.IsEmpty) NullificationWeave(dc, source, progress);
            return;
        }
        if (cue.CardKind == CardKind.Dodge && !source.IsEmpty)
        {
            // Paying one response says nothing about the attack's final result.
            if (cue.Kind == BattleCueKind.Response) Dodge(dc, source, progress);
            return;
        }
        if (cue.CardKind is CardKind.Peach or CardKind.Alcohol && !source.IsEmpty)
        {
            Aura(dc, source, cue.CardKind == CardKind.Peach ? Green : Gold,
                cue.CardKind == CardKind.Peach ? GreenGlow : GoldGlow, progress);
            return;
        }
        foreach (var seat in cue.TargetSeats)
        {
            if (!seats.TryGetValue(seat, out var target)) continue;
            if (cue.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
                Slash(dc, source.IsEmpty ? target : source, target, cue.CardKind.Value, progress);
            else if (cue.Kind == BattleCueKind.Card && cue.CardKind == CardKind.Duel && !source.IsEmpty)
                DuelChallenge(dc, source, target, progress);
            else if (cue.CardKind is not null)
                Aura(dc, target, Cyan, BlueGlow, progress);
        }
    }

    public static void DrawGroupResponseFocus(DrawingContext dc, BattleCue cue, Rect responder, double progress)
    {
        if (cue.Kind != BattleCueKind.ResponseWindow || !cue.IsInquiry ||
            cue.CardKind is not (CardKind.ArrowBarrage or CardKind.BarbarianAssault) ||
            responder.IsEmpty || responder.Width <= 0 || responder.Height <= 0 || progress <= 0 || progress >= .82) return;
        var artwork = PortraitArtwork(responder);
        if (artwork.Width <= 8 || artwork.Height <= 8) return;
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(progress, .01, .82));
        // This is the current public response request, not a hit. Projectiles
        // approach and stop without impact sparks, a success shield or HP tint.
        if (cue.CardKind == CardKind.ArrowBarrage)
        {
            for (var index = 0; index < 3; index++)
            {
                var travel = 1 - Math.Pow(1 - Math.Clamp((progress - index * .045) / .38, 0, 1), 2);
                var head = new Point(artwork.Left + artwork.Width * (.18 + index * .28 + .08 * travel),
                    artwork.Top + artwork.Height * (-.1 + (.62 + index * .045) * travel));
                var direction = new Vector(.18, 1); direction.Normalize();
                var length = Math.Min(artwork.Height * .32, artwork.Width * .55);
                ResponseProjectile(dc, head, direction, length, Gold, feathered: true);
            }
        }
        else
        {
            var travel = 1 - Math.Pow(1 - Math.Clamp(progress / .43, 0, 1), 3);
            for (var side = -1; side <= 1; side += 2)
            {
                var head = Center(artwork) + new Vector(side * artwork.Width * (.7 - .56 * travel),
                    artwork.Height * (.4 - .3 * travel + side * .055));
                var direction = new Vector(-side * .8, -.6);
                var length = Math.Min(artwork.Height * .55, artwork.Width * .8);
                ResponseProjectile(dc, head, direction, length, AshGold, feathered: false);
            }
        }
        dc.Pop();
        dc.Pop();
    }

    private static void ResponseProjectile(DrawingContext dc, Point head, Vector direction, double length, Brush shaft, bool feathered)
    {
        var normal = new Vector(-direction.Y, direction.X);
        var tail = head - direction * length;
        var breadth = Math.Clamp(length * (feathered ? .13 : .19), 2.2, 7);
        dc.DrawLine(new Pen(shaft, feathered ? 1.6 : 2.5) { StartLineCap = PenLineCap.Round }, tail, head);
        var tip = new StreamGeometry();
        using (var path = tip.Open())
        {
            path.BeginFigure(head, true, true);
            path.LineTo(head - direction * (breadth * 2.5) + normal * breadth, true, false);
            path.LineTo(head - direction * (breadth * 1.7), true, false);
            path.LineTo(head - direction * (breadth * 2.5) - normal * breadth, true, false);
        }
        tip.Freeze();
        dc.DrawGeometry(White, new Pen(shaft, .7), tip);
        if (!feathered) return;
        var feather = new Pen(shaft, 1.2);
        for (var index = 0; index < 2; index++)
        {
            var root = tail + direction * (breadth * (.8 + index));
            dc.DrawLine(feather, root, root - direction * breadth + normal * breadth);
            dc.DrawLine(feather, root, root - direction * breadth - normal * breadth);
        }
    }

    private static void DuelChallenge(DrawingContext dc, Rect source, Rect target, double p)
    {
        if (source.Width <= 0 || target.IsEmpty || target.Width <= 0 || p >= .65) return;
        var direction = Center(target) - Center(source);
        var distance = direction.Length;
        if (distance <= 1) return;
        direction.Normalize();
        double Edge(Rect bounds) => Math.Min(bounds.Width / (2 * Math.Max(.001, Math.Abs(direction.X))),
            bounds.Height / (2 * Math.Max(.001, Math.Abs(direction.Y)))) + 10;
        var from = Center(source) + direction * Edge(source);
        var to = Center(target) - direction * Edge(target);
        var gap = Vector.Multiply(to - from, direction);
        if (gap < 28) return;
        var middle = from + (to - from) * .5;
        var travel = 1 - Math.Pow(1 - Math.Clamp(p / .36, 0, 1), 2);
        var length = Math.Clamp(gap * .24, 18, 56);
        dc.PushOpacity(Pulse(p, .01, .65));
        for (var side = 0; side < 2; side++)
        {
            var facing = side == 0 ? direction : -direction;
            var start = side == 0 ? from : to;
            var head = start + (middle - facing * 7 - start) * travel;
            // Opposed blades meet in the open table corridor. Neither one
            // strikes a portrait or predicts who will fail the Duel.
            DuelBlade(dc, head - facing * length, facing, length, side == 0 ? Gold : Cyan);
        }
        dc.Pop();
    }

    private static void DuelGuard(DrawingContext dc, Rect responder, double p)
    {
        if (p >= .55 || responder.Width <= 0 || responder.Height <= 0) return;
        var artwork = PortraitArtwork(responder);
        var center = Center(artwork);
        var settle = 1 - Math.Pow(1 - Math.Clamp(p / .24, 0, 1), 3);
        var length = Math.Min(artwork.Width * .6, artwork.Height * .65);
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(p, .01, .55));
        for (var side = -1; side <= 1; side += 2)
        {
            var angle = (-90 + side * (24 + 22 * settle)) * Math.PI / 180;
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            var grip = center + new Vector(-side * artwork.Width * .2, artwork.Height * (.28 - .08 * settle));
            DuelBlade(dc, grip, direction, length, Cyan);
        }
        // Paying a Duel Slash is a local parry, never a fresh outgoing attack.
        dc.Pop();
        dc.Pop();
    }

    private static void DuelBlade(DrawingContext dc, Point grip, Vector direction, double length, Brush edge)
    {
        var normal = new Vector(-direction.Y, direction.X);
        var breadth = Math.Clamp(length * .075, 1.7, 4.5);
        var blade = new StreamGeometry();
        using (var path = blade.Open())
        {
            path.BeginFigure(grip - normal * breadth, true, true);
            path.LineTo(grip + direction * (length * .78) - normal * breadth, true, false);
            path.LineTo(grip + direction * length, true, false);
            path.LineTo(grip + direction * (length * .78) + normal * breadth, true, false);
            path.LineTo(grip + normal * breadth, true, false);
        }
        blade.Freeze();
        dc.DrawGeometry(White, new Pen(edge, 1), blade);
        dc.DrawLine(new Pen(edge, 2.3), grip - normal * breadth * 2.5, grip + normal * breadth * 2.5);
        dc.DrawLine(new Pen(AshGold, 3) { EndLineCap = PenLineCap.Round }, grip, grip - direction * breadth * 3);
    }

    private static void NullificationWeave(DrawingContext dc, Rect responder, double p)
    {
        if (p >= .66 || responder.Width <= 0 || responder.Height <= 0) return;
        var artwork = PortraitArtwork(responder);
        var center = Center(artwork);
        var close = 1 - Math.Pow(1 - Math.Clamp(p / .19, 0, 1), 2);
        var dissolve = Math.Clamp((p - .22) / .4, 0, 1);
        var half = artwork.Width * .35;
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(p, .01, .66));
        // A counterspell's woven seal separates into strips. The public chain
        // caption alone says whether the actual effect is restored or nullified.
        for (var row = 0; row < 4; row++)
        for (var side = -1; side <= 1; side += 2)
        {
            var offset = new Vector(side * half * ((1 - close) * .7 + dissolve * .55),
                (row - 1.5) * artwork.Height * (.13 + .1 * dissolve));
            var inner = center + offset + new Vector(side * half * .05, 0);
            var outer = inner + new Vector(side * half, -side * half * .18);
            var strip = new StreamGeometry();
            using (var path = strip.Open())
            {
                path.BeginFigure(inner + new Vector(0, -1.5), true, true);
                path.LineTo(outer + new Vector(0, -1.5), true, false);
                path.LineTo(outer + new Vector(-side * 4, 2), true, false);
                path.LineTo(inner + new Vector(side * 3, 2), true, false);
            }
            strip.Freeze();
            dc.DrawGeometry(row % 2 == 0 ? Cyan : Violet, null, strip);
        }
        dc.DrawLine(new Pen(White, 1.6 * (1 - dissolve)),
            center + new Vector(0, -artwork.Height * .28), center + new Vector(0, artwork.Height * .28));
        dc.Pop();
        dc.Pop();
    }

    private static void JudgmentConfirmation(DrawingContext dc, Rect artwork, double p)
    {
        if (artwork.Width <= 8 || artwork.Height <= 8) return;
        var center = Center(artwork);
        var travel = Math.Clamp(p / .55, 0, 1);
        Point Position(double t) => center + new Vector(artwork.Width * (-.32 + .64 * t),
            artwork.Height * (.18 - .16 * t - .1 * Math.Sin(t * Math.PI)));
        var stroke = new StreamGeometry();
        using (var path = stroke.Open())
        {
            path.BeginFigure(Position(0), false, false);
            for (var index = 1; index <= 12; index++)
            {
                var t = Math.Min(travel, index / 12.0);
                path.LineTo(Position(t), true, false);
                if (t >= travel) break;
            }
        }
        stroke.Freeze();
        var head = Position(travel);
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(p, 0, 1));
        dc.DrawGeometry(null, new Pen(Gold, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, stroke);
        dc.PushOpacity(.5);
        dc.DrawEllipse(GoldGlow, null, head, 10, 10);
        dc.Pop();
        dc.DrawEllipse(White, null, head, 1.7, 1.7);
        for (var index = 0; index < 3; index++)
        {
            var point = Position(Math.Max(0, travel - index * .14)) + new Vector(0, -3 - index * 2 - p * 4);
            dc.PushOpacity((1 - p) * .8);
            dc.DrawEllipse(Gold, null, point, 1.2, 1.2);
            dc.Pop();
        }
        // The same neutral gold marks every revealed effective judgment face.
        // It says nothing about the physical card's printed suit or win/loss.
        dc.Pop();
        dc.Pop();
    }

    private static void DyingPulse(DrawingContext dc, Rect artwork, double p)
    {
        var center = Center(artwork);
        // Two bounded heartbeats draw attention to the victim while leaving
        // the public name, role and health regions outside the artwork intact.
        var heartbeat = .4 + .6 * (.5 - .5 * Math.Cos(p * Math.PI * 4));
        var rim = artwork;
        rim.Inflate(-2 - (1 - heartbeat) * 2, -2 - (1 - heartbeat) * 2);
        dc.PushOpacity(.45 + .5 * heartbeat);
        dc.DrawEllipse(DyingGlow, null, center, artwork.Width * .48, artwork.Height * .48);
        dc.DrawRoundedRectangle(null, new Pen(Red, 1 + heartbeat), rim, 5, 5);
        var pulse = new StreamGeometry();
        using (var path = pulse.Open())
        {
            var baseline = center + new Vector(0, artwork.Height * .21);
            path.BeginFigure(baseline + new Vector(-artwork.Width * .32, 0), false, false);
            path.LineTo(baseline + new Vector(-artwork.Width * .1, 0), true, false);
            path.LineTo(baseline + new Vector(-artwork.Width * .035, artwork.Height * .06), true, false);
            path.LineTo(baseline + new Vector(artwork.Width * .035, -artwork.Height * .17 * heartbeat), true, false);
            path.LineTo(baseline + new Vector(artwork.Width * .1, 0), true, false);
            path.LineTo(baseline + new Vector(artwork.Width * .32, 0), true, false);
        }
        pulse.Freeze();
        dc.DrawGeometry(null, new Pen(Red, 1.5) { LineJoin = PenLineJoin.Round }, pulse);
        dc.Pop();
    }

    private static void DeathClosure(DrawingContext dc, Rect artwork, double p)
    {
        var center = Center(artwork);
        var close = 1 - Math.Pow(1 - p, 2);
        var halfWidth = artwork.Width * (.46 - .3 * close);
        var halfHeight = artwork.Height * (.46 - .3 * close);
        var length = Math.Min(artwork.Width, artwork.Height) * .17 * (1 - close * .35);
        var pen = new Pen(AshGold, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var x in new[] { -1, 1 })
            foreach (var y in new[] { -1, 1 })
            {
                var corner = center + new Vector(x * halfWidth, y * halfHeight);
                dc.DrawLine(pen, corner, corner - new Vector(x * length, 0));
                dc.DrawLine(pen, corner, corner - new Vector(0, y * length));
            }
        // A brief closing mark settles downward; the snapshot owns the final
        // dead-seat presentation, so this effect adds no persistent shade.
        dc.PushOpacity((1 - p) * .65);
        for (var index = 0; index < 4; index++)
        {
            var point = center + new Vector((index - 1.5) * artwork.Width * .085,
                artwork.Height * (.05 + .24 * p) + index * 2);
            dc.DrawLine(new Pen(AshGold, 1), point, point + new Vector(0, 3 + 4 * p));
        }
        dc.Pop();
    }

    private static void RescueRise(DrawingContext dc, Rect artwork, double p)
    {
        var rise = 1 - Math.Pow(1 - p, 3);
        var center = Center(artwork) + new Vector(0, artwork.Height * (.14 - .23 * rise));
        dc.PushOpacity(.5);
        dc.DrawEllipse(GreenGlow, null, center, artwork.Width * .43, artwork.Height * .42);
        dc.Pop();
        for (var index = 0; index < 2; index++)
        {
            var width = artwork.Width * (.32 + index * .1);
            var bottom = center + new Vector(0, artwork.Height * (.13 + index * .12));
            var arc = new StreamGeometry();
            using (var path = arc.Open())
            {
                path.BeginFigure(bottom + new Vector(-width, -artwork.Height * .13), false, false);
                path.BezierTo(bottom + new Vector(-width * .65, artwork.Height * .03),
                    bottom + new Vector(width * .65, artwork.Height * .03),
                    bottom + new Vector(width, -artwork.Height * .13), true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, new Pen(index == 0 ? Green : Gold, index == 0 ? 2 : 1.2)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }
        for (var index = 0; index < 5; index++)
        {
            var point = center + new Vector((index - 2) * artwork.Width * .13,
                artwork.Height * (.2 - .3 * rise) + Math.Sin(index * 2) * 3);
            dc.PushOpacity(.5 + .4 * Math.Sin(p * Math.PI));
            dc.DrawEllipse(index % 2 == 0 ? Green : Gold, null, point, 1.4, 1.4);
            dc.Pop();
        }
    }

    private static void WeaponActivation(DrawingContext dc, Rect source, double p)
    {
        if (source.Width <= 0 || source.Height <= 0) return;
        p /= .5;
        if (p >= 1) return;
        var artwork = PortraitArtwork(source);
        var center = Center(artwork);
        var size = Math.Min(artwork.Width, artwork.Height);
        var blade = new Vector(size * .19, -size * .24);
        var hilt = new Vector(size * .1, size * .08);
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(p, .01, .95));
        dc.DrawEllipse(GoldGlow, null, center, size * .43, size * .43);
        dc.DrawLine(new Pen(Gold, 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center - blade, center + blade);
        dc.DrawLine(new Pen(White, 1.1), center, center + blade);
        dc.DrawLine(new Pen(Gold, 2), center - blade * .45 - hilt, center - blade * .45 + hilt);
        for (var index = 0; index < 3; index++)
        {
            var offset = new Vector(-size * (.26 + index * .055), size * (.02 + index * .11));
            dc.PushOpacity((1 - p) * .6);
            dc.DrawLine(new Pen(Gold, 1.3), center + offset, center + offset + blade * .28);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }

    private static void StoneAxe(DrawingContext dc, Rect source, Rect target, double p)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.IsEmpty || target.Width <= 0 || target.Height <= 0) return;
        var direction = Center(target) - Center(source);
        if (direction.Length < 40) return;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        double Edge(Rect bounds) => Math.Min(bounds.Width / (2 * Math.Max(.001, Math.Abs(direction.X))),
            bounds.Height / (2 * Math.Max(.001, Math.Abs(direction.Y)))) + 16;
        var start = Center(source) + direction * Edge(source);
        var finish = Center(target) - direction * Edge(target);
        if (Vector.Multiply(finish - start, direction) <= 0) return;
        var travel = Math.Clamp(p * WeaponDuration / WeaponArrivalTime, 0, 1);
        var head = start + (finish - start) * (1 - Math.Pow(1 - travel, 2));
        var tail = head - direction * Math.Min(70, (finish - start).Length * travel);
        var corridor = Rect.Union(source, target);
        corridor.Inflate(32, 32);
        Geometry clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(corridor), new RectangleGeometry(source));
        clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(target));
        dc.PushClip(clip);
        dc.PushOpacity(Pulse(p, .01, .82));
        Ribbon(dc, tail, head, normal, 3, Gold);
        dc.DrawLine(new Pen(White, 1), tail + direction * 8 * travel, head);
        var axe = new StreamGeometry();
        using (var path = axe.Open())
        {
            path.BeginFigure(head - normal * 13 - direction * 6, true, true);
            path.BezierTo(head - normal * 5 + direction * 7, head + normal * 10 + direction * 9,
                head + normal * 16 - direction * 8, true, false);
            path.LineTo(head + normal * 5 - direction * 4, true, false);
        }
        axe.Freeze();
        dc.DrawGeometry(Gold, null, axe);
        dc.DrawLine(new Pen(White, 1), head - normal * 8, head + normal * 10 - direction * 2);
        // Reaching the original target edge only announces that this Slash
        // continues. A separate committed Damage cue owns any portrait hit.
        dc.Pop();
        dc.Pop();
    }

    private static void Chain(DrawingContext dc, Rect source, Rect target, DamageNature nature, double p)
    {
        var from = Center(source); var to = Center(target);
        var direction = to - from;
        if (direction.Length < 40) return;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        double Edge(Rect bounds) => Math.Min(bounds.Width / (2 * Math.Max(.001, Math.Abs(direction.X))),
            bounds.Height / (2 * Math.Max(.001, Math.Abs(direction.Y)))) + 10;
        var start = from + direction * Edge(source); var finish = to - direction * Edge(target);
        if (Vector.Multiply(finish - start, direction) <= 0) return;
        var travel = Math.Clamp(p * ChainDuration / ChainArrivalTime, 0, 1);
        var span = finish - start;
        var bend = Math.Min(30, span.Length * .08);
        Point Position(double t)
        {
            var knot = t * 8; var index = Math.Min(7, (int)knot);
            var zigzag = nature == DamageNature.Thunder ? (index % 2 == 0 ? 1 : -1) * (1 - 2 * (knot - index)) * 7 : 0;
            return start + span * t + normal * (Math.Sin(t * Math.PI) * (bend + zigzag));
        }
        var path = new StreamGeometry();
        using (var route = path.Open())
        {
            route.BeginFigure(start, false, false);
            var segments = nature == DamageNature.Thunder ? 8 : 24;
            for (var index = 1; index <= segments; index++)
            {
                var t = Math.Min(travel, index / (double)segments);
                route.LineTo(Position(t), true, false);
                if (t >= travel) break;
            }
        }
        path.Freeze();
        var ink = nature == DamageNature.Thunder ? Violet : Red;
        var glow = nature == DamageNature.Thunder ? ThunderGlow : FireGlow;
        var head = Position(travel);
        dc.PushOpacity(Math.Min(1, p / .08) * Math.Min(1, (1 - p) / .5));
        dc.PushOpacity(.25);
        dc.DrawGeometry(null, new Pen(ink, 10) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
        dc.Pop();
        dc.DrawGeometry(null, new Pen(ink, 3.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
        if (nature == DamageNature.Thunder) dc.DrawGeometry(null, new Pen(White, .9), path);
        dc.DrawEllipse(glow, null, head, 26, 26);
        dc.DrawEllipse(White, null, head, 3, 3);
        for (var index = 0; index < 5; index++)
        {
            var t = Math.Max(0, travel - index * .045);
            var ember = Position(t) + normal * Math.Sin(index * 2 + p * 11) * 6;
            dc.DrawEllipse(ink, null, ember, 2.5 - index * .3, 2.5 - index * .3);
        }
        // The route announces propagation. Only a committed Damage cue strikes the portrait.
        dc.Pop();
    }

    private static void Slash(DrawingContext dc, Rect source, Rect target, CardKind kind, double p)
    {
        var ink = kind == CardKind.FireSlash ? Red : kind == CardKind.ThunderSlash ? Violet : Gold;
        var glow = kind == CardKind.FireSlash ? FireGlow : kind == CardKind.ThunderSlash ? ThunderGlow : GoldGlow;
        var from = Center(source);
        var to = Center(target);
        var travel = Math.Clamp((p - .08) / .33, 0, 1);
        var direction = to - from;
        if (direction.Length <= 1 || p >= .65) return;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        double Edge(Rect bounds) => Math.Min(bounds.Width / (2 * Math.Max(.001, Math.Abs(direction.X))),
            bounds.Height / (2 * Math.Max(.001, Math.Abs(direction.Y)))) + 12;
        var start = from + direction * Edge(source);
        var finish = to - direction * Edge(target);
        if (Vector.Multiply(finish - start, direction) <= 0) return;
        // Build speed toward the existing arrival boundary, then draw the tail
        // back into that fixed endpoint instead of leaving a full blade parked there.
        var distance = (finish - start).Length * travel * travel;
        var head = start + direction * distance;
        var remaining = 1 - Math.Clamp((p - .41) / .24, 0, 1);
        var strokeScale = (.55 + .45 * travel) * remaining;
        var tailLength = Math.Min(145, distance) * remaining * remaining;
        var tail = head - direction * tailLength;
        dc.PushOpacity(Pulse(p, .02, .65));
        var glowLength = (12 + Math.Min(56, tailLength * .38)) * strokeScale;
        var glowWidth = (8 + 10 * travel) * strokeScale;
        var glowCenter = head - direction * (glowLength - 6 * strokeScale);
        dc.PushTransform(new MatrixTransform(direction.X, direction.Y, normal.X, normal.Y, glowCenter.X, glowCenter.Y));
        dc.DrawEllipse(glow, null, new Point(), glowLength, glowWidth);
        dc.Pop();
        Ribbon(dc, tail, head, normal, 10 * strokeScale, ink);
        // A short route or its first frame must not put the inner tail ahead of the tip.
        Ribbon(dc, tail + direction * Math.Min(16 * strokeScale, tailLength * .35), head, normal, 2 * strokeScale, White);
        // The blade approaches the portrait edge. Only a committed Damage cue
        // may strike the portrait; an attack declaration can still be dodged.
        var bladeNormal = normal * strokeScale;
        var bladeDirection = direction * strokeScale;
        var sweep = new StreamGeometry();
        using (var path = sweep.Open())
        {
            path.BeginFigure(head - bladeNormal * 43 - bladeDirection * 10, true, true);
            path.BezierTo(head - bladeNormal * 18 + bladeDirection * 26, head + bladeNormal * 25 + bladeDirection * 24,
                head + bladeNormal * 48 - bladeDirection * 20, true, false);
            path.BezierTo(head + bladeNormal * 16 + bladeDirection * 7, head - bladeNormal * 20 + bladeDirection * 10,
                head - bladeNormal * 43 - bladeDirection * 10, true, false);
        }
        sweep.Freeze();
        dc.DrawGeometry(ink, null, sweep);
        dc.DrawLine(new Pen(White, 2 * strokeScale), head - bladeNormal * 29, head + bladeNormal * 30 + bladeDirection * 7);
        if (kind == CardKind.FireSlash)
        {
            dc.PushOpacity(.45);
            Flames(dc, tail, 35 * strokeScale, travel);
            dc.Pop();
        }
        if (kind == CardKind.ThunderSlash)
        {
            var bolt = new StreamGeometry();
            using (var path = bolt.Open())
            {
                path.BeginFigure(tail, false, false);
                for (var i = 1; i <= 5; i++)
                    path.LineTo(tail + (head - tail) * (i / 5d) + bladeNormal * (i == 5 ? 0 : Math.Sin(i * 2 + Math.Floor(p * 10)) * 16), true, false);
            }
            bolt.Freeze();
            dc.DrawGeometry(null, new Pen(White, 2 * strokeScale), bolt);
        }
        dc.Pop();
    }

    private static void Dodge(DrawingContext dc, Rect bounds, double p)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        p /= .48;
        if (p >= 1) return;
        var artwork = PortraitArtwork(bounds);
        var center = Center(artwork);
        var shift = artwork.Width * .22 * (1 - Math.Pow(1 - p, 3));
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(p, .02, .96));
        // Open silhouettes trail a short sidestep inside the artwork. Keep the
        // name, public HP and identity visible; never close a success shield.
        for (var i = 0; i < 3; i++)
        {
            var ghost = center + new Vector(shift - artwork.Width * .15 * i, -artwork.Height * .025 * i);
            var trace = new StreamGeometry();
            using (var path = trace.Open())
            {
                path.BeginFigure(ghost + new Vector(artwork.Width * .02, -artwork.Height * .34), false, false);
                path.BezierTo(ghost + new Vector(-artwork.Width * .23, -artwork.Height * .28),
                    ghost + new Vector(-artwork.Width * .29, artwork.Height * .08),
                    ghost + new Vector(-artwork.Width * .09, artwork.Height * .35), true, false);
            }
            trace.Freeze();
            dc.PushOpacity(i == 0 ? .9 : .5 / i);
            dc.DrawGeometry(null, new Pen(Cyan, i == 0 ? 2.6 : 1.8)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, trace);
            dc.Pop();
        }
        var sweep = new StreamGeometry();
        using (var path = sweep.Open())
        {
            path.BeginFigure(center + new Vector(-artwork.Width * .37, artwork.Height * .22), false, false);
            path.BezierTo(center + new Vector(-artwork.Width * .14, artwork.Height * .1),
                center + new Vector(shift + artwork.Width * .1, -artwork.Height * .06),
                center + new Vector(shift + artwork.Width * .16, -artwork.Height * .25), true, false);
        }
        sweep.Freeze();
        dc.DrawGeometry(null, new Pen(Cyan, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, sweep);
        dc.DrawLine(new Pen(Cyan, 1.4), center + new Vector(-artwork.Width * .38, -artwork.Height * .13),
            center + new Vector(shift - artwork.Width * .17, -artwork.Height * .13));
        dc.DrawLine(new Pen(Cyan, 1.4), center + new Vector(-artwork.Width * .39, artwork.Height * .04),
            center + new Vector(shift - artwork.Width * .22, artwork.Height * .04));
        dc.Pop();
        dc.Pop();
    }

    private static void ResponseProgress(DrawingContext dc, Rect bounds, BattleCue cue, double p)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || p >= .78) return;
        var artwork = PortraitArtwork(bounds);
        var center = new Point(artwork.Left + artwork.Width * .5, artwork.Top + artwork.Height * .43);
        var radii = new Size(artwork.Width * .43, artwork.Height * .33);
        // Exact public numbers remain in the cue label. Dense requests use a
        // finite coarse meter rather than allocating one stroke per response.
        var ticks = Math.Min(6, cue.RequiredResponseCount);
        var filled = ticks * (cue.ResponseCount / (double)cue.RequiredResponseCount);
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(Pulse(p, .01, .78));
        for (var index = 0; index < ticks; index++)
        {
            var start = (200 + index * 140d / ticks + 3) * Math.PI / 180;
            var end = (200 + (index + 1) * 140d / ticks - 3) * Math.PI / 180;
            Point OnArc(double angle) => center + new Vector(radii.Width * Math.Cos(angle), radii.Height * Math.Sin(angle));
            StreamGeometry Arc(double finish)
            {
                var tick = new StreamGeometry();
                using (var path = tick.Open())
                {
                    path.BeginFigure(OnArc(start), false, false);
                    path.ArcTo(OnArc(finish), radii, 0, false, SweepDirection.Clockwise, true, false);
                }
                tick.Freeze();
                return tick;
            }
            var pen = new Pen(Cyan, Math.Clamp(bounds.Width / 65, 1.6, 2.8))
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.PushOpacity(.24);
            dc.DrawGeometry(null, pen, Arc(end));
            dc.Pop();
            var fraction = Math.Clamp(filled - index, 0, 1);
            // A near-complete dense request still has an unanswered response.
            // Keep a visible end gap until the exact public count is complete.
            if (index == ticks - 1 && cue.ResponseCount < cue.RequiredResponseCount)
                fraction = Math.Min(fraction, .8);
            if (fraction > 0) dc.DrawGeometry(null, pen, Arc(start + (end - start) * fraction));
        }
        dc.Pop();
        dc.Pop();
    }

    private static void Impact(DrawingContext dc, Rect target, Brush ink, double p, Brush glow, bool recovery, DamageNature nature)
    {
        if (target.IsEmpty || target.Width <= 0 || target.Height <= 0) return;
        if (!recovery)
        {
            DamageImpact(dc, target, ink, p, glow, nature);
            return;
        }
        var center = Center(target);
        var radius = Math.Clamp(Math.Max(target.Width, target.Height) * .65, 65, 112);
        dc.PushOpacity(Pulse(p, .01, .9));
        dc.DrawEllipse(glow, null, center, radius * (1.1 + .25 * p), radius * (1.1 + .25 * p));
        dc.DrawRoundedRectangle(null, new Pen(ink, 3), target, 7, 7);
        dc.DrawEllipse(null, new Pen(ink, 2 * (1 - p) + .5), center,
            radius * (.3 + .85 * p), radius * (.3 + .85 * p));
        Sparks(dc, center, radius, p, ink, 10);
        for (var i = 0; i < 5; i++)
        {
            var point = center + new Vector((i - 2) * 27 + Math.Sin(i * 2 + p * 5) * 8, radius * .5 - p * radius * 1.4);
            dc.DrawLine(new Pen(Green, 2), point + new Vector(-5, 0), point + new Vector(5, 0));
            dc.DrawLine(new Pen(Green, 2), point + new Vector(0, -5), point + new Vector(0, 5));
        }
        dc.Pop();
    }

    private static void DamageImpact(DrawingContext dc, Rect target, Brush ink, double p, Brush glow, DamageNature nature)
    {
        // Seat anchors include the top identity, left name and bottom HP. Keep
        // elemental motion in the portrait artwork, within the actual seat.
        var artwork = PortraitArtwork(target);
        var center = Center(artwork);
        var radius = Math.Min(artwork.Width, artwork.Height) * .55;
        dc.PushClip(new RectangleGeometry(target));
        dc.PushOpacity(Pulse(p, .01, .9));
        dc.DrawRoundedRectangle(null, new Pen(ink, Math.Clamp(target.Width / 100, 1.2, 2)), target, 7, 7);
        dc.PushClip(new RectangleGeometry(artwork));
        dc.DrawEllipse(glow, null, center, artwork.Width * (.55 + .12 * p), artwork.Height * (.55 + .12 * p));
        // Preserve the target's committed hit geometry, but never tint its
        // identity, vertical name, health text or health meter.
        dc.PushOpacity(Math.Max(0, 1 - p / .22) * .4);
        dc.DrawRoundedRectangle(ink, null, target, 7, 7);
        dc.Pop();
        dc.DrawEllipse(null, new Pen(ink, 1.5 * (1 - p) + .5), center,
            radius * (.3 + .7 * p), radius * (.3 + .7 * p));
        Sparks(dc, center, radius, p, ink, 10);
        if (nature == DamageNature.Fire) PortraitFlames(dc, artwork, p);
        else if (nature == DamageNature.Thunder) PortraitLightning(dc, artwork, p);
        else
        {
            dc.PushOpacity(1 - p);
            var cut = new Vector(radius * .7, -radius * .5);
            dc.DrawLine(new Pen(ink, 12 * (1 - p) + 1), center - cut, center + cut);
            dc.DrawLine(new Pen(White, 3), center - cut, center + cut);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void Skill(DrawingContext dc, Rect bounds, double p)
    {
        // The owner's short ink seal identifies activation. Its readable name
        // has a separate lifetime; only committed results may paint a hit.
        var artwork = PortraitArtwork(bounds);
        if (artwork.Width <= 8 || artwork.Height <= 8 || p >= .52) return;
        var center = Center(artwork);
        var unfold = 1 - Math.Pow(1 - Math.Clamp(p / .18, 0, 1), 3);
        var half = Math.Min(34, Math.Min(artwork.Width, artwork.Height) * (.2 + .12 * unfold));
        var opacity = Math.Min(1, p / .035) * Math.Clamp((.52 - p) / .28, 0, 1);
        dc.PushClip(new RectangleGeometry(artwork));
        dc.PushOpacity(opacity);
        Point At(double x, double y) => center + new Vector(x * half, y * half);
        for (var side = -1; side <= 1; side += 2)
        {
            var brushstroke = new StreamGeometry();
            using (var path = brushstroke.Open())
            {
                path.BeginFigure(At(side * -1.25, side * .55), false, false);
                path.BezierTo(At(side * -1.4, side * -.8), At(side * -.3, side * -1.38),
                    At(side * (.1 + .7 * unfold), side * -1.18), true, false);
            }
            brushstroke.Freeze();
            dc.DrawGeometry(null, new Pen(SkillInk, 4), brushstroke);
            dc.DrawGeometry(null, new Pen(AshGold, .7), brushstroke);
        }

        var seal = new StreamGeometry();
        using (var path = seal.Open())
        {
            // Broken corners keep the stamp open over the portrait artwork.
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            {
                path.BeginFigure(At(x * .38, y), false, false);
                path.LineTo(At(x, y), true, false);
                path.LineTo(At(x, y * .38), true, false);
            }
        }
        seal.Freeze();
        dc.DrawGeometry(null, new Pen(SkillInk, 5), seal);
        dc.DrawGeometry(null, new Pen(SkillSeal, 2.5), seal);
        var carving = new StreamGeometry();
        using (var path = carving.Open())
        {
            for (var side = -1; side <= 1; side += 2)
            {
                path.BeginFigure(At(side * .62, -.55), false, false);
                path.LineTo(At(side * .22, -.55), true, false);
                path.LineTo(At(side * .22, .52), true, false);
                path.LineTo(At(side * .65, .52), true, false);
                path.LineTo(At(side * .65, .08), true, false);
                path.LineTo(At(side * .42, .08), true, false);
            }
        }
        carving.Freeze();
        dc.DrawGeometry(null, new Pen(SkillInk, 4), carving);
        dc.DrawGeometry(null, new Pen(AshGold, 1.4), carving);
        dc.Pop();
        dc.Pop();
    }

    private static void Aura(DrawingContext dc, Rect bounds, Brush ink, Brush glow, double p)
    {
        var center = Center(bounds);
        var radius = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * .6, 60, 105);
        dc.PushOpacity(Pulse(p, .04, .88) * .7);
        dc.DrawEllipse(glow, null, center, radius, radius);
        dc.DrawEllipse(null, new Pen(ink, 2), center, radius * (.6 + .4 * p), radius * (.6 + .4 * p));
        Sparks(dc, center, radius, p, ink, 8);
        dc.Pop();
    }

    private static void Sparks(DrawingContext dc, Point center, double radius, double p, Brush ink, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = i * 2.399963 + .3;
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            var distance = radius * (.3 + p * (i % 3 * .18 + .75));
            var point = center + direction * distance;
            dc.PushOpacity((1 - p) * (i % 2 == 0 ? .95 : .5));
            dc.DrawLine(new Pen(ink, i % 3 == 0 ? 2.5 : 1.3), point, point - direction * (5 + (1 - p) * 13));
            dc.DrawEllipse(White, null, point, i % 3 == 0 ? 2 : 1, i % 3 == 0 ? 2 : 1);
            dc.Pop();
        }
    }

    private static void Flames(DrawingContext dc, Point center, double radius, double p)
    {
        for (var i = 0; i < 7; i++)
        {
            var foot = center + new Vector((i - 3) * radius * .19, radius * .5);
            var height = radius * (.8 + .3 * Math.Sin(i * 3.7 + p * 9));
            var fire = new StreamGeometry();
            using (var path = fire.Open())
            {
                path.BeginFigure(foot + new Vector(-12, 0), true, true);
                path.BezierTo(foot + new Vector(-24, -height * .35), foot + new Vector(18, -height * .7),
                    foot + new Vector(Math.Sin(i + p * 8) * 20, -height), true, false);
                path.BezierTo(foot + new Vector(32, -height * .55), foot + new Vector(22, -height * .15),
                    foot + new Vector(12, 0), true, false);
            }
            fire.Freeze();
            dc.PushOpacity(.6);
            dc.DrawGeometry(i % 2 == 0 ? Red : Gold, null, fire);
            dc.Pop();
        }
    }

    private static void PortraitFlames(DrawingContext dc, Rect area, double p)
    {
        for (var i = 0; i < 5; i++)
        {
            var foot = new Point(area.Left + area.Width * (.13 + i * .185), area.Bottom - area.Height * .02);
            var height = area.Height * (.62 + .2 * Math.Sin(i * 3.7 + p * 9));
            var breadth = area.Width * .1;
            var fire = new StreamGeometry();
            using (var path = fire.Open())
            {
                path.BeginFigure(foot + new Vector(-breadth * .6, 0), true, true);
                path.BezierTo(foot + new Vector(-breadth * 1.2, -height * .35), foot + new Vector(breadth * .9, -height * .7),
                    foot + new Vector(Math.Sin(i + p * 8) * breadth * .8, -height), true, false);
                path.BezierTo(foot + new Vector(breadth * 1.6, -height * .55), foot + new Vector(breadth * 1.1, -height * .15),
                    foot + new Vector(breadth * .6, 0), true, false);
            }
            fire.Freeze();
            dc.PushOpacity(.6);
            dc.DrawGeometry(i % 2 == 0 ? Red : Gold, null, fire);
            dc.Pop();
        }
    }

    private static void PortraitLightning(DrawingContext dc, Rect area, double p)
    {
        for (var branch = 0; branch < 3; branch++)
        {
            var line = new StreamGeometry();
            using (var path = line.Open())
            {
                var main = branch == 1;
                var top = new Point(area.Left + area.Width * (.22 + branch * .28),
                    area.Top + area.Height * (main ? .02 : .25));
                var height = area.Height * (main ? .94 : .53);
                path.BeginFigure(top, false, false);
                for (var i = 1; i <= 6; i++)
                    path.LineTo(top + new Vector(Math.Sin(i * 2.9 + branch + Math.Floor(p * 5)) * area.Width * (main ? .13 : .06),
                        i * height / 6), true, false);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(Violet, branch == 1 ? 2.4 : 1.3) { LineJoin = PenLineJoin.Round }, line);
            dc.DrawGeometry(null, new Pen(White, branch == 1 ? .85 : .6) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    private static void Ribbon(DrawingContext dc, Point start, Point end, Vector normal, double width, Brush brush)
    {
        var ribbon = new StreamGeometry();
        using (var path = ribbon.Open())
        {
            path.BeginFigure(start, true, true);
            path.LineTo(end + normal * width, true, false);
            path.LineTo(end - normal * width, true, false);
        }
        ribbon.Freeze();
        dc.DrawGeometry(brush, null, ribbon);
    }

    private static double Pulse(double p, double start, double end) =>
        Math.Clamp((p - start) / .08, 0, 1) * Math.Clamp((end - p) / .22, 0, 1);
    private static Rect PortraitArtwork(Rect bounds) => new(bounds.Left + bounds.Width * .24,
        bounds.Top + bounds.Height * .2, bounds.Width * .6, bounds.Height * .46);
    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    private static Brush Solid(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
    private static Brush Glow(Color color) => Freeze(new RadialGradientBrush(new GradientStopCollection
    { new(color, 0), new(Color.FromArgb((byte)(color.A / 2), color.R, color.G, color.B), .45), new(Colors.Transparent, 1) }));
    private static T Freeze<T>(T value) where T : Freezable { value.Freeze(); return value; }
}
