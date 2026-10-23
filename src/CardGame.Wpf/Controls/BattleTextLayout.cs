using System.Windows;

namespace CardGame.Wpf.Controls;

/// <summary>Places transient text near its public owner without covering other text or decisions.</summary>
internal static class BattleTextLayout
{
    public static bool Fits(Rect candidate, Rect viewport, IReadOnlyList<Rect> occupied)
    {
        if (!viewport.Contains(candidate)) return false;
        for (var index = 0; index < occupied.Count; index++)
            if (occupied[index].IntersectsWith(candidate)) return false;
        return true;
    }

    public static bool TryPlace(Size size, Point preferred, Rect viewport, IReadOnlyList<Rect> occupied, out Rect result)
    {
        result = Rect.Empty;
        if (size.Width > viewport.Width || size.Height > viewport.Height) return false;
        var x = Math.Clamp(preferred.X - size.Width / 2, viewport.Left, viewport.Right - size.Width);
        var y = Math.Clamp(preferred.Y - size.Height / 2, viewport.Top, viewport.Bottom - size.Height);
        var desired = new Rect(new Point(x, y), size);
        if (Fits(desired, viewport, occupied)) { result = desired; return true; }

        // Obstacle edges provide close placements; a coarse grid also covers open table space.
        var xs = new List<double>(occupied.Count * 2 + 3) { x, viewport.Left, viewport.Right - size.Width };
        var ys = new List<double>(occupied.Count * 2 + 3) { y, viewport.Top, viewport.Bottom - size.Height };
        foreach (var area in occupied)
        {
            xs.Add(area.Left - size.Width - 6); xs.Add(area.Right + 6);
            ys.Add(area.Top - size.Height - 6); ys.Add(area.Bottom + 6);
        }
        for (var column = viewport.Left; column + size.Width <= viewport.Right; column += size.Width + 6) xs.Add(column);
        for (var row = viewport.Top; row + size.Height <= viewport.Bottom; row += size.Height + 6) ys.Add(row);
        // Build the vertical candidates once, then visit the nearest first.
        // Original order breaks equal-distance ties, matching the earlier full
        // grid search. Stop after the nearest fitting distance, retaining the
        // original order when adding the horizontal distance rounds two
        // distinct vertical distances to the same final score.
        var tops = ys.Distinct().Where(value => value >= viewport.Top && value + size.Height <= viewport.Bottom)
            .Select((value, order) => (Value: value, Distance: (value - y) * (value - y), Order: order))
            .OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Order).ToArray();
        var distance = double.PositiveInfinity;
        foreach (var left in xs.Distinct().Where(value => value >= viewport.Left && value + size.Width <= viewport.Right))
        {
            var horizontal = (left - x) * (left - x);
            if (horizontal >= distance) continue;
            var columnResult = Rect.Empty;
            var columnDistance = distance;
            var columnOrder = int.MaxValue;
            foreach (var top in tops)
            {
                var score = horizontal + top.Distance;
                if (score > columnDistance) break;
                if (score == columnDistance && (columnResult.IsEmpty || top.Order >= columnOrder)) continue;
                var candidate = new Rect(new Point(left, top.Value), size);
                if (!Fits(candidate, viewport, occupied)) continue;
                columnResult = candidate; columnDistance = score; columnOrder = top.Order;
            }
            if (!columnResult.IsEmpty) { result = columnResult; distance = columnDistance; }
        }
        return !result.IsEmpty;
    }
}
