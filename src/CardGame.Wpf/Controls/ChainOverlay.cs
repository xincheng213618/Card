using System.Windows;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

/// <summary>A persistent chain across the general's portrait, independent of target and status rails.</summary>
public sealed class ChainOverlay : FrameworkElement
{
    private static readonly Pen Shadow = FrozenPen(Brushes.Black, 6);
    private static readonly Pen Steel = FrozenPen(new LinearGradientBrush(
        Color.FromRgb(220, 229, 229), Color.FromRgb(55, 66, 70), 90), 3.8);
    private static readonly Pen Shine = FrozenPen(Brushes.LightSlateGray, 0.8);

    public ChainOverlay() { IsHitTestVisible = false; Focusable = false; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        for (var x = -5.0; x < ActualWidth + 10; x += 15)
        {
            var edge = ((int)((x + 5) / 15) & 1) != 0;
            var link = new Rect(x, edge ? 8 : 5, 23, edge ? 5 : 11);
            dc.DrawRoundedRectangle(null, Shadow, link, 5, 5);
            dc.DrawRoundedRectangle(null, Steel, link, 5, 5);
            dc.DrawRoundedRectangle(null, Shine, link, 5, 5);
        }
    }

    private static Pen FrozenPen(Brush brush, double width)
    {
        var pen = new Pen(brush, width); pen.Freeze(); return pen;
    }
}
