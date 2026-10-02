using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CardGame.Wpf.Controls;

/// <summary>Browse an overflowing hand and keep newly selected cards in view.</summary>
public sealed class HandScrollViewer : ScrollViewer
{
    public static readonly DependencyProperty RevealSelectionProperty = DependencyProperty.RegisterAttached(
        "RevealSelection", typeof(bool), typeof(HandScrollViewer),
        new PropertyMetadata(false, SelectionChanged));

    public static bool GetRevealSelection(DependencyObject element) => (bool)element.GetValue(RevealSelectionProperty);
    public static void SetRevealSelection(DependencyObject element, bool value) => element.SetValue(RevealSelectionProperty, value);

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (ScrollableWidth > 0 && e.Delta != 0 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ScrollToHorizontalOffset(HorizontalOffset - e.Delta * 108.0 / 120);
            e.Handled = true;
        }
        base.OnPreviewMouseWheel(e);
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        if (IsMouseOver && (e.HorizontalChange != 0 || e.ViewportWidthChange != 0) && FindHand(this) is { } hand)
            hand.UpdatePointer(Mouse.GetPosition(hand));
    }

    private static HandPanel? FindHand(DependencyObject element)
    {
        if (element is HandPanel hand) return hand;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindHand(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }

    private static void SelectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement card || e.NewValue is not true) return;
        // Binding may select a new container before it has a parent or an arranged size.
        card.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!GetRevealSelection(card)) return;
            DependencyObject? parent = card;
            while (parent is not null && parent is not HandScrollViewer)
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is not HandScrollViewer viewport || viewport.ScrollableWidth <= 0) return;
            var bounds = card.TransformToAncestor(viewport).TransformBounds(new Rect(card.RenderSize));
            if (bounds.Left < 0)
                viewport.ScrollToHorizontalOffset(viewport.HorizontalOffset + bounds.Left);
            else if (bounds.Right > viewport.ViewportWidth)
                viewport.ScrollToHorizontalOffset(viewport.HorizontalOffset + bounds.Right - viewport.ViewportWidth);
        }));
    }
}
