using System.Windows;

namespace CardGame.Wpf.Controls;

internal enum PublicCardFlightStyle { Played, Transfer, Discard }

/// <summary>A bounded, cue-owned route for an already public local hand face.</summary>
internal sealed class PublicCardFlightPath
{
    private const int ValidationSteps = 128;
    private const int MaximumCornerCandidates = 192;
    private readonly Point _launch, _destination;
    private readonly Size _initialSize, _finalSize;
    private readonly Segment[]? _segments;
    private readonly double _length;
    private readonly double _extraLift;
    private readonly PublicCardFlightStyle _style;
    private readonly double _shrink, _shrinkPeak;

    private PublicCardFlightPath(Point launch, Point destination, Size initialSize, Size finalSize,
        Segment[]? segments = null, double extraLift = 0,
        PublicCardFlightStyle style = PublicCardFlightStyle.Played, double shrink = .42, double shrinkPeak = .5)
    {
        _launch = launch; _destination = destination;
        _initialSize = initialSize; _finalSize = finalSize; _segments = segments;
        _extraLift = extraLift;
        _style = style; _shrink = shrink; _shrinkPeak = shrinkPeak;
        if (segments is not null)
            foreach (var segment in segments) _length += segment.Length;
    }

    public bool Detoured => _segments is not null || _extraLift != 0;
    public bool NeedsClip { get; private set; }
    public static double Scale(double move) => 1 - .42 * Math.Sin(Math.Clamp(move, 0, 1) * Math.PI);
    public double ScaleAt(double move) => 1 - _shrink * Math.Sin(ShrinkPhase(Math.Clamp(move, 0, 1)) * Math.PI);
    public Point PositionAt(double flight) => Position(flight, Ease(Math.Clamp(flight, 0, 1)));

    public static PublicCardFlightPath Create(Point launch, Point destination, Size initialSize, Size finalSize,
        IReadOnlyList<Rect> obstacles, Rect viewport, PublicCardFlightStyle style = PublicCardFlightStyle.Played)
    {
        var original = new PublicCardFlightPath(launch, destination, initialSize, finalSize, style: style);
        if (original.IsClear(obstacles, viewport)) return original;
        original.NeedsClip = true;
        // No route can remove an overlap at an immutable endpoint.
        if (!Clear(original.SweptBounds(0, 0), obstacles, viewport) ||
            !Clear(original.SweptBounds(1, 1), obstacles, viewport)) return original;
        // Concentrate extra lift in mid-flight so the face still settles onto
        // the public pile before its unchanged handoff time.
        var lifted = new PublicCardFlightPath(launch, destination, initialSize, finalSize, extraLift: 24, style: style);
        if (lifted.IsClear(obstacles, viewport)) return lifted;

        var blocked = Rect.Empty;
        var crossing = Rect.Empty;
        for (var i = 0; i < ValidationSteps; i++)
        {
            var swept = original.SweptBounds((double)i / ValidationSteps, (double)(i + 1) / ValidationSteps);
            foreach (var obstacle in obstacles)
                if (!obstacle.IsEmpty && obstacle.IntersectsWith(swept))
                {
                    blocked.Union(obstacle);
                    // A HUD above the landing can intersect the original arc,
                    // but it is not the barrier separating hand and landing.
                    if (obstacle.Top > Math.Min(launch.Y, destination.Y) &&
                        obstacle.Bottom < Math.Max(launch.Y, destination.Y)) crossing.Union(obstacle);
                }
        }
        var envelope = original.Envelope(0, 1);
        if (!crossing.IsEmpty) blocked = crossing;
        var left = blocked.IsEmpty ? Math.Min(launch.X, destination.X) : blocked.Left - envelope.Right - 6;
        var right = blocked.IsEmpty ? Math.Max(launch.X, destination.X) : blocked.Right - envelope.Left + 6;
        PublicCardFlightPath? best = null;
        if (!crossing.IsEmpty)
        {
            // Stay farther outside while the hand face is wide, then incline
            // inward as it shrinks. Turn toward the landing just beyond the
            // intervening controls instead of rising beside another portrait.
            var inward = Math.Min(initialSize.Width, finalSize.Width) * .5 + 1;
            var clearance = Math.Min(initialSize.Height, finalSize.Height) * .4;
            for (var side = 0; side < 2; side++)
            for (var entry = 0; entry < 3; entry++)
            for (var turn = 0; turn < 4; turn++)
            {
                var edge = side == 0 ? left : right;
                var direction = side == 0 ? 1 : -1;
                var lane = edge + direction * (inward - 28 + entry * 4);
                var exitLane = edge + direction * inward;
                var padding = clearance + (turn - 2) * 4;
                var exitY = launch.Y > destination.Y
                    ? Math.Max(destination.Y, crossing.Top - padding)
                    : Math.Min(destination.Y, crossing.Bottom + padding);
                Consider(lane, 0, exitLane, exitY);
            }
            return best ?? original.FindCornerRoute(obstacles, viewport) ?? original;
        }
        // At most 24 candidates. Inner lanes use the space opened by the face's
        // mid-flight shrink; a short retreat leaves room to turn below a button.
        for (var side = 0; side < 2; side++)
        for (var insetIndex = 0; insetIndex < 4; insetIndex++)
        for (var retreat = 0; retreat < 3; retreat++)
        {
            var inset = insetIndex switch { 1 => 24, 2 => 40, 3 => 56, _ => 0 };
            var lane = side == 0 ? left + inset : right - inset;
            var away = Math.Sign(launch.Y - destination.Y) * retreat * 16;
            Consider(lane, away, lane, destination.Y);
        }
        return best ?? original.FindCornerRoute(obstacles, viewport) ?? original;

        void Consider(double lane, double retreat, double exitLane, double exitY)
        {
            if (lane + envelope.Left < viewport.Left || lane + envelope.Right > viewport.Right) return;
            var segments = BuildRoute(launch, destination, lane, retreat, exitLane, exitY);
            var candidate = new PublicCardFlightPath(launch, destination, initialSize, finalSize, segments, style: style);
            if ((best is null || candidate._length < best._length) && candidate.IsClear(obstacles, viewport)) best = candidate;
        }
    }

    private PublicCardFlightPath? FindCornerRoute(IReadOnlyList<Rect> obstacles, Rect viewport)
    {
        var upward = _launch.Y > _destination.Y;
        var barriers = obstacles.Where(rect => !rect.IsEmpty &&
            rect.Top > Math.Min(_launch.Y, _destination.Y) &&
            rect.Bottom < Math.Max(_launch.Y, _destination.Y)).ToArray();
        if (barriers.Length == 0) return null;
        var union = Rect.Empty;
        foreach (var barrier in barriers) union.Union(barrier);
        var farthest = upward ? barriers.MinBy(rect => rect.Top) : barriers.MaxBy(rect => rect.Bottom);
        var left = barriers.MinBy(rect => rect.Left);
        var right = barriers.MaxBy(rect => rect.Right);
        var corners = new[] { new[] { left, farthest, union }.Distinct().ToArray(),
            new[] { right, farthest, union }.Distinct().ToArray() };
        var attempted = 0;
        // First try new corner routes at the unchanged size. Only a blocked
        // narrow corridor may select a deeper, route-owned temporary shrink.
        ReadOnlySpan<(double Shrink, double Peak)> profiles = [(.42, .5), (.60, .75), (.60, .5), (.60, .25)];
        foreach (var (shrink, peak) in profiles)
        {
            var probe = new PublicCardFlightPath(_launch, _destination, _initialSize, _finalSize,
                style: _style, shrink: shrink, shrinkPeak: peak);
            var compact = probe.Envelope(probe.InverseEase(peak), probe.InverseEase(peak));
            // All candidates in one profile share these interval envelopes.
            // Cache the expensive rotated extrema once, rather than per route.
            var envelopes = new Rect[ValidationSteps];
            for (var i = 0; i < envelopes.Length; i++)
                envelopes[i] = probe.Envelope((double)i / ValidationSteps, (double)(i + 1) / ValidationSteps);
            for (var side = 0; side < corners.Length; side++)
            foreach (var corner in corners[side])
            foreach (var xPadding in new[] { 6, 18 })
            foreach (var yPadding in new[] { 0, 12 })
            foreach (var inward in new[] { 0, 24 })
            {
                if (++attempted > MaximumCornerCandidates) return null;
                var direction = side == 0 ? -1 : 1;
                var exitX = side == 0 ? corner.Left - compact.Right - xPadding : corner.Right - compact.Left + xPadding;
                var exitY = upward ? corner.Top - compact.Bottom - yPadding : corner.Bottom - compact.Top + yPadding;
                var segments = BuildRoute(_launch, _destination, exitX - direction * inward, 0, exitX, exitY);
                var candidate = new PublicCardFlightPath(_launch, _destination, _initialSize, _finalSize, segments,
                    style: _style, shrink: shrink, shrinkPeak: peak);
                if (candidate.IsClear(obstacles, viewport, envelopes)) return candidate;
            }
        }
        return null;
    }

    public Point Position(double flight, double move)
    {
        flight = Math.Clamp(flight, 0, 1); move = Math.Clamp(move, 0, 1);
        if (flight <= 0) return _launch;
        if (flight >= 1) return _destination;
        if (_segments is null)
        {
            var point = _launch + (_destination - _launch) * move;
            point.Y -= Lift(Math.Sin(flight * Math.PI));
            return point;
        }
        Locate(move, out var segment, out var t);
        return _segments[segment].Position(t);
    }

    private static Segment[] BuildRoute(Point launch, Point destination, double lane, double retreat, double exitLane, double exitY)
    {
        Span<Point> points = stackalloc Point[5];
        var count = 0;
        points[count++] = launch;
        if (retreat != 0) points[count++] = new(launch.X, launch.Y + retreat);
        var corner = new Point(lane, launch.Y + retreat);
        if ((corner - points[count - 1]).Length > .001) points[count++] = corner;
        corner = new(exitLane, exitY);
        if ((corner - points[count - 1]).Length > .001) points[count++] = corner;
        if ((destination - points[count - 1]).Length > .001) points[count++] = destination;
        var result = new List<Segment>(7);
        var current = launch;
        for (var i = 1; i < count - 1; i++)
        {
            var incoming = points[i] - points[i - 1]; var outgoing = points[i + 1] - points[i];
            var radius = Math.Min(24, Math.Min(incoming.Length, outgoing.Length) * .45);
            incoming.Normalize(); outgoing.Normalize();
            var entry = points[i] - incoming * radius; var exit = points[i] + outgoing * radius;
            AddLine(current, entry);
            result.Add(new(entry, points[i], exit, true));
            current = exit;
        }
        AddLine(current, destination);
        if (result.Count == 0) result.Add(new(launch, launch, destination, false));
        return result.ToArray();

        void AddLine(Point start, Point end)
        {
            if ((end - start).Length > .001) result.Add(new(start, start + (end - start) * .5, end, false));
        }
    }

    private bool IsClear(IReadOnlyList<Rect> obstacles, Rect viewport, Rect[]? envelopes = null)
    {
        if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0) return false;
        for (var i = 0; i < ValidationSteps; i++)
        {
            var first = (double)i / ValidationSteps; var last = (double)(i + 1) / ValidationSteps;
            var bounds = envelopes is null ? SweptBounds(first, last) : SweptBoundsWithEnvelope(first, last, envelopes[i]);
            if (!Clear(bounds, obstacles, viewport)) return false;
        }
        return true;
    }

    private static bool Clear(Rect bounds, IReadOnlyList<Rect> obstacles, Rect viewport)
    {
        if (!viewport.Contains(bounds)) return false;
        foreach (var obstacle in obstacles)
            if (!obstacle.IsEmpty && obstacle.IntersectsWith(bounds)) return false;
        return true;
    }

    // Each interval encloses the complete curve and every rotated face between
    // samples, rather than checking isolated poses that could skip a thin label.
    private Rect SweptBounds(double first, double last)
        => SweptBoundsWithEnvelope(first, last, Envelope(first, last));

    private Rect SweptBoundsWithEnvelope(double first, double last, Rect envelope)
    {
        var firstMove = Ease(first); var lastMove = Ease(last);
        Rect centers;
        if (_segments is null)
        {
            var start = _launch + (_destination - _launch) * firstMove;
            var end = _launch + (_destination - _launch) * lastMove;
            var (minimum, maximum) = SinRange(first, last);
            // Lift is monotone over the nonnegative sine interval.
            centers = new(new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y) - Lift(maximum)),
                new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y) - Lift(minimum)));
        }
        else
        {
            Locate(firstMove, out var firstSegment, out var firstT);
            Locate(lastMove, out var lastSegment, out var lastT);
            centers = Rect.Empty;
            for (var i = firstSegment; i <= lastSegment; i++)
                centers.Union(_segments[i].Bounds(i == firstSegment ? firstT : 0, i == lastSegment ? lastT : 1));
        }
        return new(new Point(centers.Left + envelope.Left, centers.Top + envelope.Top),
            new Point(centers.Right + envelope.Right, centers.Bottom + envelope.Bottom));
    }

    private Rect Envelope(double first, double last)
    {
        var a = Ease(first); var b = Ease(last);
        // Scale is smallest in the middle and returns to one at both endpoints.
        // Its interval maximum comes from the smaller endpoint sine.
        var scale = 1 - _shrink * SinRange(ShrinkPhase(a), ShrinkPhase(b)).Minimum;
        var width = Math.Max(_initialSize.Width + (_finalSize.Width - _initialSize.Width) * a,
            _initialSize.Width + (_finalSize.Width - _initialSize.Width) * b);
        var height = Math.Max(_initialSize.Height + (_finalSize.Height - _initialSize.Height) * a,
            _initialSize.Height + (_finalSize.Height - _initialSize.Height) * b);
        var (minimum, maximum) = SinRange(first, last);
        var degrees = _style switch
        {
            PublicCardFlightStyle.Transfer => 8,
            PublicCardFlightStyle.Discard => -10,
            _ => Math.Sign(_destination.X - _launch.X) * 10
        };
        var angleA = degrees * minimum * Math.PI / 180; var angleB = degrees * maximum * Math.PI / 180;
        var low = Math.Min(angleA, angleB); var high = Math.Max(angleA, angleB);
        var bounds = Rect.Empty;
        // The owner footer starts inside the hand face and unfolds below it.
        // Interval maxima conservatively include that growth and the body shadow.
        var played = _style == PublicCardFlightStyle.Played;
        var bottom = height / 2 + (played ? Math.Max(5, 21 * width / 86 * b) : 0);
        var shadow = played ? 5 : 0;
        var outline = played ? 1 : .75;
        // Include the outline before rotation too: rotating the conservative
        // outer rectangle can extend farther than inflating its world bounds.
        IncludeCorner((-width / 2 - outline) * scale, (-height / 2 - outline) * scale);
        IncludeCorner((width / 2 + shadow + outline) * scale, (-height / 2 - outline) * scale);
        IncludeCorner((-width / 2 - outline) * scale, (bottom + outline) * scale);
        IncludeCorner((width / 2 + shadow + outline) * scale, (bottom + outline) * scale);
        return bounds;

        void IncludeCorner(double x, double y)
        {
            Include(low); Include(high);
            // The extrema of a rotated coordinate occur at these stationary angles.
            for (var turn = -1; turn <= 1; turn++)
            {
                var xExtreme = Math.Atan2(-y, x) + turn * Math.PI;
                var yExtreme = Math.Atan2(x, y) + turn * Math.PI;
                if (xExtreme > low && xExtreme < high) Include(xExtreme);
                if (yExtreme > low && yExtreme < high) Include(yExtreme);
            }
            void Include(double angle) => bounds.Union(new Point(x * Math.Cos(angle) - y * Math.Sin(angle),
                x * Math.Sin(angle) + y * Math.Cos(angle)));
        }
    }

    private void Locate(double move, out int index, out double t)
    {
        var distance = Math.Clamp(move, 0, 1) * _length;
        index = 0;
        while (index < _segments!.Length - 1 && distance > _segments[index].Length)
            distance -= _segments[index++].Length;
        t = _segments[index].Parameter(distance);
    }

    private double Lift(double sine) => 28 * sine + _extraLift * sine * sine;
    private double Ease(double flight) => 1 - Math.Pow(1 - flight, _style == PublicCardFlightStyle.Played ? 3 : 2);
    private double InverseEase(double move) => 1 - Math.Pow(1 - move, _style == PublicCardFlightStyle.Played ? 1.0 / 3 : .5);
    private double ShrinkPhase(double move) => move <= _shrinkPeak
        ? move / (2 * _shrinkPeak) : .5 + (move - _shrinkPeak) / (2 * (1 - _shrinkPeak));
    private static (double Minimum, double Maximum) SinRange(double first, double last)
    {
        var a = Math.Sin(first * Math.PI); var b = Math.Sin(last * Math.PI);
        return (Math.Min(a, b), first <= .5 && last >= .5 ? 1 : Math.Max(a, b));
    }

    private sealed class Segment
    {
        private const int ArcSteps = 24;
        private readonly Point _start, _control, _end;
        private readonly double[]? _arcLengths;
        public double Length { get; }

        public Segment(Point start, Point control, Point end, bool curved)
        {
            _start = start; _control = control; _end = end;
            if (!curved) { Length = (end - start).Length; return; }
            _arcLengths = new double[ArcSteps + 1];
            var previous = start;
            for (var i = 1; i <= ArcSteps; i++)
            {
                var point = Position((double)i / ArcSteps);
                _arcLengths[i] = _arcLengths[i - 1] + (point - previous).Length;
                previous = point;
            }
            Length = _arcLengths[ArcSteps];
        }

        public Point Position(double t) => _start + (_control - _start) * (2 * t * (1 - t)) + (_end - _start) * (t * t);
        public Rect Bounds(double first, double last)
        {
            var start = Position(first); var end = Position(last);
            var control = start + ((_control - _start) * (1 - first) + (_end - _control) * first) * (last - first);
            var bounds = new Rect(start, end); bounds.Union(control); return bounds;
        }

        public double Parameter(double distance)
        {
            if (Length <= .001) return 1;
            distance = Math.Clamp(distance, 0, Length);
            if (_arcLengths is null) return distance / Length;
            var low = 0; var high = ArcSteps;
            while (high - low > 1)
            {
                var middle = (low + high) / 2;
                if (_arcLengths[middle] < distance) low = middle; else high = middle;
            }
            var span = _arcLengths[high] - _arcLengths[low];
            return (low + (span <= .001 ? 0 : (distance - _arcLengths[low]) / span)) / ArcSteps;
        }
    }
}
