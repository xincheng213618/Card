using System.Windows;

namespace CardGame.Wpf.Controls;

/// <summary>Places transient text near its public owner without covering other text or decisions.</summary>
internal static class BattleTextLayout
{
    public static bool Fits(Rect candidate, Rect viewport, IReadOnlyList<Rect> occupied) =>
        viewport.Contains(candidate) && occupied.All(area => !area.IntersectsWith(candidate));

    public static bool TryPlace(Size size, Point preferred, Rect viewport, IReadOnlyList<Rect> occupied, out Rect result)
    {
        result = Rect.Empty;
        if (size.Width > viewport.Width || size.Height > viewport.Height) return false;
        var x = Math.Clamp(preferred.X - size.Width / 2, viewport.Left, viewport.Right - size.Width);
        var y = Math.Clamp(preferred.Y - size.Height / 2, viewport.Top, viewport.Bottom - size.Height);
        var desired = new Rect(new Point(x, y), size);
        if (Fits(desired, viewport, occupied)) { result = desired; return true; }

        // Obstacle edges provide close placements; a coarse grid also covers open table space.
        var xs = new List<double> { x, viewport.Left, viewport.Right - size.Width };
        var ys = new List<double> { y, viewport.Top, viewport.Bottom - size.Height };
        foreach (var area in occupied)
        {
            xs.Add(area.Left - size.Width - 6); xs.Add(area.Right + 6);
            ys.Add(area.Top - size.Height - 6); ys.Add(area.Bottom + 6);
        }
        for (var column = viewport.Left; column + size.Width <= viewport.Right; column += size.Width + 6) xs.Add(column);
        for (var row = viewport.Top; row + size.Height <= viewport.Bottom; row += size.Height + 6) ys.Add(row);
        var distance = double.PositiveInfinity;
        foreach (var left in xs.Distinct().Where(value => value >= viewport.Left && value + size.Width <= viewport.Right))
            foreach (var top in ys.Distinct().Where(value => value >= viewport.Top && value + size.Height <= viewport.Bottom))
            {
                var candidate = new Rect(new Point(left, top), size);
                var score = (left - x) * (left - x) + (top - y) * (top - y);
                if (score >= distance || !Fits(candidate, viewport, occupied)) continue;
                result = candidate; distance = score;
            }
        return !result.IsEmpty;
    }
}
