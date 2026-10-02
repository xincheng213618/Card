using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

/// <summary>Keep the hand contiguous from the equipment rail; overlap only when space runs out.</summary>
public sealed class HandPanel : Panel
{
    private const double CardWidth = 112;
    private const double CardHeight = 178;
    private const double MinimumStep = 36;
    private double _step;
    private UIElement? _hoveredCard;

    public static readonly DependencyProperty IsPointerCardProperty = DependencyProperty.RegisterAttached(
        "IsPointerCard", typeof(bool), typeof(HandPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsPointerCard(DependencyObject element) => (bool)element.GetValue(IsPointerCardProperty);
    public static void SetIsPointerCard(DependencyObject element, bool value) => element.SetValue(IsPointerCardProperty, value);

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
        if (count == 0) { _step = 0; SetHoveredCard(null); return finalSize; }
        _step = count == 1 ? CardWidth : Math.Clamp((finalSize.Width - CardWidth) / (count - 1), MinimumStep, CardWidth);
        for (var index = 0; index < count; index++)
            InternalChildren[index].Arrange(new Rect(index * _step, 0, CardWidth, CardHeight));
        if (_hoveredCard is not null && !InternalChildren.Contains(_hoveredCard)) SetHoveredCard(null);
        return finalSize;
    }

    // Lifted artwork can cover its neighbor. Resolve input from the original
    // card slots so the raised visual never captures the neighboring strip.
    private UIElement? CardContainerAt(Point point)
    {
        var count = InternalChildren.Count;
        if (count == 0 || _step <= 0 || point.X < 0 || point.Y < 0 || point.Y > CardHeight ||
            point.X >= (count - 1) * _step + CardWidth) return null;
        return InternalChildren[Math.Min(count - 1, (int)(point.X / _step))];
    }

    internal Button? CardButtonAt(Point point) => FindButton(CardContainerAt(point));

    internal void UpdatePointer(Point point) => SetHoveredCard(CardContainerAt(point));

    private void SetHoveredCard(UIElement? card)
    {
        if (ReferenceEquals(card, _hoveredCard)) return;
        if (_hoveredCard is not null) SetIsPointerCard(_hoveredCard, false);
        _hoveredCard = card;
        if (_hoveredCard is not null) SetIsPointerCard(_hoveredCard, true);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        UpdatePointer(e.GetPosition(this));
        base.OnPreviewMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        SetHoveredCard(null);
        base.OnMouseLeave(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!e.Handled && PressCardAt(e.GetPosition(this))) e.Handled = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    internal bool PressCardAt(Point point)
    {
        UpdatePointer(point);
        var button = CardButtonAt(point);
        if (button is null) return false;
        if (button.IsEnabled && button.Command is { } command)
        {
            button.Focus();
            if (command is RoutedCommand routed)
            {
                var target = button.CommandTarget ?? button;
                if (routed.CanExecute(button.CommandParameter, target)) routed.Execute(button.CommandParameter, target);
            }
            else if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
        }
        return true;
    }

    private static Button? FindButton(DependencyObject? element)
    {
        if (element is null) return null;
        if (element is Button button) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindButton(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }
}
