using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CardGame.Wpf.Controls;

// Fixed-size tiles keep ListBox items and native selection intact while realizing only nearby rows.
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(148d, FrameworkPropertyMetadataOptions.AffectsMeasure), PositiveSize);
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(204d, FrameworkPropertyMetadataOptions.AffectsMeasure), PositiveSize);
    private static readonly DependencyProperty ItemIndexProperty = DependencyProperty.RegisterAttached(
        "ItemIndex", typeof(int), typeof(VirtualizingTilePanel), new PropertyMetadata(-1));
    private static bool PositiveSize(object value) => value is double size && double.IsFinite(size) && size > 0;

    public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    private int _columns = 1;
    private double _slotWidth;
    private double _verticalOffset;
    private Size _extent;
    private Size _viewport;

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var count = owner?.Items.Count ?? 0;
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : ItemWidth;
        var height = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : ItemHeight;
        _columns = Math.Max(1, (int)(width / ItemWidth));
        _slotWidth = width / _columns;
        _viewport = new(width, height);
        _extent = new(width, Math.Ceiling((double)count / _columns) * ItemHeight);
        _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        ScrollOwner?.InvalidateScrollInfo();
        var first = Math.Max(0, (int)(_verticalOffset / ItemHeight) - 1) * _columns;
        var last = Math.Min(count - 1, ((int)Math.Ceiling((_verticalOffset + height) / ItemHeight) + 1) * _columns - 1);
        // Accessing the visual collection initializes the panel's item generator on first layout.
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        // Release old tiles before asking the recycling generator for the next viewport.
        for (var childIndex = children.Count - 1; childIndex >= 0; childIndex--)
        {
            var index = (int)children[childIndex].GetValue(ItemIndexProperty);
            if (index >= first && index <= last) continue;
            var position = new GeneratorPosition(childIndex, 0);
            if (generator is IRecyclingItemContainerGenerator recycling) recycling.Recycle(position, 1);
            else generator.Remove(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
        if (count > 0 && width > 0 && height > 0)
        {
            var position = generator.GeneratorPositionFromIndex(first);
            var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
            {
                for (var index = first; index <= last; index++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                    // Recycled containers also need to rejoin the visual tree.
                    if (newlyRealized || childIndex >= children.Count || !ReferenceEquals(children[childIndex], child))
                    {
                        if (childIndex >= children.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.SetValue(ItemIndexProperty, index);
                    child.Measure(new Size(_slotWidth, ItemHeight));
                }
            }
        }
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var index = (int)child.GetValue(ItemIndexProperty);
            child.Arrange(new Rect(index % _columns * _slotWidth,
                index / _columns * ItemHeight - _verticalOffset, _slotWidth, ItemHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        ItemContainerGenerator.RemoveAll();
        RemoveInternalChildRange(0, InternalChildren.Count);
        _verticalOffset = 0;
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= (ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0)) return;
        var top = index / _columns * ItemHeight;
        if (top < VerticalOffset) SetVerticalOffset(top);
        else if (top + ItemHeight > VerticalOffset + ViewportHeight)
            SetVerticalOffset(top + ItemHeight - ViewportHeight);
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _verticalOffset;
    public ScrollViewer ScrollOwner { get; set; } = null!;
    public void SetHorizontalOffset(double offset) { }
    public void SetVerticalOffset(double offset)
    {
        if (!double.IsFinite(offset)) offset = offset > 0 ? ExtentHeight : 0;
        offset = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(offset - _verticalOffset) < .01) return;
        _verticalOffset = offset;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }
    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is null || !IsAncestorOf(visual)) return Rect.Empty;
        var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
        var previousOffset = VerticalOffset;
        if (bounds.Top < 0) SetVerticalOffset(VerticalOffset + bounds.Top);
        else if (bounds.Bottom > ViewportHeight) SetVerticalOffset(VerticalOffset + bounds.Bottom - ViewportHeight);
        bounds.Offset(0, previousOffset - VerticalOffset);
        bounds.Intersect(new Rect(_viewport));
        return bounds;
    }
}
