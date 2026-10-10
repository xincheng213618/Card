using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.Controls;

/// <summary>Bounded vector effects. All positions and identities come from public presentation cues.</summary>
internal static class BattleEffectPainter
{
    private static readonly Brush Gold = Solid(255, 203, 100);
    private static readonly Brush White = Solid(255, 249, 218);
    private static readonly Brush Cyan = Solid(116, 231, 255);
    private static readonly Brush Red = Solid(255, 91, 62);
    private static readonly Brush Violet = Solid(175, 151, 255);
    private static readonly Brush Green = Solid(134, 255, 179);
    private static readonly Brush GoldGlow = Glow(Color.FromArgb(190, 255, 178, 55));
    private static readonly Brush BlueGlow = Glow(Color.FromArgb(160, 53, 195, 255));
    private static readonly Brush FireGlow = Glow(Color.FromArgb(200, 255, 73, 25));
    private static readonly Brush ThunderGlow = Glow(Color.FromArgb(185, 153, 94, 255));
    private static readonly Brush GreenGlow = Glow(Color.FromArgb(150, 76, 237, 144));
    private static readonly Brush Banner = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(Colors.Transparent, 0), new(Color.FromArgb(240, 38, 28, 21), .18),
        new(Color.FromArgb(240, 38, 28, 21), .82), new(Colors.Transparent, 1)
    }, new Point(0, .5), new Point(1, .5)));
    private static readonly Typeface Calligraphy = new("KaiTi");

    public static void Draw(DrawingContext dc, BattleCue cue, double progress, Rect source,
        IReadOnlyDictionary<int, Rect> seats, double pixelsPerDip)
    {
        if (cue.IsInquiry || progress <= 0 || progress >= 1) return;
        if (cue.Kind == BattleCueKind.Skill)
        {
            if (!source.IsEmpty) Skill(dc, source, cue.Label, progress, pixelsPerDip);
            return;
        }
        if (cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery)
        {
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
        if (cue.CardKind == CardKind.Dodge && !source.IsEmpty)
        {
            Dodge(dc, source, progress);
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
            else if (cue.CardKind is not null)
                Aura(dc, target, Cyan, BlueGlow, progress);
        }
    }

    private static void Slash(DrawingContext dc, Rect source, Rect target, CardKind kind, double p)
    {
        var ink = kind == CardKind.FireSlash ? Red : kind == CardKind.ThunderSlash ? Violet : Gold;
        var glow = kind == CardKind.FireSlash ? FireGlow : kind == CardKind.ThunderSlash ? ThunderGlow : GoldGlow;
        var from = Center(source);
        var to = Center(target);
        var travel = Math.Clamp((p - .08) / .33, 0, 1);
        var direction = to - from;
        if (direction.Length > 1)
        {
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            var head = from + (to - from) * (1 - Math.Pow(1 - travel, 2));
            dc.PushOpacity(Pulse(p, .06, .62));
            dc.DrawEllipse(glow, null, head, 48, 48);
            var tail = head - direction * Math.Min(160, (to - from).Length * travel);
            Ribbon(dc, tail, head, normal, 12, ink);
            Ribbon(dc, tail + direction * 25, head, normal, 3, White);
            dc.Pop();
        }
        var strike = Math.Clamp((p - .24) / .55, 0, 1);
        if (p < .24 || p > .85) return;
        var radius = Math.Clamp(Math.Max(target.Width, target.Height) * .65, 60, 115);
        dc.PushOpacity(Pulse(p, .24, .85));
        dc.DrawEllipse(glow, null, to, radius * 1.2, radius * 1.2);
        dc.PushTransform(new RotateTransform(-37, to.X, to.Y));
        var sweep = new StreamGeometry();
        using (var path = sweep.Open())
        {
            path.BeginFigure(to + new Vector(-radius, radius * .45), true, true);
            path.BezierTo(to + new Vector(-radius * .4, -radius * .45), to + new Vector(radius * .6, -radius * .8),
                to + new Vector(radius * 1.1, -radius * .25), true, false);
            path.BezierTo(to + new Vector(radius * .1, -radius * .32), to + new Vector(-radius * .15, radius * .25),
                to + new Vector(-radius, radius * .45), true, false);
        }
        sweep.Freeze();
        dc.DrawGeometry(ink, null, sweep);
        dc.DrawLine(new Pen(White, 3), to + new Vector(-radius * .8, radius * .25), to + new Vector(radius, -radius * .3));
        dc.Pop();
        Sparks(dc, to, radius, strike, ink, 14);
        if (kind == CardKind.FireSlash) Flames(dc, to, radius, strike);
        if (kind == CardKind.ThunderSlash) Lightning(dc, to, radius, strike);
        dc.Pop();
    }

    private static void Dodge(DrawingContext dc, Rect bounds, double p)
    {
        var center = Center(bounds);
        var radius = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * .6, 65, 110);
        dc.PushOpacity(Pulse(p, .02, .88));
        dc.DrawEllipse(BlueGlow, null, center, radius * 1.35, radius * 1.35);
        for (var i = 1; i <= 3; i++)
        {
            var ghost = bounds;
            ghost.Offset((-1 + 2 * (i % 2)) * (18 + p * 60) * i / 3, -8 * i);
            dc.PushOpacity(.35 / i);
            dc.DrawRoundedRectangle(null, new Pen(Cyan, 2), ghost, 8, 8);
            dc.Pop();
        }
        dc.DrawEllipse(null, new Pen(Cyan, 3), center, radius * (.75 + p * .22), radius * (.85 + p * .22));
        var shield = new StreamGeometry();
        using (var path = shield.Open())
        {
            path.BeginFigure(center + new Vector(0, -radius * .65), false, true);
            path.LineTo(center + new Vector(radius * .48, -radius * .38), true, false);
            path.BezierTo(center + new Vector(radius * .5, radius * .25), center + new Vector(radius * .22, radius * .48),
                center + new Vector(0, radius * .7), true, false);
            path.BezierTo(center + new Vector(-radius * .22, radius * .48), center + new Vector(-radius * .5, radius * .25),
                center + new Vector(-radius * .48, -radius * .38), true, false);
        }
        shield.Freeze();
        dc.DrawGeometry(null, new Pen(White, 3), shield);
        Sparks(dc, center, radius, p, Cyan, 12);
        dc.Pop();
    }

    private static void Impact(DrawingContext dc, Rect target, Brush ink, double p, Brush glow, bool recovery, DamageNature nature)
    {
        var center = Center(target);
        var radius = Math.Clamp(Math.Max(target.Width, target.Height) * .65, 65, 112);
        dc.PushOpacity(Pulse(p, .01, .9));
        dc.DrawEllipse(glow, null, center, radius * 1.2, radius * 1.2);
        var shock = target;
        shock.Offset(recovery ? 0 : Math.Sin(p * 48) * 6 * (1 - p), 0);
        dc.DrawRoundedRectangle(null, new Pen(ink, 3), shock, 7, 7);
        dc.DrawEllipse(null, new Pen(ink, 2 * (1 - p) + .5), center,
            radius * (.3 + .85 * p), radius * (.3 + .85 * p));
        Sparks(dc, center, radius, p, ink, recovery ? 10 : 18);
        if (recovery)
        {
            for (var i = 0; i < 5; i++)
            {
                var point = center + new Vector((i - 2) * 27 + Math.Sin(i * 2 + p * 5) * 8, radius * .5 - p * radius * 1.4);
                dc.DrawLine(new Pen(Green, 2), point + new Vector(-5, 0), point + new Vector(5, 0));
                dc.DrawLine(new Pen(Green, 2), point + new Vector(0, -5), point + new Vector(0, 5));
            }
        }
        else if (nature == DamageNature.Fire) Flames(dc, center, radius, p);
        else if (nature == DamageNature.Thunder) Lightning(dc, center, radius, p);
        else
        {
            dc.DrawLine(new Pen(White, 3), center + new Vector(-radius * .6, -radius * .35), center + new Vector(radius * .6, radius * .35));
            dc.DrawLine(new Pen(White, 3), center + new Vector(-radius * .5, radius * .4), center + new Vector(radius * .5, -radius * .4));
        }
        dc.Pop();
    }

    private static void Skill(DrawingContext dc, Rect bounds, string name, double p, double dpi)
    {
        var center = Center(bounds);
        var radius = Math.Clamp(Math.Max(bounds.Width, bounds.Height) * .7, 70, 120);
        dc.PushOpacity(Pulse(p, .02, .98));
        dc.DrawEllipse(GoldGlow, null, center, radius * 1.45, radius * 1.45);
        dc.DrawEllipse(null, new Pen(Gold, 2.5), center, radius * .84, radius * .84);
        for (var i = 0; i < 12; i++)
        {
            var angle = i * Math.PI / 6 + p * .7;
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            dc.DrawLine(new Pen(Gold, i % 3 == 0 ? 3 : 1), center + direction * radius * .9,
                center + direction * radius * (i % 3 == 0 ? 1.14 : .98));
        }
        Sparks(dc, center, radius, p, Gold, 18);
        var text = new FormattedText(name, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            Calligraphy, name.Length > 6 ? 26 : 34, White, dpi) { MaxTextWidth = 220, Trimming = TextTrimming.CharacterEllipsis };
        var width = Math.Max(150, text.Width + 54);
        var banner = new Rect(center.X - width / 2, center.Y + bounds.Height * .12, width, 62);
        dc.DrawRectangle(Banner, null, banner);
        dc.DrawLine(new Pen(Gold, 1.5), new Point(banner.Left + 25, banner.Top), new Point(banner.Right - 25, banner.Top));
        dc.DrawLine(new Pen(Gold, 1.5), new Point(banner.Left + 25, banner.Bottom), new Point(banner.Right - 25, banner.Bottom));
        dc.DrawText(text, new Point(center.X - text.Width / 2, banner.Top + (banner.Height - text.Height) / 2));
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

    private static void Lightning(DrawingContext dc, Point center, double radius, double p)
    {
        for (var branch = 0; branch < 3; branch++)
        {
            var line = new StreamGeometry();
            using (var path = line.Open())
            {
                var top = center + new Vector((branch - 1) * radius * .55, -radius * 1.3);
                path.BeginFigure(top, false, false);
                for (var i = 1; i <= 6; i++)
                    path.LineTo(top + new Vector(Math.Sin(i * 2.9 + branch + Math.Floor(p * 8)) * radius * .25,
                        i * radius * .33), true, false);
            }
            line.Freeze();
            dc.DrawGeometry(null, new Pen(Violet, 7), line);
            dc.DrawGeometry(null, new Pen(White, 2), line);
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
    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    private static Brush Solid(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
    private static Brush Glow(Color color) => Freeze(new RadialGradientBrush(new GradientStopCollection
    { new(color, 0), new(Color.FromArgb((byte)(color.A / 2), color.R, color.G, color.B), .45), new(Colors.Transparent, 1) }));
    private static T Freeze<T>(T value) where T : Freezable { value.Freeze(); return value; }
}
