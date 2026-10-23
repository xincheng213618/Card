using System.Windows;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

/// <summary>Public portrait-to-portrait clearance for declared Slash/Duel blades, never a damage effect.</summary>
internal sealed class SlashFlightPath
{
    private const double HalfSize = 12.75;
    private readonly Rect _source, _target, _viewport;
    private readonly Rect[] _obstacles;
    private readonly DrawCardFlightPath? _route;
    public Geometry Clip { get; }
    public bool IsCompact => _route is not null;
    public double Length { get; }

    public SlashFlightPath(Rect source, Rect target, Rect[] obstacles, Rect viewport)
    {
        _source = source; _target = target; _obstacles = obstacles; _viewport = viewport;
        Geometry clip = new RectangleGeometry(viewport);
        foreach (var obstacle in obstacles)
            clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(obstacle));
        clip.Freeze(); Clip = clip;

        var direction = Center(target) - Center(source);
        if (direction.Length <= 1) return;
        direction.Normalize();
        double Edge(Rect bounds) => Math.Min(bounds.Width / (2 * Math.Max(.001, Math.Abs(direction.X))),
            bounds.Height / (2 * Math.Max(.001, Math.Abs(direction.Y)))) + 12;
        var start = Center(source) + direction * Edge(source);
        var finish = Center(target) - direction * Edge(target);
        // The original full-size sweep, glow and flame fit in this corridor.
        // Leave that visual unchanged when it clears the actual UI ink.
        if (Vector.Multiply(finish - start, direction) > 0 && Clear(start, finish, 66, obstacles, viewport)) return;

        var barriers = obstacles.Append(source).Append(target).ToArray();
        var sources = Ports(source).Where(point => Clear(point, point, HalfSize, barriers, viewport)).ToArray();
        var targets = Ports(target).Where(point => Clear(point, point, HalfSize, barriers, viewport)).ToArray();
        // Ports are prefiltered before route solving. Bound the work per public
        // target and retain the first clear route, rather than optimize every pair.
        foreach (var pair in (from launch in sources from destination in targets
                              orderby (destination - launch).LengthSquared select (From: launch, To: destination)).Take(16))
        {
            var route = DrawCardFlightPath.Create(pair.From, pair.To, new Size(24, 24), barriers, viewport);
            if (route.NeedsClip) continue;
            _route = route;
            var previous = route.Position(0);
            for (var index = 1; index <= 96; index++)
            {
                var point = route.Position(index / 96d);
                Length += (point - previous).Length;
                previous = point;
            }
            break;
        }
    }

    public bool Matches(Rect source, Rect target, Rect[] obstacles, Rect viewport) =>
        source == _source && target == _target && viewport == _viewport && _obstacles.AsSpan().SequenceEqual(obstacles);

    public Point Position(double move) => _route!.Position(move);

    public Vector Direction(double move)
    {
        var direction = Position(Math.Min(1, move + .001)) - Position(Math.Max(0, move - .001));
        if (direction.Length <= .000001) return new Vector(1, 0);
        direction.Normalize(); return direction;
    }

    private static IEnumerable<Point> Ports(Rect bounds)
    {
        const double gap = HalfSize + 3;
        foreach (var fraction in new[] { .5, .25, .75 })
        {
            yield return new(bounds.Left - gap, bounds.Top + bounds.Height * fraction);
            yield return new(bounds.Right + gap, bounds.Top + bounds.Height * fraction);
            yield return new(bounds.Left + bounds.Width * fraction, bounds.Top - gap);
            yield return new(bounds.Left + bounds.Width * fraction, bounds.Bottom + gap);
        }
    }

    private static bool Clear(Point start, Point finish, double halfSize, IReadOnlyList<Rect> obstacles, Rect viewport)
    {
        viewport.Inflate(-halfSize, -halfSize);
        if (!viewport.Contains(start) || !viewport.Contains(finish)) return false;
        foreach (var obstacle in obstacles)
        {
            var bounds = obstacle; bounds.Inflate(halfSize, halfSize);
            var low = 0d; var high = 1d;
            if (Slab(start.X, finish.X - start.X, bounds.Left, bounds.Right) &&
                Slab(start.Y, finish.Y - start.Y, bounds.Top, bounds.Bottom)) return false;

            bool Slab(double origin, double distance, double minimum, double maximum)
            {
                if (Math.Abs(distance) < .000001) return origin >= minimum && origin <= maximum;
                var first = (minimum - origin) / distance; var last = (maximum - origin) / distance;
                if (first > last) (first, last) = (last, first);
                low = Math.Max(low, first); high = Math.Min(high, last);
                return low <= high;
            }
        }
        return true;
    }

    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
}
