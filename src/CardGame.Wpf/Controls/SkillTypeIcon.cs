using System.Windows;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

public sealed class SkillTypeIcon : FrameworkElement
{
    public static readonly DependencyProperty IsConversionProperty = DependencyProperty.Register(nameof(IsConversion),
        typeof(bool), typeof(SkillTypeIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsConversion { get => (bool)GetValue(IsConversionProperty); set => SetValue(IsConversionProperty, value); }
    private static readonly Geometry Conversion = Shape("M 4,9 A 7,7 0 0 1 17,7 L 14,7 20,11 20,3 18,5 A 9,9 0 0 0 2,9 Z M 18,13 A 7,7 0 0 1 5,15 L 8,15 2,11 2,19 4,17 A 9,9 0 0 0 20,13 Z");

    public SkillTypeIcon() { IsHitTestVisible = false; Focusable = false; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // Locked skills already carry their emblem in the original button artwork.
        if (!IsConversion) return;
        dc.PushTransform(new ScaleTransform(ActualWidth / 22, ActualHeight / 22));
        // Cover the stock active/locked emblem so conversion takes visual precedence.
        dc.DrawEllipse(Brushes.Black, null, new Point(11, 11), 10.5, 10.5);
        dc.DrawGeometry(Brushes.Ivory, null, Conversion);
        dc.Pop();
    }

    private static Geometry Shape(string value) { var geometry = Geometry.Parse(value); geometry.Freeze(); return geometry; }
}
