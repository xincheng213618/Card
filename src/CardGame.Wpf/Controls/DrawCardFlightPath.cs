using System.Windows;

namespace CardGame.Wpf.Controls;

/// <summary>A public card-back route with a conservative, constant-size clearance envelope.</summary>
internal sealed class DrawCardFlightPath
{
    private readonly Segment[] _segments;
    private readonly double _length;
    private readonly Vector _halfSize;
    public bool NeedsClip { get; }

    private DrawCardFlightPath(Segment[] segments, Vector halfSize, bool needsClip)
    {
        _segments = segments;
        _length = segments.Sum(segment => segment.Length);
        _halfSize = halfSize;
        NeedsClip = needsClip;
    }

    public static DrawCardFlightPath Create(Point launch, Point destination, Size maximumSize,
        IReadOnlyList<Rect> obstacles, Rect viewport)
    {
        // Include the actual 1.5px outline. The body only shrinks during flight,
        // so this envelope also protects every intermediate pose and corner.
        var half = new Vector(maximumSize.Width / 2 + .75, maximumSize.Height / 2 + .75);
        var area = viewport;
        area.Inflate(-half.X, -half.Y);
        var barriers = obstacles.Where(rect => !rect.IsEmpty).Select(rect =>
        {
            rect.Inflate(half.X, half.Y);
            return rect;
        }).ToArray();
        var direct = new[] { launch, destination };
        Point[]? best = null;
        var bestLength = double.PositiveInfinity;
        Consider(direct);
        if (best is null)
        {
            // Bounded candidates follow the measured obstacle edges. A side
            // corridor clears both the seat ring and the fixed command dock.
            foreach (var obstacle in barriers)
            {
                foreach (var x in new[] { obstacle.Left - 6, obstacle.Right + 6 })
                    Consider([launch, new(x, launch.Y), new(x, destination.Y), destination]);
                foreach (var y in new[] { obstacle.Top - 6, obstacle.Bottom + 6 })
                    Consider([launch, new(launch.X, y), new(destination.X, y), destination]);
            }
        }
        var points = best ?? direct;
        var rounded = Build(points, round: true);
        // The control-point hull encloses the complete quadratic corner.
        // If rounding consumes a narrow clearance, retain its safe straight edges.
        if (best is not null && !rounded.All(segment => CurveClear(segment, area, barriers)))
            rounded = Build(points, round: false);
        return new(rounded, half, best is null);

        void Consider(Point[] points)
        {
            double length = 0;
            for (var i = 1; i < points.Length; i++)
            {
                length += (points[i] - points[i - 1]).Length;
                if (length >= bestLength || !area.Contains(points[i - 1]) || !area.Contains(points[i]) ||
                    barriers.Any(rect => Intersects(points[i - 1], points[i], rect))) return;
            }
            best = points; bestLength = length;
        }
    }

    public Point Position(double move)
    {
        var distance = Math.Clamp(move, 0, 1) * _length;
        for (var i = 0; i < _segments.Length; i++)
        {
            var segment = _segments[i];
            if (distance <= segment.Length || i == _segments.Length - 1)
                return segment.AtDistance(distance);
            distance -= segment.Length;
        }
        return _segments[^1].End;
    }

    public IEnumerable<Rect> Corridor()
    {
        foreach (var segment in _segments)
        {
            var divisions = Math.Max(1, (int)Math.Ceiling(segment.Length / 48));
            for (var part = 0; part < divisions; part++)
            {
                var bounds = segment.Bounds((double)part / divisions, (double)(part + 1) / divisions);
                bounds.Inflate(_halfSize.X, _halfSize.Y);
                yield return bounds;
            }
        }
    }

    private static Segment[] Build(Point[] input, bool round)
    {
        var points = new List<Point>();
        foreach (var point in input)
            if (points.Count == 0 || (points[^1] - point).Length > .001) points.Add(point);
        var result = new List<Segment>();
        var current = points[0];
        for (var i = 1; i < points.Count - 1; i++)
        {
            var incoming = points[i] - points[i - 1];
            var outgoing = points[i + 1] - points[i];
            var radius = round ? Math.Min(8, Math.Min(incoming.Length, outgoing.Length) * .3) : 0;
            incoming.Normalize(); outgoing.Normalize();
            var entry = points[i] - incoming * radius;
            var exit = points[i] + outgoing * radius;
            Line(current, entry);
            if (radius > 0) result.Add(new(entry, points[i], exit));
            current = exit;
        }
        Line(current, points[^1]);
        if (result.Count == 0) result.Add(new(current, current, current));
        return result.ToArray();

        void Line(Point start, Point end)
        {
            if ((end - start).Length > .001) result.Add(new(start, start + (end - start) * .5, end));
        }
    }

    private static bool CurveClear(Segment segment, Rect area, Rect[] barriers)
    {
        for (var i = 0; i < 16; i++)
        {
            var hull = segment.Bounds(i / 16d, (i + 1) / 16d);
            if (!area.Contains(hull) || barriers.Any(rect => rect.IntersectsWith(hull))) return false;
        }
        return true;
    }

    private static bool Intersects(Point start, Point end, Rect rectangle)
    {
        var from = 0d; var to = 1d;
        return Slab(start.X, end.X - start.X, rectangle.Left, rectangle.Right) &&
            Slab(start.Y, end.Y - start.Y, rectangle.Top, rectangle.Bottom);

        bool Slab(double origin, double direction, double low, double high)
        {
            if (Math.Abs(direction) < .000001) return origin >= low && origin <= high;
            var a = (low - origin) / direction; var b = (high - origin) / direction;
            if (a > b) (a, b) = (b, a);
            from = Math.Max(from, a); to = Math.Min(to, b);
            return from <= to;
        }
    }

    private sealed class Segment
    {
        private readonly Point _start, _control;
        private readonly double[] _distances = new double[17];
        public Point End { get; }
        public double Length => _distances[^1];
        public Segment(Point start, Point control, Point end)
        {
            _start = start; _control = control; End = end;
            var previous = start;
            for (var i = 1; i < _distances.Length; i++)
            {
                var point = At(i / 16d);
                _distances[i] = _distances[i - 1] + (point - previous).Length;
                previous = point;
            }
        }
        private Point At(double t) => _start + (_control - _start) * (2 * t * (1 - t)) + (End - _start) * (t * t);
        public Point AtDistance(double distance)
        {
            distance = Math.Clamp(distance, 0, Length);
            for (var i = 1; i < _distances.Length; i++)
                if (distance <= _distances[i])
                {
                    var span = _distances[i] - _distances[i - 1];
                    return At((i - 1 + (span < .000001 ? 0 : (distance - _distances[i - 1]) / span)) / 16);
                }
            return End;
        }
        public Rect Bounds(double first, double last)
        {
            var start = At(first); var end = At(last);
            var control = start + ((_control - _start) * (1 - first) + (End - _control) * first) * (last - first);
            var bounds = new Rect(start, end); bounds.Union(control); return bounds;
        }
    }
}
