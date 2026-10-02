using System.Windows;
using System.Windows.Controls;

namespace CardGame.Wpf.Controls;

/// <summary>Keep the hand contiguous from the equipment rail; overlap only when space runs out.</summary>
public sealed class HandPanel : Panel
{
    private const double CardWidth = 112;
    private const double CardHeight = 178;
    private const double MinimumStep = 36;

    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(HandPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(CardWidth, CardHeight));
        var minimum = InternalChildren.Count == 0 ? 0 : CardWidth + (InternalChildren.Count - 1) * MinimumStep;
        var width = ViewportWidth > 0 ? ViewportWidth : double.IsInfinity(availableSize.Width) ? minimum : availableSize.Width;
        return new Size(Math.Max(minimum, width), CardHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return finalSize;
        var step = count == 1 ? 0 : Math.Clamp((finalSize.Width - CardWidth) / (count - 1), MinimumStep, CardWidth);
        var offset = 0.0;
        for (var index = 0; index < count; index++)
            InternalChildren[index].Arrange(new Rect(offset + index * step, 0, CardWidth, CardHeight));
        return finalSize;
    }
}
